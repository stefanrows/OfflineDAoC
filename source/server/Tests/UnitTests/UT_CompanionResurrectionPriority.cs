using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Bug 55: a companion that buffs everyone first and resurrects a dead player
    // afterwards. Fix: pick the strongest resurrection rank the current power
    // allows (weaker rank when low, strongest when affordable), and hold off
    // buffs/maintenance while a known rez is unaffordable and someone is dead.
    [TestFixture, NonParallelizable]
    public class UT_CompanionResurrectionPriority
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private readonly List<GameLiving> _actors = new();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive = true;
            public bool Casting;
            public override bool IsAlive => Alive;
            public override bool IsAttacking => false;
            public override bool IsCasting => Casting;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override IControlledBrain ControlledBrain { get; set; }
            public override int Mana { get; set; }
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

        private static void Field(System.Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);

        private Bot MakeResurrectorWithRanks(params (int power, byte level, int resurrectHealth)[] ranks)
        {
            Bot bot = Actor<Bot>();
            bot.Alive = true;
            bot.Name = "Cleric";
            Field(typeof(GameBot), bot, "m_characterClass", new ClassCleric());
            var spells = new List<Spell>();
            foreach (var (power, level, resurrectHealth) in ranks)
            {
                spells.Add(new Spell(new DbSpell
                {
                    Type = eSpellType.Resurrect.ToString(),
                    Target = eSpellTarget.REALM.ToString(),
                    Power = power,
                    ResurrectHealth = resurrectHealth,
                }, level));
            }
            bot.Spells = spells;
            return bot;
        }

        [Test]
        public void BestAffordableResurrection_PicksTheStrongestRankThePowerAllows()
        {
            Bot bot = MakeResurrectorWithRanks((20, 10, 50), (60, 30, 100));
            bot.Mana = 100;
            Assert.That(bot.BestAffordableResurrection().Level, Is.EqualTo(30), "Plenty of power: use the strong rez.");
        }

        [Test]
        public void BestAffordableResurrection_FallsBackToTheSmallestRankWhenLow()
        {
            Bot bot = MakeResurrectorWithRanks((20, 10, 50), (60, 30, 100));
            bot.Mana = 25;
            Assert.That(bot.BestAffordableResurrection().Level, Is.EqualTo(10), "Only the small rez fits the current power.");
        }

        [Test]
        public void BestAffordableResurrection_NullWhenNothingIsAffordable()
        {
            Bot bot = MakeResurrectorWithRanks((20, 10, 50), (60, 30, 100));
            bot.Mana = 5;
            Assert.That(bot.BestAffordableResurrection(), Is.Null);
        }

        [Test]
        public void BestAffordableResurrection_NullForABotWithNoKnownResurrection()
        {
            Bot bot = MakeResurrectorWithRanks();
            bot.Mana = 1000;
            Assert.That(bot.BestAffordableResurrection(), Is.Null);
        }

        [Test]
        public void ResurrectionSpell_StillTracksTheStrongestKnownRankRegardlessOfPower()
        {
            // ResurrectionSpell (task-42-era, used by TemporaryCompanionRecovery and
            // HasViableResurrector) must keep meaning "best known", independent of
            // BestAffordableResurrection's power-aware pick (bug 55).
            Bot bot = MakeResurrectorWithRanks((20, 10, 50), (60, 30, 100));
            bot.Mana = 0;
            Assert.That(bot.ResurrectionSpell.Level, Is.EqualTo(30));
            Assert.That(bot.BestAffordableResurrection(), Is.Null);
        }
    }
}
