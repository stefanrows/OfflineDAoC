using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// The artifact spell handlers ported from OpenDAoC-Core (spells/Artifacts): every spell
/// type resolves to exactly one handler, none of them depends on the legacy
/// GameSpellEffect hooks our ECS never calls, and the ECS bridge applies and removes
/// their bonuses symmetrically.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_ArtifactSpellHandlers
{
    private static readonly eSpellType[] PortedTypes =
    [
        eSpellType.AllStatsDebuff, eSpellType.AllStatsPercentDebuff, eSpellType.AlvarusMorph,
        eSpellType.Arrogance, eSpellType.AtensShield, eSpellType.AtlantisTabletMorph,
        eSpellType.StarsProc, eSpellType.StarsProc2, eSpellType.BeltOfMoon, eSpellType.BeltOfSun,
        eSpellType.CeremonialBracerMezz, eSpellType.CeremonialBracerStun, eSpellType.CloudsongAura,
        eSpellType.CloudsongFall, eSpellType.LoreDebuff, eSpellType.StrengthConstitutionDrain,
        eSpellType.ABSDamageShield, eSpellType.Morph, eSpellType.ArcaneLeadership,
        eSpellType.EfficientHealing, eSpellType.EfficientEndurance, eSpellType.Powershield,
        eSpellType.DreamMorph, eSpellType.DreamGroupMorph, eSpellType.ShatterIllusions,
        eSpellType.HealFlask, eSpellType.DeadFlask, eSpellType.GoldenSpearJavelin,
        eSpellType.HarpyFeatherCloak, eSpellType.HpPwrEndRegen, eSpellType.MaddeningScalars,
        eSpellType.RealmLore, eSpellType.ScarabProc, eSpellType.ShadesOfMist, eSpellType.SnakeCharmer,
        eSpellType.StyleDmgAbs, eSpellType.Tartaros, eSpellType.TraitorsDaggerProc, eSpellType.DdtProcDd,
        eSpellType.TraitorsDaggerSummon, eSpellType.Traldor, eSpellType.ZoSummon, eSpellType.Bedazzlement,
        // WP3.0b: the types no project implemented, and AFHitsBuff for the Boon of Kings row.
        eSpellType.CrocodileTearsProc, eSpellType.PowerHealthEnduranceRegenBuff, eSpellType.UnyeldingProc,
        eSpellType.YouthIllness, eSpellType.ZahurAura, eSpellType.ZahurEnduProc, eSpellType.ZoAura, eSpellType.AFHitsBuff,
    ];

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private static readonly SpellLine Line = new("test", "test", "test", true);

    private sealed class Living : GameNPC
    {
        public override bool IsAlive => true;

        // In-memory pools: the real setters start regeneration timers and the real Change*
        // methods lock fields an uninitialized test object does not have.
        public int HealthValue;
        public int ManaValue;
        public int EnduranceValue;
        public override int Health { get => HealthValue; set => HealthValue = Math.Clamp(value, 0, MaxHealth); }
        public override int MaxHealth => 1000;
        public override int Mana { get => ManaValue; set => ManaValue = Math.Clamp(value, 0, MaxMana); }
        public override int MaxMana => 1000;
        public override int Endurance { get => EnduranceValue; set => EnduranceValue = Math.Clamp(value, 0, MaxEndurance); }
        public override int MaxEndurance => 100;

        public override int ChangeMana(GameObject changeSource, eManaChangeType manaChangeType, int changeAmount)
        {
            int old = Mana;
            Mana += changeAmount;
            return Mana - old;
        }

        public override int ChangeEndurance(GameObject changeSource, eEnduranceChangeType enduranceChangeType, int changeAmount)
        {
            int old = Endurance;
            Endurance += changeAmount;
            return Endurance - old;
        }
    }

    private sealed class Server : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private GameServer _previousServer;
    private Living _owner;

    [SetUp]
    public void Setup()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((Server) RuntimeHelpers.GetUninitializedObject(typeof(Server)));

        _owner = (Living) RuntimeHelpers.GetUninitializedObject(typeof(Living));
        _owner.ObjectState = GameObject.eObjectState.Active;
        typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(_owner, new ArrayList());
        _owner.effectListComponent = EffectListComponent.Create(_owner);
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", PrivateInstance).SetValue(_owner, new PropertyCollection());

        foreach (string category in new[] { "BaseBuffBonusCategory", "SpecBuffBonusCategory", "DebuffCategory", "SpecDebuffCategory", "OtherBonus", "AbilityBonus", "ItemBonus" })
            typeof(GameLiving).GetField($"<{category}>k__BackingField", PrivateInstance)?.SetValue(_owner, new PropertyIndexer());
    }

    [TearDown]
    public void Teardown()
    {
        if (_owner?.effectListComponent != null)
            ServiceObjectStore.Remove(_owner.effectListComponent);

        GameServer.LoadTestDouble(_previousServer);
    }

    private static List<Type> HandlersFor(eSpellType spellType)
    {
        return typeof(GameServer).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(ISpellHandler).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes<SpellHandlerAttribute>(false).Any(a => a.SpellType == spellType))
            .ToList();
    }

    private static Spell MakeSpell(eSpellType type, int value, int id, int lifeDrainReturn = 0)
    {
        return new Spell(new DbSpell
        {
            SpellID = id,
            Type = type.ToString(),
            Target = "Enemy",
            Duration = 60,
            Value = value,
            LifeDrainReturn = lifeDrainReturn,
        }, 50);
    }

    [Test]
    public void EveryPortedSpellTypeResolvesToExactlyOneHandler()
    {
        foreach (eSpellType spellType in PortedTypes)
        {
            List<Type> handlers = HandlersFor(spellType);
            Assert.That(handlers, Has.Count.EqualTo(1), $"{spellType}: {string.Join(", ", handlers.Select(h => h.FullName))}");
            Assert.That(handlers[0].Namespace, Does.StartWith("DOL.GS.Spells"), spellType.ToString());
            Assert.That(ScriptMgr.CacheSpellHandlerConstructor(spellType), Is.Not.Null,
                $"{spellType}: the registry must compile a (GameLiving, Spell, SpellLine) constructor");
        }
    }

    [Test]
    public void NoPortedHandlerReliesOnTheLegacyEffectHooks()
    {
        Type[] legacy = [typeof(GameSpellEffect)];
        Type[] legacyExpire = [typeof(GameSpellEffect), typeof(bool)];

        HashSet<Type> artifactTypes = PortedTypes.Select(t => HandlersFor(t).Single()).ToHashSet();
        artifactTypes.Add(typeof(ArtifactMorphProcSpellHandler));

        foreach (Type handler in artifactTypes)
        {
            MethodInfo[] hooks =
            [
                handler.GetMethod(nameof(SpellHandler.OnEffectStart), legacy),
                handler.GetMethod(nameof(SpellHandler.OnEffectExpires), legacyExpire),
                handler.GetMethod(nameof(SpellHandler.OnEffectPulse), legacy),
            ];

            // The ECS never calls these; an artifact override of them would be dead code.
            foreach (MethodInfo hook in hooks)
                Assert.That(artifactTypes.Contains(hook?.DeclaringType), Is.False, $"{handler.Name} overrides the legacy {hook?.Name}");
        }
    }

    [Test]
    public void AllStatsDebuffAppliesAndRemovesSymmetrically()
    {
        AllStatsDebuff handler = new(_owner, MakeSpell(eSpellType.AllStatsDebuff, 12, 991401), Line);
        ArtifactSpellEffect effect = new(new(_owner, 60_000, 1, handler));

        handler.OnArtifactEffectStart(effect);
        // Stats are stored negative like stock debuffs (StatCalculator reads Math.Abs);
        // absorption positive (its calculator subtracts the raw value).
        Assert.That(_owner.DebuffCategory[eProperty.Strength], Is.EqualTo(-12));
        Assert.That(_owner.DebuffCategory[eProperty.Acuity], Is.EqualTo(-12));
        Assert.That(_owner.DebuffCategory[eProperty.MagicAbsorption], Is.EqualTo(12));

        handler.OnArtifactEffectStop(effect);
        Assert.That(_owner.DebuffCategory[eProperty.Strength], Is.Zero);
        Assert.That(_owner.DebuffCategory[eProperty.Acuity], Is.Zero);
        Assert.That(_owner.DebuffCategory[eProperty.MagicAbsorption], Is.Zero);
    }

    [Test]
    public void ArtifactDebuffStacksWithAStockDebuffInsteadOfCancellingIt()
    {
        StrengthDebuff stock = new(_owner, MakeSpell(eSpellType.StrengthDebuff, 60, 991410), Line);
        AllStatsDebuff sleeves = new(_owner, MakeSpell(eSpellType.AllStatsDebuff, 12, 991411), Line);
        LoreDebuff lore = new(_owner, MakeSpell(eSpellType.LoreDebuff, 10, 991412), Line);

        ECSGameSpellEffect stockEffect = stock.CreateECSEffect(new(_owner, 60_000, 1, stock));
        ECSGameSpellEffect sleevesEffect = sleeves.CreateECSEffect(new(_owner, 60_000, 1, sleeves));
        ECSGameSpellEffect loreEffect = lore.CreateECSEffect(new(_owner, 60_000, 1, lore));
        _owner.effectListComponent.BeginTick();

        Assert.That(stockEffect.IsActive && sleevesEffect.IsActive && loreEffect.IsActive, Is.True);
        Assert.That(stockEffect, Is.InstanceOf<StatDebuffECSEffect>());
        // Shaman -60 strength plus Foppish Sleeves 12 must read as 72, not 48.
        Assert.That(Math.Abs(_owner.DebuffCategory[eProperty.Strength]), Is.EqualTo(72));
        Assert.That(_owner.DebuffCategory[eProperty.Resist_Heat], Is.EqualTo(-10));
        Assert.That(_owner.DebuffCategory[eProperty.SpellDamage], Is.EqualTo(-10));

        sleevesEffect.End();
        loreEffect.End();
        _owner.effectListComponent.BeginTick();
        Assert.That(Math.Abs(_owner.DebuffCategory[eProperty.Strength]), Is.EqualTo(60));
        Assert.That(_owner.DebuffCategory[eProperty.Resist_Heat], Is.Zero);
    }

    [Test]
    public void ArtifactDebuffsDoNotBlockOtherArtifactEffects()
    {
        HarpyFeatherCloak harpy = new(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991413), Line);
        TraldorSpellHandler traldor = new(_owner, MakeSpell(eSpellType.Traldor, 5, 991414), Line);
        ZoDebuffSpellHandler zo = new(_owner, MakeSpell(eSpellType.Bedazzlement, 10, 991415), Line);
        StrengthConstitutionDrain drain = new(_owner, MakeSpell(eSpellType.StrengthConstitutionDrain, 20, 991416), Line);

        ECSGameSpellEffect harpyEffect = harpy.CreateECSEffect(new(_owner, 60_000, 1, harpy));
        _owner.effectListComponent.BeginTick();
        ECSGameSpellEffect traldorEffect = traldor.CreateECSEffect(new(_owner, 60_000, 1, traldor));
        ECSGameSpellEffect zoEffect = zo.CreateECSEffect(new(_owner, 60_000, 1, zo));
        ECSGameSpellEffect drainEffect = drain.CreateECSEffect(new(_owner, 60_000, 1, drain));
        _owner.effectListComponent.BeginTick();

        Assert.That(harpyEffect.IsActive, Is.True, "Harpy must stay when Traldor lands");
        Assert.That(traldorEffect.IsActive, Is.True);
        Assert.That(zoEffect.IsActive, Is.True);
        Assert.That(drainEffect.IsActive, Is.True);
        Assert.That(_owner.DebuffCategory[eProperty.ToHitBonus], Is.EqualTo(10));
        Assert.That(_owner.SpecBuffBonusCategory[eProperty.SpellDamage], Is.EqualTo(5));
        Assert.That(_owner.DebuffCategory[eProperty.FumbleChance], Is.EqualTo(10));
        Assert.That(_owner.DebuffCategory[eProperty.Constitution], Is.EqualTo(-20));

        // A second cast of the same artifact debuff still conflicts.
        Assert.That(harpy.HasConflictingEffectWith(new HarpyFeatherCloak(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991417), Line)), Is.True);
        Assert.That(harpy.HasConflictingEffectWith(traldor), Is.False);
        Assert.That(zo.HasConflictingEffectWith(harpy), Is.False);
        Assert.That(drain.HasConflictingEffectWith(zo), Is.False);
    }

    [Test]
    public void ProcAndAblativeEffectsComeOnlyFromHandlersTheCoreCanCast()
    {
        // GameLiving hard-casts the handler of an OffensiveProc effect to
        // OffensiveProcSpellHandler and of an AblativeArmor effect to AblativeArmorSpellHandler.
        HashSet<eSpellType> exercised = new();

        foreach (eSpellType spellType in PortedTypes)
        {
            Spell spell = MakeSpell(spellType, 0, 991500 + (int) spellType);
            ISpellHandler handler;
            ECSGameSpellEffect effect;

            try
            {
                handler = ScriptMgr.CacheSpellHandlerConstructor(spellType)(_owner, spell, Line);
                effect = (handler as SpellHandler)?.CreateECSEffect(new(_owner, 60_000, 1, handler));
            }
            catch (Exception)
            {
                continue; // summon handlers need a world; they create no proc or ablative effect
            }

            exercised.Add(spellType);

            if (effect is ArtifactProcEffect)
                Assert.That(handler, Is.InstanceOf<OffensiveProcSpellHandler>(), spellType.ToString());

            if (effect is ArtifactAblativeEffect || effect?.EffectType is eEffect.AblativeArmor)
                Assert.That(handler, Is.InstanceOf<AblativeArmorSpellHandler>(), spellType.ToString());

            if (effect?.EffectType is eEffect.OffensiveProc)
                Assert.That(handler, Is.InstanceOf<OffensiveProcSpellHandler>(), spellType.ToString());

            // GameLiving.OnAttackedByEnemy hard-casts DefensiveProc handlers the same way.
            if (effect is ArtifactDefensiveProcEffect || effect?.EffectType is eEffect.DefensiveProc)
                Assert.That(handler, Is.InstanceOf<DefensiveProcSpellHandler>(), spellType.ToString());
        }

        Assert.That(exercised, Is.SupersetOf(new[]
        {
            eSpellType.AtlantisTabletMorph, eSpellType.DreamMorph, eSpellType.DreamGroupMorph,
            eSpellType.MaddeningScalars, eSpellType.TraitorsDaggerProc, eSpellType.ABSDamageShield,
            eSpellType.UnyeldingProc,
        }), "every handler that builds a proc or ablative effect must have been checked");
    }

    [Test]
    public void TraldorBuffsSpellDamageAndResistPierceThroughTheBridge()
    {
        TraldorSpellHandler handler = new(_owner, MakeSpell(eSpellType.Traldor, 5, 991402), Line);
        ArtifactSpellEffect effect = new(new(_owner, 60_000, 1, handler));

        // The stock StatBuffECSEffect would apply nothing: Traldor maps to eEffect.Unknown.
        Assert.That(effect.EffectType, Is.EqualTo(eEffect.Unknown));

        handler.OnArtifactEffectStart(effect);
        Assert.That(_owner.SpecBuffBonusCategory[eProperty.SpellDamage], Is.EqualTo(5));
        Assert.That(_owner.BaseBuffBonusCategory[eProperty.ResistPierce], Is.EqualTo(5));

        handler.OnArtifactEffectStop(effect);
        Assert.That(_owner.SpecBuffBonusCategory[eProperty.SpellDamage], Is.Zero);
        Assert.That(_owner.BaseBuffBonusCategory[eProperty.ResistPierce], Is.Zero);
    }

    [Test]
    public void HarpyAndZoDebuffsUsePositiveDebuffValues()
    {
        HarpyFeatherCloak harpy = new(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991403), Line);
        ZoDebuffSpellHandler zo = new(_owner, MakeSpell(eSpellType.Bedazzlement, 10, 991404), Line);
        ArtifactSpellEffect harpyEffect = new(new(_owner, 60_000, 1, harpy));
        ArtifactSpellEffect zoEffect = new(new(_owner, 60_000, 1, zo));

        harpy.OnArtifactEffectStart(harpyEffect);
        zo.OnArtifactEffectStart(zoEffect);

        // ToHitBonusCalculator and FumbleChanceCalculator subtract / scale a positive debuff.
        Assert.That(_owner.DebuffCategory[eProperty.ToHitBonus], Is.EqualTo(10));
        Assert.That(_owner.DebuffCategory[eProperty.FumbleChance], Is.EqualTo(10));
        Assert.That(_owner.DebuffCategory[eProperty.SpellFumbleChance], Is.EqualTo(10));

        harpy.OnArtifactEffectStop(harpyEffect);
        zo.OnArtifactEffectStop(zoEffect);
        Assert.That(_owner.DebuffCategory[eProperty.ToHitBonus], Is.Zero);
        Assert.That(_owner.DebuffCategory[eProperty.FumbleChance], Is.Zero);
        Assert.That(_owner.DebuffCategory[eProperty.SpellFumbleChance], Is.Zero);
    }

    [Test]
    public void ArtifactProcAndAblativeEffectsLandInTheBucketsTheCoreReads()
    {
        SpellHandler proc = new(_owner, MakeSpell(eSpellType.DreamMorph, 0, 991405), Line);
        SpellHandler ablative = new(_owner, MakeSpell(eSpellType.ABSDamageShield, 100, 991406), Line);

        Assert.That(new ArtifactProcEffect(new(_owner, 60_000, 1, proc)).EffectType, Is.EqualTo(eEffect.OffensiveProc),
            "GameLiving.OnAttackEnemy only fires procs found under eEffect.OffensiveProc");
        Assert.That(new ArtifactAblativeEffect(new(_owner, 60_000, 1, ablative)).EffectType, Is.EqualTo(eEffect.AblativeArmor),
            "The melee absorb code only reads eEffect.AblativeArmor");
    }

    [Test]
    public void ArtifactMorphsExcludeEachOtherButNotARefresh()
    {
        Spell shades = MakeSpell(eSpellType.ShadesOfMist, 0, 991407);
        Spell dream = MakeSpell(eSpellType.DreamMorph, 0, 991408);
        SpellHandler shadesHandler = new(_owner, shades, Line);
        ArtifactSpellEffect shadesEffect = new(new(_owner, 60_000, 1, shadesHandler));
        shadesEffect.FinalizeState(EffectListComponent.AddEffectResult.Added);

        Dictionary<eEffect, List<ECSGameEffect>> effects = (Dictionary<eEffect, List<ECSGameEffect>>)
            typeof(EffectListComponent).GetField("_effects", PrivateInstance).GetValue(_owner.effectListComponent);
        effects[eEffect.Unknown] = new() { shadesEffect };

        Assert.That(ArtifactSpells.HasOtherMorph(_owner, dream), Is.True);
        Assert.That(ArtifactSpells.HasOtherMorph(_owner, shades), Is.False, "Recasting the same form is a refresh");
        Assert.That(ArtifactSpells.FindEffect(_owner, eSpellType.ShadesOfMist), Is.SameAs(shadesEffect));
        Assert.That(ArtifactSpells.FindEffect(_owner, eSpellType.CloudsongFall), Is.Null);
    }

    [Test]
    public void SummonedItemIdsMatchIgnoringCase()
    {
        IReadOnlySet<string> ids = ArtifactSpells.ItemIds("Sun_Crush", "Moon_Staff");
        Assert.That(ids.Contains("sun_crush"), Is.True);
        Assert.That(ids.Contains("MOON_STAFF"), Is.True);
        Assert.That(ids.Contains("Sun_Bow"), Is.False);
    }

    // WP3.0b ---------------------------------------------------------------------------------

    private static Spell MakeSelfSpell(eSpellType type, int id, int value, int duration = 600, int frequency = 0, int damage = 0)
    {
        return new Spell(new DbSpell
        {
            SpellID = id,
            Type = type.ToString(),
            Target = "Self",
            Duration = duration,
            Frequency = frequency,
            Value = value,
            Damage = damage,
        }, 50);
    }

    private AttackData Hit(GameLiving attacker, eAttackResult result, int damage)
    {
        return new AttackData
        {
            Attacker = attacker,
            Target = _owner,
            AttackType = AttackData.eAttackType.MeleeOneHand,
            AttackResult = result,
            Damage = damage,
        };
    }

    [Test]
    public void CrocodileTearsConvertsPartOfEveryHitWhileItLasts()
    {
        CrocodileTearsProc handler = new(_owner, MakeSelfSpell(eSpellType.CrocodileTearsProc, 991601, 5), Line);
        ArtifactSpellEffect effect = new(new(_owner, 600_000, 1, handler));
        Living attacker = (Living) RuntimeHelpers.GetUninitializedObject(typeof(Living));

        handler.OnArtifactEffectStart(effect);

        try
        {
            AttackData hit = Hit(attacker, eAttackResult.HitUnstyled, 200);
            GameEventMgr.Notify(GameLivingEvent.AttackedByEnemy, _owner, new AttackedByEnemyEventArgs(hit));
            // GameLiving raises AttackedByEnemy before DealDamage, so the hit itself shrinks.
            Assert.That(hit.Damage, Is.EqualTo(190));
            Assert.That(_owner.Mana, Is.EqualTo(10));
            Assert.That(_owner.Endurance, Is.EqualTo(10));

            AttackData miss = Hit(attacker, eAttackResult.Missed, 200);
            GameEventMgr.Notify(GameLivingEvent.AttackedByEnemy, _owner, new AttackedByEnemyEventArgs(miss));
            Assert.That(miss.Damage, Is.EqualTo(200), "a miss converts nothing");
            Assert.That(_owner.Mana, Is.EqualTo(10));
        }
        finally
        {
            handler.OnArtifactEffectStop(effect);
        }

        AttackData after = Hit(attacker, eAttackResult.HitUnstyled, 200);
        GameEventMgr.Notify(GameLivingEvent.AttackedByEnemy, _owner, new AttackedByEnemyEventArgs(after));
        Assert.That(after.Damage, Is.EqualTo(200), "the conversion ends with the effect");
        Assert.That(_owner.Mana, Is.EqualTo(10));
    }

    [Test]
    public void KalareRegenerationRestoresAShareOfEveryPoolEachPulse()
    {
        PowerHealthEnduranceRegenBuff handler = new(_owner, MakeSelfSpell(eSpellType.PowerHealthEnduranceRegenBuff, 991602, -5, 30, 50), Line);
        ECSGameSpellEffect effect = handler.CreateECSEffect(new(_owner, 30_000, 1, handler));
        Assert.That(effect, Is.InstanceOf<ArtifactSpellEffect>());
        Assert.That(effect.PulseFreq, Is.EqualTo(5000), "Frequency 50 is one pulse every five seconds");

        _owner.HealthValue = 500;
        _owner.ManaValue = 0;
        _owner.EnduranceValue = 10;
        effect.OnEffectPulse();
        Assert.That(_owner.Health, Is.EqualTo(550));
        Assert.That(_owner.Mana, Is.EqualTo(50));
        Assert.That(_owner.Endurance, Is.EqualTo(15));

        _owner.HealthValue = 990;
        effect.OnEffectPulse();
        Assert.That(_owner.Health, Is.EqualTo(1000), "never above the maximum");

        PowerHealthEnduranceRegenBuff flat = new(_owner, MakeSelfSpell(eSpellType.PowerHealthEnduranceRegenBuff, 991603, 20, 30, 50), Line);
        Assert.That(flat.Amount(1000), Is.EqualTo(20), "a positive value is points, not percent");
    }

    [Test]
    public void UnyieldingWillIsADefensiveProcTheCoreCanFire()
    {
        UnyeldingProc handler = new(_owner, MakeSelfSpell(eSpellType.UnyeldingProc, 991604, 991605, 600, 20, 991606), Line);
        ECSGameSpellEffect effect = handler.CreateECSEffect(new(_owner, 600_000, 1, handler));

        Assert.That(handler, Is.InstanceOf<DefensiveProcSpellHandler>(), "GameLiving casts DefensiveProc handlers to this type");
        Assert.That(effect, Is.InstanceOf<ArtifactDefensiveProcEffect>());
        Assert.That(effect.EffectType, Is.EqualTo(eEffect.DefensiveProc), "the core only fires procs found under eEffect.DefensiveProc");

        MethodInfo baseHandler = typeof(DefensiveProcSpellHandler).GetMethod(nameof(DefensiveProcSpellHandler.EventHandler), [typeof(AttackData)]);
        MethodInfo ringHandler = typeof(UnyeldingProc).GetMethod(nameof(DefensiveProcSpellHandler.EventHandler), [typeof(AttackData)]);
        Assert.That(baseHandler.IsVirtual, Is.True);
        Assert.That(ringHandler.DeclaringType, Is.EqualTo(typeof(UnyeldingProc)), "the ring casts both of its spells from one roll");

        Living attacker = (Living) RuntimeHelpers.GetUninitializedObject(typeof(Living));
        Assert.DoesNotThrow(() => handler.EventHandler(Hit(attacker, eAttackResult.Missed, 0)), "a miss never procs");
    }

    [Test]
    public void RecoveryCuresResurrectionIllnessAndIsRefusedWithoutIt()
    {
        YouthIllness recovery = new(_owner, MakeSelfSpell(eSpellType.YouthIllness, 991607, 0, 0), Line);
        Assert.That(YouthIllness.HasResurrectionIllness(_owner), Is.False);
        Assert.That(recovery.CheckBeginCast(_owner, true), Is.False, "no illness, so the charge is not spent");

        SpellHandler sickness = new(_owner, MakeSelfSpell(eSpellType.PveResurrectionIllness, 991608, 50, 300), Line);
        ECSGameSpellEffect illness = ECSGameEffectFactory.Create(new(_owner, 300_000, 1, sickness), static (in i) => new ResurrectionIllnessECSGameEffect(i));
        _owner.effectListComponent.BeginTick();
        Assert.That(illness.EffectType, Is.EqualTo(eEffect.ResurrectionIllness));
        Assert.That(YouthIllness.HasResurrectionIllness(_owner), Is.True);

        recovery.OnDirectEffect(_owner);
        _owner.effectListComponent.BeginTick();
        Assert.That(YouthIllness.HasResurrectionIllness(_owner), Is.False);
    }

    [Test]
    public void ZahurAuraBuffsSpellRangeAndResistPierceSymmetrically()
    {
        ZahurAura handler = new(_owner, MakeSelfSpell(eSpellType.ZahurAura, 991609, 5, 5), Line);
        ECSGameSpellEffect effect = handler.CreateECSEffect(new(_owner, 5_000, 1, handler));
        Assert.That(effect, Is.InstanceOf<ArtifactSpellEffect>());

        handler.OnArtifactEffectStart(effect);
        Assert.That(_owner.SpecBuffBonusCategory[eProperty.SpellRange], Is.EqualTo(5));
        Assert.That(_owner.BaseBuffBonusCategory[eProperty.ResistPierce], Is.EqualTo(5));

        handler.OnArtifactEffectStop(effect);
        Assert.That(_owner.SpecBuffBonusCategory[eProperty.SpellRange], Is.Zero);
        Assert.That(_owner.BaseBuffBonusCategory[eProperty.ResistPierce], Is.Zero);
    }

    [Test]
    public void EfficientEnduranceTurnsPartOfAHealIntoEndurance()
    {
        Assert.That(ZahurEnduProc.OnHealReceived(_owner, 400), Is.Zero, "nothing without the effect");

        ZahurEnduProc handler = new(_owner, MakeSelfSpell(eSpellType.ZahurEnduProc, 991610, 10), Line);
        ECSGameSpellEffect effect = handler.CreateECSEffect(new(_owner, 600_000, 1, handler));
        _owner.effectListComponent.BeginTick();
        Assert.That(effect.IsActive, Is.True);

        _owner.EnduranceValue = 20;
        Assert.That(ZahurEnduProc.OnHealReceived(_owner, 400), Is.EqualTo(40), "10% of a 400 heal");
        Assert.That(_owner.Endurance, Is.EqualTo(60));
        Assert.That(ZahurEnduProc.OnHealReceived(_owner, 900), Is.EqualTo(40), "capped at the endurance pool");

        effect.End();
        _owner.effectListComponent.BeginTick();
        _owner.EnduranceValue = 0;
        Assert.That(ZahurEnduProc.OnHealReceived(_owner, 400), Is.Zero);
    }

    [Test]
    public void ZoAuraIsTheAreaBedazzlementAndDoesNotStackWithIt()
    {
        ZoAuraSpellHandler aura = new(_owner, MakeSpell(eSpellType.ZoAura, 25, 991611), Line);
        ZoDebuffSpellHandler band = new(_owner, MakeSpell(eSpellType.Bedazzlement, 25, 991612), Line);
        HarpyFeatherCloak harpy = new(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991613), Line);
        ArtifactSpellEffect effect = new(new(_owner, 30_000, 1, aura));

        aura.OnArtifactEffectStart(effect);
        Assert.That(_owner.DebuffCategory[eProperty.FumbleChance], Is.EqualTo(25));
        Assert.That(_owner.DebuffCategory[eProperty.SpellFumbleChance], Is.EqualTo(25));
        aura.OnArtifactEffectStop(effect);
        Assert.That(_owner.DebuffCategory[eProperty.FumbleChance], Is.Zero);
        Assert.That(_owner.DebuffCategory[eProperty.SpellFumbleChance], Is.Zero);

        Assert.That(aura.HasConflictingEffectWith(band), Is.True);
        Assert.That(band.HasConflictingEffectWith(aura), Is.True, "the effect list asks the existing effect's handler");
        Assert.That(aura.HasConflictingEffectWith(harpy), Is.False);
        Assert.That(harpy.HasConflictingEffectWith(aura), Is.False);
    }

    [Test]
    public void BoonOfKingsRowsReachTheEcsArmorAndHealthHandler()
    {
        // The case-insensitive parse turns the old registration's "AfHitsBuff" into AFHitsBuff.
        Spell lower = new(new DbSpell { SpellID = 991614, Type = "AfHitsBuff", Target = "Realm", Duration = 5, Value = 20 }, 50);
        Spell upper = new(new DbSpell { SpellID = 991615, Type = "AFHitsBuff", Target = "Realm", Duration = 5, Value = 20 }, 50);
        Assert.That(lower.SpellType, Is.EqualTo(eSpellType.AFHitsBuff));
        Assert.That(upper.SpellType, Is.EqualTo(eSpellType.AFHitsBuff));
        Assert.That(ScriptMgr.CacheSpellHandlerConstructor(eSpellType.AFHitsBuff)(_owner, upper, Line), Is.InstanceOf<AfHitsBuffSpellHandler>());

        AfHitsBuffSpellHandler handler = new(_owner, upper, Line);
        ECSGameSpellEffect first = handler.CreateECSEffect(new(_owner, 5_000, 1, handler));
        ECSGameSpellEffect renewed = handler.CreateECSEffect(new(_owner, 5_000, 1, handler));
        Assert.That(first, Is.InstanceOf<ArtifactSpellEffect>());

        handler.OnArtifactEffectStart(first);
        // Refreshing an ending effect starts the new instance before the old one stops
        // (EffectListComponent, IsEnding path): no double bonus, and none lost.
        handler.OnArtifactEffectStart(renewed);
        Assert.That(_owner.OtherBonus[eProperty.ArmorFactor], Is.EqualTo(20), "not added twice");
        Assert.That(_owner.AbilityBonus[eProperty.MaxHealth], Is.EqualTo(20));

        handler.OnArtifactEffectStop(first);
        Assert.That(_owner.OtherBonus[eProperty.ArmorFactor], Is.EqualTo(20), "the renewed effect still holds the bonus");
        Assert.That(_owner.AbilityBonus[eProperty.MaxHealth], Is.EqualTo(20));

        handler.OnArtifactEffectStop(renewed);
        Assert.That(_owner.OtherBonus[eProperty.ArmorFactor], Is.Zero, "the last stop removes it");
        Assert.That(_owner.AbilityBonus[eProperty.MaxHealth], Is.Zero);
        handler.OnArtifactEffectStop(renewed);
        Assert.That(_owner.OtherBonus[eProperty.ArmorFactor], Is.Zero, "removed once, not twice");

        // A swap without stop or start (pulse refresh) leaves one holder; its stop removes the bonus.
        handler.OnArtifactEffectStart(first);
        handler.OnArtifactEffectStop(renewed);
        Assert.That(_owner.OtherBonus[eProperty.ArmorFactor], Is.Zero);
    }

    private sealed class RecordingHandler : SpellHandler
    {
        public readonly List<GameLiving> Started = new();

        public RecordingHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override bool StartSpell(GameLiving target)
        {
            Started.Add(target);
            return true;
        }
    }

    private sealed class RecordingHarpy : HarpyFeatherCloak
    {
        public RecordingHandler SubHandler;
        public ISpellHandler StockSubHandler;

        public RecordingHarpy(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        protected override ISpellHandler CreateSubSpellHandler(Spell subSpell)
        {
            StockSubHandler = base.CreateSubSpellHandler(subSpell);
            SubHandler = new RecordingHandler(Caster, subSpell, Line);
            return SubHandler;
        }
    }

    [Test]
    public void DisorientedCastsItsSubSpellOnTheTarget()
    {
        // The core's CastSubSpells never casts a plain SubSpellID (its guard sees the parent's
        // SubSpellID); the artifact to-hit debuff casts it itself.
        Dictionary<int, Spell> index = (Dictionary<int, Spell>) typeof(SkillBase)
            .GetField("m_spellIndex", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Spell dot = new(new DbSpell { SpellID = 991630, Type = "DamageOverTime", Target = "Enemy", Duration = 25, Frequency = 50, Damage = 20, DamageType = 12, Radius = 700 }, 1);
        Spell chained = new(new DbSpell { SpellID = 991631, Type = "DamageOverTime", Target = "Enemy", SubSpellID = 991630 }, 1);
        index[991630] = dot;
        index[991631] = chained;

        try
        {
            Living target = (Living) RuntimeHelpers.GetUninitializedObject(typeof(Living));
            Spell disoriented = new(new DbSpell { SpellID = 991632, Type = "HarpyFeatherCloak", Target = "Enemy", Duration = 60, Value = 10, Range = 1500, Radius = 700, SubSpellID = 991630 }, 50);
            RecordingHarpy harpy = new(_owner, disoriented, Line);

            harpy.CastSubSpells(target);
            Assert.That(harpy.SubHandler, Is.Not.Null, "the sub spell is cast");
            Assert.That(harpy.SubHandler.Spell.ID, Is.EqualTo(991630));
            Assert.That(harpy.SubHandler.Started, Is.EqualTo(new[] { target }), "on the spell's target, once");
            Assert.That(harpy.StockSubHandler, Is.InstanceOf<DoTSpellHandler>(), "through the stock handler of the sub spell's type");

            RecordingHarpy noSub = new(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991633), Line);
            noSub.CastSubSpells(target);
            Assert.That(noSub.SubHandler, Is.Null, "no SubSpellID, nothing cast");

            Spell chainParent = new(new DbSpell { SpellID = 991634, Type = "HarpyFeatherCloak", Target = "Enemy", Duration = 60, Value = 10, SubSpellID = 991631 }, 50);
            RecordingHarpy chain = new(_owner, chainParent, Line);
            chain.CastSubSpells(target);
            Assert.That(chain.SubHandler, Is.Null, "no chains, as the core");
        }
        finally
        {
            index.Remove(991630);
            index.Remove(991631);
        }
    }

    [Test]
    public void DexConDebuffHasItsOwnEffectAndNoLongerBlocksArtifactEffects()
    {
        Spell poison = MakeSpell(eSpellType.DexterityConstitutionDebuff, 50, 991616);
        Assert.That(EffectHelper.GetEffectFromSpell(poison), Is.EqualTo(eEffect.DexConDebuff));
        Assert.That(EffectHelper.GetPropertiesFromEffect(eEffect.DexConDebuff), Is.EquivalentTo(new[] { eProperty.Dexterity, eProperty.Constitution }));

        // Inhibiting poison first, then two artifact effects (which share eEffect.Unknown).
        DexterityConDebuff inhibit = new(_owner, poison, Line);
        HarpyFeatherCloak harpy = new(_owner, MakeSpell(eSpellType.HarpyFeatherCloak, 10, 991617), Line);
        TraldorSpellHandler traldor = new(_owner, MakeSpell(eSpellType.Traldor, 5, 991618), Line);

        ECSGameSpellEffect inhibitEffect = inhibit.CreateECSEffect(new(_owner, 60_000, 1, inhibit));
        _owner.effectListComponent.BeginTick();
        ECSGameSpellEffect harpyEffect = harpy.CreateECSEffect(new(_owner, 60_000, 1, harpy));
        ECSGameSpellEffect traldorEffect = traldor.CreateECSEffect(new(_owner, 60_000, 1, traldor));
        _owner.effectListComponent.BeginTick();

        Assert.That(inhibitEffect.IsActive && harpyEffect.IsActive && traldorEffect.IsActive, Is.True);
        // The stock debuff now applies its stats (stored negative like every stock debuff).
        Assert.That(_owner.DebuffCategory[eProperty.Dexterity], Is.EqualTo(-50));
        Assert.That(_owner.DebuffCategory[eProperty.Constitution], Is.EqualTo(-50));
        Assert.That(_owner.DebuffCategory[eProperty.ToHitBonus], Is.EqualTo(10));

        // And the other order: an artifact effect is already there when the poison lands.
        inhibitEffect.End();
        _owner.effectListComponent.BeginTick();
        Assert.That(_owner.DebuffCategory[eProperty.Dexterity], Is.Zero);
        DexterityConDebuff second = new(_owner, MakeSpell(eSpellType.DexterityConstitutionDebuff, 66, 991619), Line);
        ECSGameSpellEffect again = second.CreateECSEffect(new(_owner, 60_000, 1, second));
        _owner.effectListComponent.BeginTick();
        Assert.That(harpyEffect.IsActive && traldorEffect.IsActive, Is.True, "the poison does not wipe artifact effects");
        Assert.That(again.IsActive, Is.True);

        // Two inhibiting poisons still compete with each other.
        Assert.That(inhibit.HasConflictingEffectWith(new DexterityConDebuff(_owner, MakeSpell(eSpellType.DexterityConstitutionDebuff, 66, 991620), Line)), Is.True);
    }
}
