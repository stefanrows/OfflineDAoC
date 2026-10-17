using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using DOL.GS.RealmAbilities;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Only the main Animist shroom follows its owner (field turrets stay planted), and every shroom
/// inherits its direct owner's realm ability bonuses. The owner is a player or a companion bot; a
/// companion's human owner must never be the source of the bonuses.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_AnimistShroomFollowAndRealmAbilities
{
    private bool _previousExpandWildMinion;
    private static readonly List<GameLiving> Actors = new();

    private sealed class Player : GamePlayer
    {
        private Player() : base(null, null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassAnimist();
    }

    private sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassAnimist();
        public void SetHumanOwner(GamePlayer owner) => typeof(GameBot)
            .GetField("<Owner>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, owner);
    }

    /// <summary>Records the movement orders the brain gives instead of moving.</summary>
    private sealed class MainShroom : TurretMainPetCaster
    {
        private MainShroom() : base((INpcTemplate)null) { }
        private List<string> _orders;
        public List<string> Orders => _orders ??= new(); // Field initializers do not run on the uninitialized test actors.
        public override int X => 0;
        public override int Y => 0;
        public override int Z => 0;
        public override bool IsAttacking => false;
        public override short MaxSpeed => 200;
        public override void Follow(GameObject target, int minDistance, int maxDistance) => Orders.Add("follow");
        public override void StopFollowing() => Orders.Add("stopfollowing");
        public override void StopMoving() => Orders.Add("stopmoving");
        public override void PathTo(Vector3 target, short speed) => Orders.Add("pathto");
    }

    private sealed class FieldShroom : TurretFnfPet
    {
        private FieldShroom() : base((INpcTemplate)null) { }
        private List<string> _orders;
        public List<string> Orders => _orders ??= new(); // Field initializers do not run on the uninitialized test actors.
        public override int X => 0;
        public override int Y => 0;
        public override int Z => 0;
        public override bool IsAttacking => false;
        public override short MaxSpeed => 200;
        public override void Follow(GameObject target, int minDistance, int maxDistance) => Orders.Add("follow");
        public override void StopFollowing() => Orders.Add("stopfollowing");
        public override void StopMoving() => Orders.Add("stopmoving");
        public override void PathTo(Vector3 target, short speed) => Orders.Add("pathto");
    }

    [SetUp]
    public void SetUp()
    {
        _previousExpandWildMinion = DOL.GS.ServerProperties.Properties.EXPAND_WILD_MINION;
        DOL.GS.ServerProperties.Properties.EXPAND_WILD_MINION = false; // The shipped default: pets get Wild Minion for melee only.
    }

    [TearDown]
    public void TearDown()
    {
        DOL.GS.ServerProperties.Properties.EXPAND_WILD_MINION = _previousExpandWildMinion;
        foreach (GameLiving actor in Actors)
            ServiceObjectStore.Remove(actor.effectListComponent);
        Actors.Clear();
    }

    #region Follow

    [TestCase(false)]
    [TestCase(true)]
    public void MainShroomFollowsItsOwnerPlayerOrCompanion(bool companion)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        MainShroom shroom = Actor<MainShroom>();
        var brain = new TurretMainPetCasterBrain(owner) { Body = shroom };

        brain.FollowOwner();
        Assert.That(shroom.Orders, Is.EqualTo(new[] { "follow" }), "Animist players were excluded from FollowOwner by the base brain");

        shroom.Orders.Clear();
        brain.Follow(owner);
        Assert.That(brain.WalkState, Is.EqualTo(eWalkState.Follow));
        Assert.That(shroom.Orders, Is.EqualTo(new[] { "follow" }));
    }

    [Test]
    public void MainShroomHonoursStayAndComeHere()
    {
        MainShroom shroom = Actor<MainShroom>();
        var brain = new TurretMainPetTankBrain(Actor<Player>()) { Body = shroom };

        brain.Stay();
        Assert.That(brain.WalkState, Is.EqualTo(eWalkState.Stay));
        Assert.That(shroom.Orders, Does.Contain("stopmoving"));

        shroom.Orders.Clear();
        brain.ComeHere();
        Assert.That(brain.WalkState, Is.EqualTo(eWalkState.ComeHere));
        Assert.That(shroom.Orders, Does.Contain("pathto"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FieldShroomNeverMoves(bool companion)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        FieldShroom shroom = Actor<FieldShroom>();

        // The summon spell starts the brain while IsMainPet is still true and clears it afterwards.
        foreach (bool isMainPet in new[] { true, false })
        {
            var brain = new TurretFNFBrain(owner) { Body = shroom, IsMainPet = isMainPet };
            brain.FollowOwner();
            brain.Follow(owner);
            brain.Goto(owner);
            brain.ComeHere();
            brain.Stay();
        }

        Assert.That(shroom.Orders, Is.Empty);
    }

    [Test]
    public void MainShroomMovesAtAPetSpeedUntilTheTemplateGivesOne()
    {
        // The Animist templates have MaxSpeed 0, which also blocks Follow() for NPCs (IsAllowedToFollow).
        var template = new NpcTemplate(new DbNpcTemplate { Name = "forest's servant", Model = "860", Size = "50", MaxSpeed = 0 });
        var main = new TurretMainPetCaster(template);
        var tank = new TurretMainPetTank(template);
        var field = new TurretFnfPet(template);

        Assert.Multiple(() =>
        {
            Assert.That(main.MaxSpeedBase, Is.EqualTo(TurretPet.MainPetMaxSpeedBase));
            Assert.That(tank.MaxSpeedBase, Is.EqualTo(TurretPet.MainPetMaxSpeedBase));
            Assert.That(field.MaxSpeedBase, Is.Zero, "field turrets stay immobile");
        });
    }

    #endregion

    #region Realm abilities

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void EveryShroomInheritsWildPowerAndWildMinionForSpells(bool companion, bool fieldShroom)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        Learn(owner, new AtlasOF_WildPowerAbility(Db("WildPower"), 2));   // 10%
        Learn(owner, new AtlasOF_WildMinionAbility(Db("WildMinion"), 1)); // 5%
        TurretPet shroom = Shroom(owner, fieldShroom);

        int chance = new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance);

        Assert.That(chance, Is.EqualTo(15), "Wild Power + Wild Minion, no +10 list-caster bonus, even with EXPAND_WILD_MINION off");
    }

    [Test]
    public void SpellCritStaysCappedAndIsZeroWithoutAnAnimistRealmAbility()
    {
        Player owner = Actor<Player>();
        TurretPet shroom = Shroom(owner, false);
        Assert.That(new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance), Is.Zero);

        Learn(owner, new AtlasOF_WildPowerAbility(Db("WildPower"), 5));
        Learn(owner, new AtlasOF_WildMinionAbility(Db("WildMinion"), 5));
        Assert.That(new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance), Is.EqualTo(50));
    }

    [Test]
    public void ShroomsOwnBonusesStillCountAndInheritedOnesAreAdditive()
    {
        Player owner = Actor<Player>();
        Learn(owner, new AtlasOF_WildPowerAbility(Db("WildPower"), 1)); // 5%
        TurretPet shroom = Shroom(owner, false);
        shroom.AbilityBonus[eProperty.CriticalSpellHitChance] = 3;

        Assert.That(new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance), Is.EqualTo(8));
    }

    [Test]
    public void CompanionShroomUsesTheCompanionsAbilitiesNotItsHumanOwners()
    {
        Player human = Actor<Player>();
        Learn(human, new AtlasOF_WildMinionAbility(Db("WildMinion"), 5)); // 25%, must not leak
        Learn(human, new AtlasOF_WildPowerAbility(Db("WildPower"), 5));
        Bot companion = Actor<Bot>();
        companion.SetHumanOwner(human);
        Learn(companion, new AtlasOF_WildMinionAbility(Db("WildMinion"), 1)); // 5%
        TurretPet shroom = Shroom(companion, false);

        Assert.Multiple(() =>
        {
            Assert.That(new CriticalMeleeHitChanceCalculator().CalcValue(shroom, eProperty.CriticalMeleeHitChance), Is.EqualTo(5));
            Assert.That(new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance), Is.EqualTo(5));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShroomMeleeCritUsesWildMinionOfThePlayerOrCompanion(bool companion)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        Learn(owner, new AtlasOF_WildMinionAbility(Db("WildMinion"), 3)); // 15%
        TurretPet shroom = Shroom(owner, false);

        Assert.That(new CriticalMeleeHitChanceCalculator().CalcValue(shroom, eProperty.CriticalMeleeHitChance), Is.EqualTo(15));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShroomInheritsWildArcanaForDotsAndDebuffs(bool companion)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        Learn(owner, new AtlasOF_WildArcanaAbility(Db("WildArcana"), 2)); // 10%
        TurretPet shroom = Shroom(owner, true);

        Assert.That(new CriticalDebuffHitChanceCalculator().CalcValue(shroom, eProperty.CriticalDebuffHitChance), Is.EqualTo(10));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShroomInheritsMasteryOfMageryAndMasteryOfTheArt(bool companion)
    {
        GameLiving owner = companion ? Actor<Bot>() : Actor<Player>();
        Learn(owner, new AtlasOF_MasteryOfMagery(Db("MasteryOfMagery"), 2)); // 6%
        Learn(owner, new AtlasOF_MasteryOfTheArt(Db("MasteryOfTheArt"), 2)); // 6%
        TurretPet shroom = Shroom(owner, false);
        TurretPet field = Shroom(owner, true);

        Assert.Multiple(() =>
        {
            Assert.That(new SpellDamagePercentCalculator().CalcValue(shroom, eProperty.SpellDamage), Is.EqualTo(6));
            Assert.That(new SpellDamagePercentCalculator().CalcValue(field, eProperty.SpellDamage), Is.EqualTo(6));
            Assert.That(new SpellCastSpeedPercentCalculator().CalcValue(shroom, eProperty.CastingSpeed), Is.EqualTo(6));
            Assert.That(new SpellCastSpeedPercentCalculator().CalcValue(field, eProperty.CastingSpeed), Is.EqualTo(6));
        });
    }

    [Test]
    public void InheritedCastingSpeedKeepsTheFifteenPercentAbilityCap()
    {
        Player owner = Actor<Player>();
        owner.AbilityBonus[eProperty.CastingSpeed] = 25;
        TurretPet shroom = Shroom(owner, false);

        Assert.That(new SpellCastSpeedPercentCalculator().CalcValue(shroom, eProperty.CastingSpeed), Is.EqualTo(15));
    }

    [Test]
    public void ShroomInheritsMasteryOfTheArcaneAndWildHealingWithTheirCaps()
    {
        Player owner = Actor<Player>();
        owner.AbilityBonus[eProperty.BuffEffectiveness] = 30;
        owner.AbilityBonus[eProperty.CriticalHealHitChance] = 60;
        TurretPet shroom = Shroom(owner, false);

        Assert.Multiple(() =>
        {
            Assert.That(new BuffEffectivenessPercentCalculator().CalcValue(shroom, eProperty.BuffEffectiveness), Is.EqualTo(25));
            Assert.That(new CriticalHealHitChanceCalculator().CalcValue(shroom, eProperty.CriticalHealHitChance), Is.EqualTo(50));
        });
    }

    [Test]
    public void OwnersItemBonusesAndOtherPetsAreNotTouched()
    {
        Player owner = Actor<Player>();
        owner.ItemBonus[eProperty.SpellDamage] = 10;
        owner.ItemBonus[eProperty.CastingSpeed] = 10;
        owner.ItemBonus[eProperty.CriticalSpellHitChance] = 10;
        TurretPet shroom = Shroom(owner, false);

        // An ordinary pet of the same owner does not inherit anything either.
        GameSummonedPet ordinary = Actor<GameSummonedPet>();
        SetBrain(ordinary, new ControlledMobBrain(owner) { Body = ordinary });
        owner.AbilityBonus[eProperty.SpellDamage] = 6;

        Assert.Multiple(() =>
        {
            Assert.That(new SpellDamagePercentCalculator().CalcValue(shroom, eProperty.SpellDamage), Is.EqualTo(6), "ability bonus only, no ToA/item bonus");
            Assert.That(new SpellCastSpeedPercentCalculator().CalcValue(shroom, eProperty.CastingSpeed), Is.Zero);
            Assert.That(new CriticalSpellHitChanceCalculator().CalcValue(shroom, eProperty.CriticalSpellHitChance), Is.Zero);
            Assert.That(new SpellDamagePercentCalculator().CalcValue(ordinary, eProperty.SpellDamage), Is.Zero);
        });
    }

    [Test]
    public void RealmAbilityOwnerIsTheDirectOwnerOnly()
    {
        Player player = Actor<Player>();
        Bot bot = Actor<Bot>();
        bot.SetHumanOwner(player);

        Assert.Multiple(() =>
        {
            Assert.That(TurretPet.GetRealmAbilityOwner(Shroom(player, false)), Is.SameAs(player));
            Assert.That(TurretPet.GetRealmAbilityOwner(Shroom(bot, true)), Is.SameAs(bot), "not the bot's human owner");
            Assert.That(TurretPet.GetRealmAbilityOwner(Actor<Player>()), Is.Null);
            Assert.That(TurretPet.GetRealmAbilityOwner(null), Is.Null);
        });
    }

    #endregion

    private static DbAbility Db(string key) => new() { KeyName = key, Name = key };

    private static void Learn(GameLiving owner, RealmAbility ability)
    {
        // Same path a character takes when the ability is trained: Activate adds the amount to AbilityBonus.
        ((Dictionary<string, Ability>)typeof(GameLiving).GetField("m_abilities", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(owner))[ability.KeyName] = ability;
        ability.Activate(owner, false);
    }

    private static TurretPet Shroom(GameLiving owner, bool field)
    {
        TurretPet shroom = field ? Actor<FieldShroom>() : Actor<MainShroom>();
        SetBrain(shroom, field
            ? new TurretFNFBrain(owner) { Body = shroom, IsMainPet = false }
            : new TurretMainPetCasterBrain(owner) { Body = shroom });
        return shroom;
    }

    private static void SetBrain(GameNPC npc, ABrain brain) => typeof(GameNPC)
        .GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, brain);

    private static T Actor<T>() where T : GameLiving
    {
        T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        void Set(string name, object value) => typeof(GameLiving)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, value);
        Set("<BaseBuffBonusCategory>k__BackingField", new PropertyIndexer());
        Set("<SpecBuffBonusCategory>k__BackingField", new PropertyIndexer());
        Set("<ItemBonus>k__BackingField", new PropertyIndexer());
        Set("<AbilityBonus>k__BackingField", new PropertyIndexer());
        Set("<OtherBonus>k__BackingField", new PropertyIndexer());
        Set("<DebuffCategory>k__BackingField", new PropertyIndexer());
        Set("<SpecDebuffCategory>k__BackingField", new PropertyIndexer());
        Set("m_abilities", new Dictionary<string, Ability>());
        Set("_abilitiesLock", new Lock());
        Set("m_charStat", new short[8]);
        actor.effectListComponent = EffectListComponent.Create(actor); // Crit calculators look for Berserk.
        Actors.Add(actor);
        if (actor is GameNPC npc)
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, new ArrayList());
        return actor;
    }
}
