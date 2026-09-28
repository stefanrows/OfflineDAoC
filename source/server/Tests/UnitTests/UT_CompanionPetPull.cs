using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
[NonParallelizable]
public sealed class UT_CompanionPetPull
{
    private static Spell NewSpell(eSpellType type) =>
        new(new DbSpell { Type = type.ToString(), Target = "Realm", Duration = 1200 }, 50);

    private sealed class Owner : GamePlayer
    {
        private Owner() : base(null, null) { }
        // Built without constructors or field initializers: defaults mean alive and healthy.
        public bool Dead;
        public bool Fighting;
        public IControlledBrain Pet;
        public override bool IsAlive => !Dead;
        public override bool InCombat => Fighting;
        public override IControlledBrain ControlledBrain { get => Pet; set => Pet = value; }
        public override IPacketLib Out => null;
    }

    private sealed class Npc : GameNPC
    {
        public bool Dead;
        public bool Fighting;
        public bool Swinging;
        public byte Wounds;
        public int PositionX;
        public override bool IsAlive => !Dead;
        public override bool InCombat => Fighting;
        public override bool IsAttacking => Swinging;
        public override byte HealthPercent => (byte)(100 - Wounds);
        public override int X => PositionX;
        public override int Y => 0;
        public override int Z => 0;
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        private PetBrain() : base(null) { }
        public override eAggressionState AggressionState { get; set; }
    }

    private sealed class Companion : GameBot
    {
        private Companion() : base((OfflineWorldBotRecord)null) { }
    }

    private long _previousTick;

    [SetUp]
    public void SetUp() => _previousTick = GameLoop.GameLoopTime;

    [TearDown]
    public void TearDown() => SetTick(_previousTick);

