using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
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
    ];

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private static readonly SpellLine Line = new("test", "test", "test", true);

    private sealed class Living : GameNPC
    {
        public override bool IsAlive => true;
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
        }

        Assert.That(exercised, Is.SupersetOf(new[]
        {
            eSpellType.AtlantisTabletMorph, eSpellType.DreamMorph, eSpellType.DreamGroupMorph,
            eSpellType.MaddeningScalars, eSpellType.TraitorsDaggerProc, eSpellType.ABSDamageShield,
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
}
