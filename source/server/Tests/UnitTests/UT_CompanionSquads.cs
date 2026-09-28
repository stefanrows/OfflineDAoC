using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Task 44: squad members fight for their owner even though their own Group is
    // the squad's, never the owner's. These tests exercise CompanionSquads directly
    // and the two central gates it replaces (PlayerLedPullCoordinator.Available and
    // CompanionPvpEngagement.Leader), without touching the database-backed roster.
    [TestFixture, NonParallelizable]
    public class UT_CompanionSquads
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private readonly List<GameLiving> _actors = new();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => true;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override GameObject TargetObject { get; set; }
            public override IControlledBrain ControlledBrain { get; set; }
            public override IPacketLib Out => new BotDummyPacketLib();
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive = true;
            public int TestX;
            public override bool IsAlive => Alive;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set => _ = value; }
            public override IControlledBrain ControlledBrain { get; set; }
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameLiving actor in _actors)
                ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new System.Collections.ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }

        private Bot MakeCompanion(Player owner, int squadIndex, bool isLeader = false)
        {
            Bot bot = Actor<Bot>();
            bot.Alive = true;
            bot.Name = "Companion";
            Field(typeof(GameBot), bot, "<Owner>k__BackingField", owner);
            var record = new PlayerCompanionRecord { SquadIndex = squadIndex, IsSquadLeader = isLeader };
            Field(typeof(GameBot), bot, "<PlayerCompanionRecord>k__BackingField", record);
            bot.EnterPlayerLedGroup(owner);
            return bot;
        }

        // Populates PlayerCompanionRoster's private ActiveSquads map directly, the
        // same live-Group registry TrySquadAdd/RestoreSquadCompanion normally build,
        // without going through the database-backed roster commands.
        private static void PutInSquad(Player owner, int squadIndex, Bot bot)
        {
            Group squad = new(bot);
            Add(squad, bot);
            FieldInfo activeSquadsField = typeof(PlayerCompanionRoster).GetField("ActiveSquads", BindingFlags.NonPublic | BindingFlags.Static);
            object activeSquads = activeSquadsField.GetValue(null);
            Type dictType = activeSquads.GetType();
            object[] tryGetArgs = { owner, null };
            bool present = (bool)dictType.GetMethod("TryGetValue").Invoke(activeSquads, tryGetArgs);
            Group[] slots;
            if (present)
                slots = (Group[])tryGetArgs[1];
            else
            {
                slots = new Group[CompanionSquadFormation.MaxSquadCount + 1];
                dictType.GetMethod("TryAdd").Invoke(activeSquads, new object[] { owner, slots });
            }
            slots[squadIndex] = squad;
        }

        private static List<GameLiving> Members(Group group) =>
            (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);

        private static void Add(Group group, GameLiving member)
        {
            Members(group).Add(member);
            member.Group = group;
        }

        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);

        [Test]
        public void SharesOwnerForce_OrdinaryOwnGroupCompanion_IsTrue()
        {
            Player owner = Actor<Player>();
            var group = new Group(owner);
            Add(group, owner);
            Bot companion = MakeCompanion(owner, squadIndex: 0);
            Add(group, companion);
            Assert.That(CompanionSquads.SharesOwnerForce(companion, owner), Is.True);
        }

        [Test]
        public void SharesOwnerForce_SquadMember_IsTrueEvenThoughGroupsDiffer()
        {
            Player owner = Actor<Player>();
            var ownGroup = new Group(owner);
            Add(ownGroup, owner);
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(squadMember.Group, Is.Not.SameAs(owner.Group));
            Assert.That(CompanionSquads.SharesOwnerForce(squadMember, owner), Is.True);
        }

        [Test]
        public void SharesOwnerForce_SquadMember_IsTrueWithoutAnOwnerGroupAtAll()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(owner.Group, Is.Null);
            Assert.That(CompanionSquads.SharesOwnerForce(squadMember, owner), Is.True);
        }

        [Test]
        public void SharesOwnerForce_UnrelatedCompanionOfAnotherOwner_IsFalse()
        {
            Player owner = Actor<Player>();
            Player stranger = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(CompanionSquads.SharesOwnerForce(squadMember, stranger), Is.False);
        }

        [Test]
        public void SharesOwnerForce_BenchedSquadRecordWithNoLiveGroup_IsFalse()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            // No PutInSquad: the record claims a squad, but the bot never actually
            // joined a live Group (e.g. still mid-restore). Must not be treated as
            // fielded.
            Assert.That(CompanionSquads.SharesOwnerForce(squadMember, owner), Is.False);
        }

        [Test]
        public void IsOwnerForceMember_OwnerHimselfAndHisSquadMembers_AreMembers()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 2);
            PutInSquad(owner, 2, squadMember);
            Assert.That(CompanionSquads.IsOwnerForceMember(owner, owner), Is.True);
            Assert.That(CompanionSquads.IsOwnerForceMember(squadMember, owner), Is.True);
            Assert.That(CompanionSquads.IsOwnerForceMember(Actor<Player>(), owner), Is.False);
        }

        [Test]
        public void ShareOwnerForce_TwoDifferentSquadsOfTheSameOwner_ShareIt()
        {
            Player owner = Actor<Player>();
            Bot squad1 = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squad1);
            Bot squad2 = MakeCompanion(owner, squadIndex: 2);
            PutInSquad(owner, 2, squad2);
            Assert.That(CompanionSquads.ShareOwnerForce(squad1, squad2), Is.True);
            Assert.That(CompanionSquads.ShareOwnerForce(squad1, owner), Is.True);
        }

        [Test]
        public void ShareOwnerForce_DifferentOwnersSquads_DoNotShareIt()
        {
            Player ownerA = Actor<Player>();
            Player ownerB = Actor<Player>();
            Bot squadA = MakeCompanion(ownerA, squadIndex: 1);
            PutInSquad(ownerA, 1, squadA);
            Bot squadB = MakeCompanion(ownerB, squadIndex: 1);
            PutInSquad(ownerB, 1, squadB);
            Assert.That(CompanionSquads.ShareOwnerForce(squadA, squadB), Is.False);
        }

        [Test]
        public void OwnerForceBots_EnumeratesOwnGroupAndEverySquad()
        {
            Player owner = Actor<Player>();
            var ownGroup = new Group(owner);
            Add(ownGroup, owner);
            Bot ownGroupCompanion = MakeCompanion(owner, squadIndex: 0);
            Add(ownGroup, ownGroupCompanion);
            Bot squad1Leader = MakeCompanion(owner, squadIndex: 1, isLeader: true);
            PutInSquad(owner, 1, squad1Leader);
            Bot squad3Member = MakeCompanion(owner, squadIndex: 3);
            PutInSquad(owner, 3, squad3Member);

            GameBot[] force = CompanionSquads.OwnerForceBots(owner).ToArray();
            Assert.That(force, Has.Member(ownGroupCompanion));
            Assert.That(force, Has.Member(squad1Leader));
            Assert.That(force, Has.Member(squad3Member));
            Assert.That(force.Length, Is.EqualTo(3));
        }

        [Test]
        public void OwnerForceBots_OwnerWithOnlySquads_StillEnumeratesThem()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(owner.Group, Is.Null);
            Assert.That(CompanionSquads.OwnerForceBots(owner).ToArray(), Is.EqualTo(new[] { squadMember }));
        }

        [Test]
        public void PlayerLedPullCoordinator_Available_TreatsASquadMemberLikeAnOrdinaryCompanion()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(PlayerLedPullCoordinator.Available(squadMember, owner), Is.True);
        }

        [Test]
        public void PlayerLedPullCoordinator_Available_FalseForAnotherOwnersSquadMember()
        {
            Player owner = Actor<Player>();
            Player stranger = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(PlayerLedPullCoordinator.Available(squadMember, stranger), Is.False);
        }

        [Test]
        public void CompanionPvpEngagement_Leader_ResolvesToOwnerForASquadMember()
        {
            Player owner = Actor<Player>();
            Bot squadMember = MakeCompanion(owner, squadIndex: 1);
            PutInSquad(owner, 1, squadMember);
            Assert.That(CompanionPvpEngagement.Leader(squadMember), Is.SameAs(owner));
        }
    }
}