    private static void SetTick(long value) =>
        typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);

    private static T Make<T>() where T : GameObject
    {
        var living = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        living.ObjectState = GameObject.eObjectState.Active;
        // Field initializers are skipped: give NPCs their (empty) brain stack.
        if (living is GameNPC)
            typeof(GameNPC).GetField("m_brains", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(living, new System.Collections.ArrayList());
        return living;
    }

    private static (Owner owner, Npc pet, PetBrain brain) OwnerWithPet()
    {
        Owner owner = Make<Owner>();
        Npc pet = Make<Npc>();
        pet.PositionX = 50;
        var brain = (PetBrain)RuntimeHelpers.GetUninitializedObject(typeof(PetBrain));
        brain.Body = pet;
        brain.AggressionState = eAggressionState.Defensive;
        owner.Pet = brain;
        return (owner, pet, brain);
    }

    /// <summary>A pull far from camp: beyond the engage radius so a release does not touch the world.</summary>
    private static Npc FarMonster()
    {
        Npc monster = Make<Npc>();
        monster.PositionX = BotBrain.GROUP_DEFENSE_ASSIST_RADIUS + 500;
        return monster;
    }

    [Test]
    public void StayGroveFacesTheLastPullElseThePlayersFacing()
    {
        var camp = new System.Numerics.Vector3(1000, 1000, 50);
        var facing = new System.Numerics.Vector3(0, 1, 0);

        var towardPull = CompanionPetPull.StayDirection(camp, new System.Numerics.Vector3(1000 + 900, 1000, 400), facing);
        Assert.That(towardPull.X, Is.EqualTo(1f).Within(0.001f));
        Assert.That(towardPull.Z, Is.EqualTo(0f), "The grove stays level with the camp");

        Assert.That(CompanionPetPull.StayDirection(camp, null, facing), Is.EqualTo(facing));
        Assert.That(CompanionPetPull.StayDirection(camp, camp, facing), Is.EqualTo(facing),
            "A pull from the camp itself gives no direction");
    }

    [Test]
    public void HealersTopThePetUpOnlyOnceTheFightIsOver()
    {
        Assert.That(CompanionPetPull.NeedsTopUp(false, false, false, 900, 1000), Is.True);
        Assert.That(CompanionPetPull.NeedsTopUp(false, false, false, 1000, 1000), Is.False, "Already full");
        Assert.That(CompanionPetPull.NeedsTopUp(true, false, false, 900, 1000), Is.False, "A pull is still running");
        Assert.That(CompanionPetPull.NeedsTopUp(false, true, false, 900, 1000), Is.False, "The owner still fights");
        Assert.That(CompanionPetPull.NeedsTopUp(false, false, true, 900, 1000), Is.False, "The pet still fights");
    }

    [Test]
    public void StayNeedsPetPullModeAndEndsWithIt()
    {
        Owner player = Make<Owner>();
        Assert.That(CompanionPetPull.SetStay(player, true), Does.Contain("/petpull on"));
        Assert.That(CompanionPetPull.IsStaying(player), Is.False);
        Assert.That(CompanionPetPull.SetStay(player, false), Does.Contain("already off"));
        Assert.That(CompanionPetPull.EndStay(player), Is.False);
    }

    [Test]
    public void OnlyBuffsThatWorkOnPetsGetPetPriority()
    {
        foreach (eSpellType type in new[] { eSpellType.StrengthConstitutionBuff, eSpellType.DexterityQuicknessBuff,
                     eSpellType.StrengthBuff, eSpellType.DamageAdd, eSpellType.DamageShield, eSpellType.HealOverTime,
                     eSpellType.BaseArmorFactorBuff, eSpellType.SpecArmorFactorBuff, eSpellType.DefensiveProc })
            Assert.That(CompanionPetPull.HelpsPet(NewSpell(type)), Is.True, type.ToString());

        // Haste, acuity and the generic armor buff stay with the group.
        foreach (eSpellType type in new[] { eSpellType.ArmorFactorBuff, eSpellType.CombatSpeedBuff, eSpellType.AcuityBuff })
            Assert.That(CompanionPetPull.HelpsPet(NewSpell(type)), Is.False, type.ToString());
    }

    [Test]
    public void NoArgumentTogglesAndOnOffSetExplicitly()
    {
        Assert.That(CompanionPetPull.ParseMode(new[] { "&petpull" }, false), Is.True);
        Assert.That(CompanionPetPull.ParseMode(new[] { "&petpull" }, true), Is.False);
        Assert.That(CompanionPetPull.ParseMode(new[] { "&petpull", "on" }, true), Is.True);
        Assert.That(CompanionPetPull.ParseMode(new[] { "&petpull", "OFF" }, false), Is.False);
        Assert.That(CompanionPetPull.ParseMode(new[] { "&petpull", "maybe" }, false), Is.Null);
    }

    [Test]
    public void ModeReplyTellsTheStateAndThatPullsStartWithThePetsAttack()
    {
        string on = CompanionPetPull.ModeReply(true, true);
        Assert.That(on, Does.Contain("ON"));
        Assert.That(on, Does.Contain("starts with your pet's attack"));
        Assert.That(on, Does.Not.Contain("Summon"));
        Assert.That(CompanionPetPull.ModeReply(true, false), Does.Contain("Summon your pet"));
        Assert.That(CompanionPetPull.ModeReply(false, true), Does.Contain("OFF"));
    }

    [Test]
    public void SetModeSwitchesTheModeWithoutStartingAPull()
    {
        (Owner owner, Npc pet, _) = OwnerWithPet();
        pet.TargetObject = FarMonster();

        Assert.That(CompanionPetPull.SetMode(owner, true), Does.Contain("ON"));
        Assert.That(CompanionPetPull.IsModeOn(owner), Is.True);
        // A selected target is no longer a pull: only the pet's own engage starts one.
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);

        Assert.That(CompanionPetPull.SetMode(owner, false), Does.Contain("OFF"));
        Assert.That(CompanionPetPull.IsModeOn(owner), Is.False);
    }

    [Test]
    public void PullStartsWhenThePetEngagesOnlyWhileTheModeIsOn()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, PetBrain brain) = OwnerWithPet();
        Npc monster = FarMonster();
        brain.OrderedAttackTarget = monster;

        Assert.That(CompanionPetPull.IsHolding(owner), Is.False, "Mode off: the pet attack is an ordinary fight");

        CompanionPetPull.SetMode(owner, true);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True, "The ordered pet attack starts the pet pull");
        Assert.That(CompanionPetPull.Pet(owner), Is.SameAs(pet));
        Assert.That(CompanionPetPull.SessionPet(owner), Is.SameAs(pet), "The pet gets its buffs first while the mode is on");

        CompanionPetPull.SetMode(owner, false);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        Assert.That(CompanionPetPull.SessionPet(owner), Is.Null);
    }

    private static GameLiving RememberedPull(GamePlayer player)
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        object modes = typeof(CompanionEngagementMode).GetField("Modes", hidden).GetValue(null);
        object[] args = [player, null];
        bool found = (bool)modes.GetType().GetMethod("TryGetValue").Invoke(modes, args);
        return found ? (GameLiving)args[1].GetType().GetField("Pull").GetValue(args[1]) : null;
    }

    [Test]
    public void PetAttackOrderDoesNotSendTheCompanionsInWhileThePullIsHeld()
    {
        SetTick(1_000_000);
        (Owner owner, _, PetBrain brain) = OwnerWithPet();
        Npc monster = FarMonster();
        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = monster;

        // ControlledMobBrain.Attack and the pet's swings report here: held, no engage order.
        PlayerLedPullCoordinator.LeaderEngaged(owner, monster);
        Assert.That(RememberedPull(owner), Is.Null);

        CompanionPetPull.SetMode(owner, false);
        PlayerLedPullCoordinator.LeaderEngaged(owner, monster);
        Assert.That(RememberedPull(owner), Is.SameAs(monster), "Mode off: the pet's attack is the group's pull at once");
    }

    [Test]
    public void AFightThePlayerOpensHimselfIsNoPetPull()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, _) = OwnerWithPet();
        CompanionPetPull.SetMode(owner, true);

        CompanionPetPull.OnLeaderAttack(owner);
        pet.Swinging = pet.Fighting = true;
        pet.TargetObject = FarMonster();
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False, "The pet assisting its owner does not hold the group");

        // Once that fight has been quiet, the pet's next engage is a pet pull again.
        pet.Swinging = pet.Fighting = false;
        SetTick(1_000_000 + CompanionPetPull.QuietEndMilliseconds);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        pet.Fighting = true;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void AnOrderOntoAMonsterAlreadyInCombatIsNoPull()
    {
        SetTick(1_000_000);
        (Owner owner, _, PetBrain brain) = OwnerWithPet();
        Npc fighting = FarMonster();
        fighting.Fighting = true;
        CompanionPetPull.SetMode(owner, true);

        // e.g. /pull with the mode on: the tank made contact and sent the pet in.
        brain.OrderedAttackTarget = fighting;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        Assert.That(CompanionPetPull.StartsPull(true, fighting, null, true, false), Is.False);
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void PetsSentAtEnemyPlayersNeverPetPull()
    {
        Npc fresh = FarMonster();
        Assert.That(CompanionPetPull.StartsPull(true, fresh, null, true, enemyPlayer: false), Is.True);
        Assert.That(CompanionPetPull.StartsPull(true, fresh, null, true, enemyPlayer: true), Is.False);
        Assert.That(CompanionPetPull.StartsPull(true, null, null, true, enemyPlayer: true), Is.False,
            "Nor does an enemy player hitting the pet");
        Assert.That(CompanionPetPull.StartsPull(false, fresh, FarMonster(), true, enemyPlayer: true), Is.False);
    }

    [Test]
    public void PetTakingDamageStartsAPullToo()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, _) = OwnerWithPet();
        CompanionPetPull.SetMode(owner, true);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);

        pet.PositionX = 1500;
        pet.Fighting = true;
        pet.TargetObject = FarMonster();
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
    }

    [Test]
    public void CompanionsHoldUntilThePassivePetIsBackAndTheModeStaysOn()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, PetBrain brain) = OwnerWithPet();
        Npc monster = FarMonster();
        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = monster;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);

        // The pet runs out and makes contact.
        SetTick(1_005_000);
        pet.PositionX = 1500;
        pet.Fighting = monster.Fighting = true;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        Assert.That(CompanionPetPull.TryGetCampFront(owner, out _), Is.True, "Animist mushrooms go to the camp front");

        // Passive but still out there: hold.
        brain.AggressionState = eAggressionState.Passive;
        brain.OrderedAttackTarget = null;
        SetTick(1_010_000);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);

        // Back beside the player: the pull is at camp.
        pet.PositionX = 200;
        SetTick(1_012_000);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        Assert.That(CompanionPetPull.IsReleased(owner), Is.True);
        Assert.That(CompanionPetPull.PetHealTarget(owner), Is.SameAs(pet), "After the release everyone heals the pet");
        Assert.That(CompanionPetPull.IsModeOn(owner), Is.True, "A release ends the pull, not the mode");

        // The fight ends; the next pet attack starts the next pull.
        pet.Fighting = monster.Fighting = false;
        monster.Dead = true;
        SetTick(1_030_000);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        Assert.That(CompanionPetPull.Pet(owner), Is.Null);

        Npc next = FarMonster();
        brain.AggressionState = eAggressionState.Defensive;
        brain.OrderedAttackTarget = next;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void AChainPullOntoAFreshMonsterStartsTheNextPullBeforeTheQuietEnd()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, PetBrain brain) = OwnerWithPet();
        Npc monster = FarMonster();
        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = monster;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        pet.PositionX = 1500;
        pet.Fighting = monster.Fighting = true;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        CompanionPetPull.OnLeaderAttack(owner);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False, "The player's attack releases the pull");

        // Ordering the pet onto the monster the group already fights is no new pull.
        brain.OrderedAttackTarget = monster;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);

        Npc fresh = FarMonster();
        brain.OrderedAttackTarget = fresh;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void PetInDangerKeepsDirectHealsOffUntilTheEmergencyRelease()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, PetBrain brain) = OwnerWithPet();
        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = FarMonster();
        pet.PositionX = 1500;
        pet.Fighting = true;
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True);
        Assert.That(CompanionPetPull.PetInDanger(owner), Is.False);
        Assert.That(CompanionPetPull.PetHealTarget(owner), Is.Null, "The held pet gets only the HoT");
        Assert.That(CompanionPetPull.IsHeldPullPet(owner, pet), Is.True);

        pet.Wounds = 40;
        SetTick(1_001_000);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.True, "Hurt, not yet critical: still held");
        Assert.That(CompanionPetPull.PetInDanger(owner), Is.True);
        Assert.That(CompanionPetPull.PetHealTarget(owner), Is.Null,
            "A direct heal would move the pet's attackers to the healer");
        Assert.That(CompanionPetPull.IsHeldPullPet(owner, pet), Is.True);

        pet.Wounds = 100 - CompanionPetPull.PetDangerHealthPercent + 1;
        SetTick(1_002_000);
        Assert.That(CompanionPetPull.IsHolding(owner), Is.False);
        Assert.That(CompanionPetPull.IsHeldPullPet(owner, pet), Is.False);
        Assert.That(CompanionPetPull.PetHealTarget(owner), Is.SameAs(pet),
            "The emergency release lets companions heal the pet");
        Assert.That(CompanionPetPull.IsModeOn(owner), Is.True);
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void SeveralAttackersCountAsDangerOnlyOnceThePetIsHurt()
    {
        Assert.That(CompanionPetPull.IsDanger(100, CompanionPetPull.PetWarningAttackers), Is.False,
            "A fresh pack pull is the pet's job: the tanks stay at camp");
        Assert.That(CompanionPetPull.IsDanger(CompanionPetPull.PetSwarmedHealthPercent - 1,
            CompanionPetPull.PetWarningAttackers), Is.True);
        Assert.That(CompanionPetPull.IsDanger(CompanionPetPull.PetSwarmedHealthPercent - 1,
            CompanionPetPull.PetWarningAttackers - 1), Is.False);
        Assert.That(CompanionPetPull.IsDanger(CompanionPetPull.PetWarningHealthPercent - 1, 1), Is.True);
    }

    [Test]
    public void CompanionsInterceptOnlyAddsOnOrRunningAtTheGroup()
    {
        Npc pet = Make<Npc>();
        Owner member = Make<Owner>();
        Npc bystander = Make<Npc>();
        bool OnGroupSide(GameLiving living) => living == member || living == pet;

        Npc addOnMember = Make<Npc>();
        addOnMember.TargetObject = member;
        addOnMember.Swinging = true;
        Assert.That(CompanionPetPull.IsAddOnGroup(addOnMember, pet, OnGroupSide), Is.True, "Running at or hitting a member");

        Npc pull = Make<Npc>();
        pull.TargetObject = pet;
        pull.Fighting = true;
        Assert.That(CompanionPetPull.IsAddOnGroup(pull, pet, OnGroupSide), Is.False, "The pull stays on the pet");

        Npc idle = Make<Npc>();
        idle.TargetObject = member;
        Assert.That(CompanionPetPull.IsAddOnGroup(idle, pet, OnGroupSide), Is.False, "A monster that is not coming");

        Npc elsewhere = Make<Npc>();
        elsewhere.TargetObject = bystander;
        elsewhere.Fighting = true;
        Assert.That(CompanionPetPull.IsAddOnGroup(elsewhere, pet, OnGroupSide), Is.False, "Nothing else is engaged");
    }

    [Test]
    public void OnlyTheOwnersHeldPullPetQualifiesForBafSuppression()
    {
        SetTick(1_000_000);
        (Owner owner, Npc pet, PetBrain brain) = OwnerWithPet();
        Npc unrelated = Make<Npc>();
        Npc target = FarMonster();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ControlledMobBrain).GetField("m_owner", hidden).SetValue(brain, owner);
        typeof(GameNPC).GetField("m_ownBrain", hidden).SetValue(pet, brain);

        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = target;
        Assert.That(CompanionPetPull.HasHeldPullPetAttacker(new GameLiving[] { unrelated, pet }), Is.True);
        Assert.That(CompanionPetPull.HasHeldPullPetAttacker(new GameLiving[] { unrelated }), Is.False);

        CompanionPetPull.OnLeaderAttack(owner);
        Assert.That(CompanionPetPull.HasHeldPullPetAttacker(new GameLiving[] { pet }), Is.False,
            "A released pull uses normal BAF");
        CompanionPetPull.SetMode(owner, false);
    }

    [Test]
    public void SquadCompanionsFollowTheirOwnersMode()
    {
        SetTick(1_000_000);
        (Owner owner, _, PetBrain brain) = OwnerWithPet();
        (Owner stranger, _, _) = OwnerWithPet();
        Companion squadMember = (Companion)RuntimeHelpers.GetUninitializedObject(typeof(Companion));
        Companion strangersCompanion = (Companion)RuntimeHelpers.GetUninitializedObject(typeof(Companion));
        typeof(GameBot).GetProperty(nameof(GameBot.PlayerGroupLeader)).SetValue(squadMember, owner);
        typeof(GameBot).GetProperty(nameof(GameBot.PlayerGroupLeader)).SetValue(strangersCompanion, stranger);

        CompanionPetPull.SetMode(owner, true);
        brain.OrderedAttackTarget = FarMonster();
        Assert.That(CompanionPetPull.HoldsFor(squadMember), Is.True);
        Assert.That(CompanionPetPull.HoldsFor(strangersCompanion), Is.False);

        CompanionPetPull.SetMode(owner, false);
        Assert.That(CompanionPetPull.HoldsFor(squadMember), Is.False);
    }

    [Test]
    public void ModeIsBoundToTheLoggedInCharacterObject()
    {
        (Owner session, _, _) = OwnerWithPet();
        (Owner nextLogin, _, _) = OwnerWithPet();
        CompanionPetPull.SetMode(session, true);
        Assert.That(CompanionPetPull.IsModeOn(nextLogin), Is.False, "A new login starts with the mode off");
        CompanionPetPull.SetMode(session, false);
    }
}
