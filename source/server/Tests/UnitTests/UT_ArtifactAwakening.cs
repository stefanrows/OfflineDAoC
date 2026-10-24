using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.Quests.Atlantis;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Earning an artifact (TOA_PLAN WP3.1b): scholars refuse without encounter credit, hand the
/// version that fits class and realm (asking when more than one fits), once per character;
/// scroll pages combine into the book in the backpack; Aphaestia's credit tokens grant the
/// encounter credit; every text names the artifact instead of the "1001" placeholder.
/// Rows have the shape of ours after packs 051 and 061.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_ArtifactAwakening
{
    private const string Leggings = "1001";
    private const string Bruiser = "1011";
    private const string LeggingsName = "Alvarus's Leggings";
    private const string Page1 = "Alvarus' Letter, part 1 of 3";
    private const string Page2 = "Alvarus' Letter, part 2 of 3";
    private const string Page3 = "Alvarus' Letter, part 3 of 3";
    private const string Book = "Alvarus' Letter, complete";
    private const string PlayerId = "awakening-test";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);

    public class Database : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type type = method.ReturnType;

            if (method.Name == "FindObjectByKey" && type == typeof(DbItemTemplate))
                return args[0] is string key && Templates.TryGetValue(key, out DbItemTemplate template) ? template : null;

            if (type == typeof(bool))
                return true;

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

            return type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    public class Packets : DispatchProxy
    {
        public readonly List<string> Messages = [];

        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == nameof(IPacketLib.SendMessage) && args?.Length > 0 && args[0] is string text)
                Messages.Add(text);

            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    /// <summary>A backpack: slots 40-79, AddItem/RemoveItem/GetItemRange as the player inventory does.</summary>
    public class Backpack : DispatchProxy
    {
        public readonly SortedDictionary<int, DbInventoryItem> Items = new();
        private readonly Lock _lock = new();

        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_Lock":
                    return _lock;
                case "AddItem" when args.Length == 2:
                {
                    var slot = (eInventorySlot) args[0];
                    var item = (DbInventoryItem) args[1];

                    if (slot == eInventorySlot.FirstEmptyBackpack)
                        slot = FirstEmpty();

                    if (slot == eInventorySlot.Invalid || Items.ContainsKey((int) slot))
                        return false;

                    item.SlotPosition = (int) slot;
                    item.OwnerID = PlayerId;
                    Items[(int) slot] = item;
                    return true;
                }
                case "RemoveItem" when args.Length == 1:
                {
                    int key = Items.FirstOrDefault(kv => kv.Value == args[0]).Key;

                    if (key == 0)
                        return false;

                    Items.Remove(key);
                    return true;
                }
                case "GetItemRange":
                    return Items.Where(kv => kv.Key >= (int) (eInventorySlot) args[0] && kv.Key <= (int) (eInventorySlot) args[1])
                        .Select(kv => kv.Value).ToList();
                case "get_AllItems":
                    return Items.Values.ToList();
                case "FindFirstEmptySlot":
                    return FirstEmpty();
                case "GetItem":
                    return Items.TryGetValue((int) (eInventorySlot) args[0], out DbInventoryItem found) ? found : null;
            }

            Type type = method.ReturnType;

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

            return type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private eInventorySlot FirstEmpty()
        {
            for (int slot = (int) eInventorySlot.FirstBackpack; slot <= (int) eInventorySlot.LastBackpack; slot++)
            {
                if (!Items.ContainsKey(slot))
                    return (eInventorySlot) slot;
            }

            return eInventorySlot.Invalid;
        }
    }

    public class Armsman : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_ID")
                return (int) eCharacterClass.Armsman;

            if (method.Name == "get_Name")
                return "Armsman";

            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private sealed class Player : GamePlayer
    {
        private Player() : base(null, null) { }
        public IPacketLib PacketLib;
        public ICharacterClass Class;
        public byte TestLevel;
        public override string Name { get => "Tester"; set { } }
        public override byte Level { get => TestLevel; set { } }
        public override IPacketLib Out => PacketLib;
        public override ICharacterClass CharacterClass => Class;
        public override string QuestPlayerID => PlayerId;
        private ConcurrentDictionary<AbstractQuest, byte> _quests;
        public override ConcurrentDictionary<AbstractQuest, byte> QuestList => _quests ??= new();
        public List<string> Messages => ((Packets) (object) PacketLib).Messages;
        public SortedDictionary<int, DbInventoryItem> Pack => ((Backpack) (object) Inventory).Items;
    }

    private sealed class Server : GameServer
    {
        public IObjectDatabase Db;
        protected override IObjectDatabase DataBaseImpl => Db;
    }

    private GameServer _previousServer;
    private Func<GamePlayer, DbItemTemplate, bool> _previousCanUse;
    private Func<GamePlayer, GameLiving, DbInventoryItem, bool> _previousDeliver;
    private Func<GameObject, string> _previousLogName;
    private HashSet<string> _usable;
    private List<DbInventoryItem> _delivered;

    [SetUp]
    public void Setup()
    {
        _previousServer = GameServer.Instance;
        var server = (Server) RuntimeHelpers.GetUninitializedObject(typeof(Server));
        server.Db = DispatchProxy.Create<IObjectDatabase, Database>();
        GameServer.LoadTestDouble(server);

        Templates.Clear();
        AddArmor("Alvarus_Leggings_Cloth", eObjectType.Cloth, 0);
        AddArmor("Alvarus_Leggings_Leather", eObjectType.Leather, 0);
        AddArmor("Alvarus_Leggings_Scale", eObjectType.Scale, (int) eRealm.Hibernia);
        AddArmor("Alvarus_Leggings_plate", eObjectType.Plate, (int) eRealm.Albion);
        AddPage("Alvarus_Letter,_part_1_of_3", Page1);
        AddPage("Alvarus_Letter,_part_2_of_3", Page2);
        AddPage("Alvarus_Letter,_part_3_of_3", Page3);
        AddCredit("alvarus_leggings_credit", "Alvarus's Leggings Credit");
        AddCredit("atlantis_tablet_credit", "Atlantis Tablet Credit");
        AddCredit("bruiser_credit", "Bruiser Credit");
        Templates["Bruiser_Crush"] = new DbItemTemplate
        {
            Id_nb = "Bruiser_Crush", Name = "Bruiser", Object_Type = (int) eObjectType.CrushingWeapon,
            Item_Type = (int) eInventorySlot.RightHandWeapon, Type_Damage = 1, AllowedClasses = "2", ClassType = "DOL.GS.InventoryArtifact",
        };

        // Our rows after packs 051 and 061: numeric placeholders in every message column.
        ArtifactMgr.Load(
            [
                new DbArtifact
                {
                    ArtifactID = Leggings, EncounterID = "Alvarus_Leggings", QuestID = "Alvarus_Leggings", ScholarID = Leggings, Credit = "Alvarus_Leggings",
                    XPRate = 5, Zone = "0", BookID = Book, BookModel = 500, ScrollModel1 = 499, ScrollModel2 = 499,
                    Scroll1 = Page1, Scroll2 = Page2, Scroll3 = Page3,
                    Scroll12 = "Alvarus' Letter, parts 1 and 2 of 3", Scroll13 = "Alvarus' Letter, parts 1 and 3 of 3", Scroll23 = "Alvarus' Letter, parts 2 and 3 of 3",
                    MessageUse = Leggings, MessageCombineScrolls = Leggings, MessageCombineBook = Leggings, MessageReceiveScrolls = Leggings, MessageReceiveBook = Leggings,
                },
                new DbArtifact
                {
                    ArtifactID = Bruiser, EncounterID = "Tale_of_Bruiser", QuestID = "Tale_of_Bruiser", ScholarID = Bruiser, Credit = "bruiser",
                    XPRate = 5, Zone = "0", BookID = string.Empty, Scroll1 = "Tale_of_Bruiser1", Scroll2 = "Tale_of_Bruiser2", Scroll3 = "Tale_of_Bruiser3",
                    MessageReceiveBook = Bruiser,
                },
            ],
            [
                new DbArtifactXItem { ArtifactID = Leggings, ItemID = "Alvarus_Leggings_Cloth", Version = "0", Realm = 0 },
                new DbArtifactXItem { ArtifactID = Leggings, ItemID = "Alvarus_Leggings_Leather", Version = "1", Realm = 0 },
                new DbArtifactXItem { ArtifactID = Leggings, ItemID = "Alvarus_Leggings_Scale", Version = "5", Realm = (int) eRealm.Hibernia },
                new DbArtifactXItem { ArtifactID = Leggings, ItemID = "Alvarus_Leggings_plate", Version = "6", Realm = (int) eRealm.Albion },
                new DbArtifactXItem { ArtifactID = Bruiser, ItemID = "Bruiser_Crush", Version = "2", Realm = 0 },
            ],
            []);

        // Test NPCs have no position; the log line only needs a name.
        _previousLogName = InventoryLogging.GetGameObjectString;
        InventoryLogging.GetGameObjectString = obj => obj == null ? "(null)" : "(" + obj.GetType().Name + ")";

        _previousCanUse = ArtifactAwakening.CanUseTemplate;
        _previousDeliver = ArtifactAwakening.Deliver;
        _usable = null; // null: every version is usable
        _delivered = [];
        ArtifactAwakening.CanUseTemplate = (_, template) => _usable == null || _usable.Contains(template.Id_nb);
        ArtifactAwakening.Deliver = (player, _, item) =>
        {
            if (!player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
                return false;

            _delivered.Add(item);
            return true;
        };
    }

    [TearDown]
    public void Teardown()
    {
        ArtifactAwakening.CanUseTemplate = _previousCanUse;
        ArtifactAwakening.Deliver = _previousDeliver;
        InventoryLogging.GetGameObjectString = _previousLogName;
        ArtifactMgr.Load([], [], []);
        Templates.Clear();
        GameServer.LoadTestDouble(_previousServer);
    }

    private static void AddArmor(string id, eObjectType type, int realm)
    {
        Templates[id] = new DbItemTemplate
        {
            Id_nb = id, Name = LeggingsName, Object_Type = (int) type, Item_Type = (int) eInventorySlot.LegsArmor,
            Realm = realm, AllowedClasses = "2;10", ClassType = "DOL.GS.InventoryArtifact",
        };
    }

    private static void AddPage(string id, string name)
    {
        Templates[id] = new DbItemTemplate
        {
            Id_nb = id, Name = name, Object_Type = (int) eObjectType.GenericItem, Item_Type = (int) eInventorySlot.FirstBackpack,
            Model = 603, Level = 35, Price = 300, MaxCount = 1, PackSize = 1, IsDropable = true, IsPickable = true, IsTradable = true,
        };
    }

    private static void AddCredit(string id, string name)
    {
        Templates[id] = new DbItemTemplate
        {
            Id_nb = id, Name = name, Object_Type = (int) eObjectType.GenericItem, Item_Type = (int) eInventorySlot.FirstBackpack,
            Price = 470, MaxCount = 1, PackSize = 1,
        };
    }

    private static Player NewPlayer(eRealm realm = eRealm.Albion, byte level = 50)
    {
        var player = (Player) RuntimeHelpers.GetUninitializedObject(typeof(Player));
        player.PacketLib = DispatchProxy.Create<IPacketLib, Packets>();
        player.Class = DispatchProxy.Create<ICharacterClass, Armsman>();
        player.TestLevel = level;
        player.Realm = realm;
        typeof(GamePlayer).GetField("_questListFinished", PrivateInstance).SetValue(player, new List<AbstractQuest>());
        typeof(GamePlayer).GetField("_questListFinishedLock", PrivateInstance).SetValue(player, new Lock());
        typeof(GamePlayer).GetField("<AvailableQuestIndexes>k__BackingField", PrivateInstance).SetValue(player, new ConcurrentQueue<byte>());
        player.Inventory = DispatchProxy.Create<IGameInventory, Backpack>();
        return player;
    }

    private static DbInventoryItem Give(Player player, string templateId)
    {
        DbInventoryItem item = GameInventoryItem.Create(Templates[templateId]);
        Assert.That(player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item), Is.True);
        return item;
    }

    private static T Npc<T>() where T : GameNPC => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static List<string> PackNames(Player player) => player.Pack.Values.Select(i => i.Name).ToList();

    [Test]
    public void ScholarRefusesWithoutCredit_KeepsThePages_AndNamesTheArtifact()
    {
        Player player = NewPlayer();
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");

        string reply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out bool handled);

        Assert.That(handled, Is.True, "a page of a known artifact is the scholar's business");
        Assert.That(reply, Does.Contain(LeggingsName).And.Contain("Aphaestia"), "readable hint: where the credit comes from");
        Assert.That(reply, Does.Not.Contain("1001"));
        Assert.That(PackNames(player), Is.EquivalentTo(new[] { Page1, Page2, Page3 }), "nothing is taken");
        Assert.That(_delivered, Is.Empty);
        Assert.That(ArtifactAwakening.Check(player, Leggings), Is.EqualTo(ArtifactRefusal.NoCredit));
    }

    [Test]
    public void MissingPages_AreNamed()
    {
        Player player = NewPlayer();
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");

        string reply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out bool handled);

        Assert.That(handled, Is.True);
        Assert.That(reply, Does.Contain(LeggingsName).And.Contain(Page2).And.Contain(Page3).And.Not.Contain(Page1));
        Assert.That(PackNames(player), Is.EqualTo(new[] { Page1 }));
    }

    [Test]
    public void OneFittingVersion_IsHandedOverAtOnce_AndUsesUpBookAndCredit()
    {
        Player player = NewPlayer(eRealm.Albion);
        _usable = ["Alvarus_Leggings_plate"]; // an armsman wears plate
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");

        string reply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out bool handled);

        Assert.That(handled, Is.True);
        Assert.That(reply, Does.Contain(LeggingsName));
        Assert.That(_delivered.Select(i => i.Id_nb), Is.EqualTo(new[] { "Alvarus_Leggings_plate" }));
        Assert.That(PackNames(player), Is.EqualTo(new[] { LeggingsName }), "the three pages are gone, the artifact is in the pack");
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.False, "the credit is used up");
        Assert.That(ArtifactMgr.HasFinishedArtifactQuest(player, Leggings), Is.True);
        Assert.That(player.GetFinishedQuests().OfType<ArtifactQuest>().Single().ItemID, Is.EqualTo("Alvarus_Leggings_plate"));
        Assert.That(player.Messages, Has.Some.Contains(LeggingsName), "the receive message names the artifact, not '1001'");
        Assert.That(player.Messages, Has.None.EqualTo(Leggings));
    }

    [Test]
    public void SeveralVersions_TheScholarKeepsTheBookAndAsks_ThenHandsTheChosenOne()
    {
        Player player = NewPlayer(eRealm.Albion);
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");
        Scholar scholar = Npc<Scholar>();

        List<ArtifactChoice> choices = ArtifactAwakening.GetChoices(player, Leggings);
        Assert.That(choices.Select(c => c.Label), Is.EqualTo(new[] { "Cloth", "Leather", "Plate" }),
            "realm 0 and Albion versions in Version order; the Hibernian scale is not offered");

        string ask = ArtifactAwakening.HandIn(player, scholar, first, out bool handled);

        Assert.That(handled, Is.True);
        Assert.That(ask, Does.Contain(LeggingsName).And.Contain("[Cloth]").And.Contain("[Leather]").And.Contain("[Plate]").And.Not.Contain("Scale"));
        Assert.That(player.Pack, Is.Empty, "the scholar keeps the book while the player chooses");
        ArtifactQuest pending = ArtifactQuest.Pending(player);
        Assert.That(pending?.ArtifactID, Is.EqualTo(Leggings));
        Assert.That(pending.Description, Does.Contain("[Plate]"));
        Assert.That(ArtifactAwakening.Greeting(player, scholar), Does.Contain("[Plate]"), "a click reminds the player of the open choice");
        Assert.That(ArtifactAwakening.Whisper(player, scholar, "Scale"), Is.Null, "a version that was not offered means nothing");

        string given = ArtifactAwakening.Whisper(player, scholar, "plate");

        Assert.That(given, Does.Contain(LeggingsName));
        Assert.That(_delivered.Select(i => i.Id_nb), Is.EqualTo(new[] { "Alvarus_Leggings_plate" }));
        Assert.That(ArtifactQuest.Pending(player), Is.Null);
        Assert.That(player.QuestList, Is.Empty, "out of the journal");
        Assert.That(ArtifactMgr.HasFinishedArtifactQuest(player, Leggings), Is.True);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.False);
    }

    [Test]
    public void HibernianGetsTheirOwnVersions()
    {
        Player player = NewPlayer(eRealm.Hibernia);

        Assert.That(ArtifactAwakening.GetChoices(player, Leggings).Select(c => c.Template.Id_nb),
            Is.EqualTo(new[] { "Alvarus_Leggings_Cloth", "Alvarus_Leggings_Leather", "Alvarus_Leggings_Scale" }));

        _usable = ["Nothing_Fits"];
        Assert.That(ArtifactAwakening.GetChoices(player, Leggings), Is.Empty,
            "a class that can use none of the versions gets none, not an artifact it can never wear");
        Assert.That(ArtifactAwakening.Check(player, Leggings), Is.EqualTo(ArtifactRefusal.NotForClass));
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit", forPurchase: true), Does.Contain(LeggingsName),
            "and Aphaestia does not sell its credit");
    }

    [Test]
    public void UnusableVersions_TheScholarRefusesAndKeepsNothing()
    {
        Player player = NewPlayer();
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        _usable = ["Nothing_Fits"];
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");

        string reply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out bool handled);

        Assert.That(handled, Is.True);
        Assert.That(reply, Does.Contain("would not answer").And.Contain(LeggingsName));
        Assert.That(PackNames(player), Is.EquivalentTo(new[] { Page1, Page2, Page3 }));
        Assert.That(_delivered, Is.Empty);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.True);
    }

    [Test]
    public void BookFromAPairDraggedOntoAPage_ComesBackWhenTheChoiceIsAbandoned()
    {
        Player player = NewPlayer();
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem one = Give(player, "Alvarus_Letter,_part_1_of_3");
        DbInventoryItem two = Give(player, "Alvarus_Letter,_part_2_of_3");
        DbInventoryItem three = Give(player, "Alvarus_Letter,_part_3_of_3");
        Assert.That(ArtifactMgr.TryCombineScrolls(player, one, two), Is.True);
        DbInventoryItem pair = player.Pack.Values.Single(i => i != three);

        // The pair (a unique) is the dragged item: the book must still come from a real page template.
        Assert.That(ArtifactMgr.TryCombineScrolls(player, pair, three), Is.True);
        DbInventoryItem book = player.Pack.Values.Single();
        Assert.That(book.Name, Is.EqualTo(Book));
        Assert.That(book.Id_nb, Does.StartWith("Alvarus_Letter,_part_"), "copied from a page template, not from the pair");

        string ask = ArtifactAwakening.HandIn(player, Npc<Scholar>(), book, out _);
        Assert.That(ask, Does.Contain("[Plate]"));
        ArtifactQuest pending = ArtifactQuest.Pending(player);
        Assert.That(pending.BookTemplateID, Does.StartWith("Alvarus_Letter,_part_"));
        Assert.That(player.Pack, Is.Empty);

        pending.AbortQuest();

        Assert.That(player.Pack.Values.Select(i => i.Name), Is.EqualTo(new[] { Book }), "the book is back");
        Assert.That(ArtifactMgr.HasBook(player, Leggings), Is.True);
        Assert.That(ArtifactQuest.Pending(player), Is.Null);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.True, "the credit was never used");
    }

    [Test]
    public void ABookWithoutAStoredTemplate_IsRebuiltFromAPageTemplate()
    {
        Player player = NewPlayer();
        DbItemTemplate page = Templates["Alvarus_Letter,_part_2_of_3"];
        DbInventoryItem pairFromPage = ArtifactMgr.CreatePages(Leggings, ArtifactMgr.Book.Page1 | ArtifactMgr.Book.Page2, page).Item;
        DbInventoryItem pairFromPair = ArtifactMgr.CreatePages(Leggings, ArtifactMgr.Book.Page2 | ArtifactMgr.Book.Page3, pairFromPage.Template).Item;

        Assert.That(ArtifactMgr.FindPageTemplate(Leggings, pairFromPair, pairFromPage), Is.SameAs(page),
            "the page template behind a combined unique ('page#id')");
        Assert.That(ArtifactMgr.FindPageTemplate(Leggings, pairFromPair), Is.Null.Or.Not.InstanceOf<DbItemUnique>());
    }

    [Test]
    public void OneArtifactPerCharacter()
    {
        Player player = NewPlayer();
        _usable = ["Alvarus_Leggings_plate"];
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");
        ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out _);
        Assert.That(_delivered, Has.Count.EqualTo(1));

        // Dropped or vaulted: the record stays, so no second copy and no second credit.
        player.Pack.Clear();
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.False);
        DbInventoryItem token = Give(player, "alvarus_leggings_credit");
        string tokenReply = ArtifactAwakening.RedeemCreditToken(player, Npc<ArtifactCreditMerchant>(), token);
        Assert.That(tokenReply, Does.Contain("already awakened").And.Contain(LeggingsName));
        Assert.That(player.Pack.Values, Does.Contain(token), "a useless token stays with the player");

        first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");
        string again = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out bool handled);

        Assert.That(handled, Is.True);
        Assert.That(again, Does.Contain("already awakened").And.Contain(LeggingsName));
        Assert.That(_delivered, Has.Count.EqualTo(1));
        Assert.That(PackNames(player).Count(n => n.StartsWith("Alvarus' Letter")), Is.EqualTo(3), "the second book is not taken");
    }

    [Test]
    public void CarryingTheArtifactAlready_IsRefused()
    {
        Player player = NewPlayer();
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        Give(player, "Alvarus_Leggings_Cloth"); // from a GM or an older save

        Assert.That(ArtifactAwakening.Check(player, Leggings), Is.EqualTo(ArtifactRefusal.AlreadyCarried));
        Assert.That(ArtifactAwakening.Explain(ArtifactRefusal.AlreadyCarried, player, Leggings), Does.Contain(LeggingsName));
    }

    [Test]
    public void TooLowLevel_IsRefused()
    {
        Player player = NewPlayer(level: 44);
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);

        Assert.That(ArtifactAwakening.Check(player, Leggings), Is.EqualTo(ArtifactRefusal.TooLow));
        Assert.That(ArtifactAwakening.Explain(ArtifactRefusal.TooLow, player, Leggings), Does.Contain(LeggingsName).And.Contain("45"));
    }

    [Test]
    public void ScrollsCombineInTheBackpack_IntoThePair_ThenTheBook()
    {
        Player player = NewPlayer();
        DbInventoryItem one = Give(player, "Alvarus_Letter,_part_1_of_3");
        DbInventoryItem two = Give(player, "Alvarus_Letter,_part_2_of_3");
        DbInventoryItem three = Give(player, "Alvarus_Letter,_part_3_of_3");
        int twoSlot = two.SlotPosition;

        Assert.That(ArtifactMgr.IsArtifactScroll(one), Is.True, "our scroll templates are generic items (Object_Type 0)");
        Assert.That(ArtifactMgr.TryCombineScrolls(player, one, one), Is.False);
        Assert.That(ArtifactMgr.TryCombineScrolls(player, one, two), Is.True);

        DbInventoryItem pair = player.Pack[twoSlot];
        Assert.That(pair.Name, Is.EqualTo("Alvarus' Letter, parts 1 and 2 of 3"));
        Assert.That(player.Pack.Values, Does.Not.Contain(one).And.Not.Contain(two));
        Assert.That(player.Messages.Last(), Does.Contain(LeggingsName).And.Not.EqualTo(Leggings));

        Assert.That(ArtifactMgr.TryCombineScrolls(player, three, pair), Is.True);
        Assert.That(player.Pack.Values.Select(i => i.Name), Is.EqualTo(new[] { Book }));
        Assert.That(ArtifactMgr.HasBook(player, Leggings), Is.True);
        Assert.That(player.Messages.Last(), Does.Contain(Book).And.Contain(LeggingsName));

        // The book is the whole story for a scholar.
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        _usable = ["Alvarus_Leggings_plate"];
        ArtifactAwakening.HandIn(player, Npc<Scholar>(), player.Pack.Values.Single(), out bool handled);
        Assert.That(handled, Is.True);
        Assert.That(_delivered.Select(i => i.Id_nb), Is.EqualTo(new[] { "Alvarus_Leggings_plate" }));
        Assert.That(player.Pack.Values.Select(i => i.Name), Is.EqualTo(new[] { LeggingsName }));
    }

    [Test]
    public void HandedPages_UseTheFewestPiecesWithoutWastingAPage()
    {
        Player player = NewPlayer();
        _usable = ["Alvarus_Leggings_plate"];
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbItemTemplate page = Templates["Alvarus_Letter,_part_1_of_3"];
        DbInventoryItem pair12 = ArtifactMgr.CreatePages(Leggings, ArtifactMgr.Book.Page1 | ArtifactMgr.Book.Page2, page).Item;
        DbInventoryItem pair23 = ArtifactMgr.CreatePages(Leggings, ArtifactMgr.Book.Page2 | ArtifactMgr.Book.Page3, page).Item;
        Assert.That(player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, pair12), Is.True);
        Assert.That(player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, pair23), Is.True);
        Give(player, "Alvarus_Letter,_part_3_of_3");

        ArtifactAwakening.HandIn(player, Npc<Scholar>(), pair12, out _);

        Assert.That(_delivered, Has.Count.EqualTo(1));
        Assert.That(PackNames(player), Is.EquivalentTo(new[] { "Alvarus' Letter, parts 2 and 3 of 3", LeggingsName }),
            "pages 1+2 and the single page 3 are used; the second pair stays");
    }

    [Test]
    public void ScrollsOfDifferentArtifactsOrTheSamePage_DoNotCombine()
    {
        Player player = NewPlayer();
        DbInventoryItem one = Give(player, "Alvarus_Letter,_part_1_of_3");
        DbInventoryItem oneAgain = Give(player, "Alvarus_Letter,_part_1_of_3");
        DbInventoryItem token = Give(player, "alvarus_leggings_credit");

        Assert.That(ArtifactMgr.TryCombineScrolls(player, one, oneAgain), Is.False);
        Assert.That(ArtifactMgr.TryCombineScrolls(player, one, token), Is.False);
        Assert.That(player.Pack, Has.Count.EqualTo(3));
    }

    [Test]
    public void CreditToken_GrantsTheEncounterCredit_AtAphaestiaOrAScholar()
    {
        Player player = NewPlayer();
        DbInventoryItem token = Give(player, "alvarus_leggings_credit");

        Assert.That(ArtifactAwakening.IsCreditToken(token), Is.True, "Id_nb = lower(Credit) + _credit");
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, token.Id_nb), Is.Null, "worth buying");

        string reply = ArtifactAwakening.RedeemCreditToken(player, Npc<ArtifactCreditMerchant>(), token);

        Assert.That(reply, Does.Contain(LeggingsName));
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.True);
        Assert.That(player.Pack, Is.Empty, "the token is used up");
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit"), Does.Contain("already hold credit"),
            "a second token is not sold");
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit", forPurchase: true), Does.Contain("already hold credit"));

        DbInventoryItem second = Give(player, "alvarus_leggings_credit");
        string scholarReply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), second, out bool handled);
        Assert.That(handled, Is.True, "scholars take tokens too");
        Assert.That(scholarReply, Does.Contain("already hold credit").And.Contain(LeggingsName));
        Assert.That(player.Pack.Values, Does.Contain(second));

        DbInventoryItem orphan = Give(player, "atlantis_tablet_credit");
        Assert.That(ArtifactAwakening.IsCreditToken(orphan), Is.False, "no artifact behind it");
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, orphan.Id_nb), Does.Contain("no artifact"));
    }

    [Test]
    public void ACarriedUnredeemedToken_StopsASecondPurchase()
    {
        Player player = NewPlayer();
        Give(player, "alvarus_leggings_credit");

        Assert.That(ArtifactAwakening.HoldsCreditToken(player, Leggings), Is.True);
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit", forPurchase: true),
            Does.Contain("already carry").And.Contain(LeggingsName));
        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit"), Is.Null, "handing the carried one in still works");
    }

    [Test]
    public void CreditToken_ForAnotherClass_IsNotSold()
    {
        Player player = NewPlayer();
        foreach (DbItemTemplate template in Templates.Values.Where(t => t.Id_nb.StartsWith("Alvarus_Leggings")))
            template.AllowedClasses = "10";

        Assert.That(ArtifactAwakening.CreditTokenProblem(player, "alvarus_leggings_credit"), Does.Contain(LeggingsName).And.Contain("Armsman"));
    }

    [Test]
    public void BooklessArtifact_IsAwakenedOnCreditAlone_ByAnArtifactScholarOnly()
    {
        Player player = NewPlayer();
        Assert.That(ArtifactMgr.HasScrollSet(Bruiser), Is.False);
        Assert.That(ArtifactAwakening.Handles(Npc<Scholar>(), Bruiser), Is.False);
        Assert.That(ArtifactAwakening.Handles(Npc<ArtifactScholar>(), Bruiser), Is.True);
        Assert.That(ArtifactAwakening.Greeting(player, Npc<ArtifactScholar>()), Does.Contain("[Bruiser]"));
        Assert.That(ArtifactAwakening.Greeting(player, Npc<Scholar>()), Does.Not.Contain("[Bruiser]"));

        Assert.That(ArtifactAwakening.Whisper(player, Npc<ArtifactScholar>(), "bruiser"), Does.Contain("Bruiser").And.Contain("Aphaestia"),
            "no credit yet");
        DbInventoryItem token = Give(player, "bruiser_credit");
        ArtifactAwakening.RedeemCreditToken(player, Npc<ArtifactCreditMerchant>(), token);

        Assert.That(ArtifactAwakening.Whisper(player, Npc<Scholar>(), "Bruiser"), Does.Contain("Artifact Scholar"));
        Assert.That(_delivered, Is.Empty);

        string reply = ArtifactAwakening.Whisper(player, Npc<ArtifactScholar>(), "Bruiser");

        Assert.That(reply, Does.Contain("Bruiser"));
        Assert.That(_delivered.Select(i => i.Id_nb), Is.EqualTo(new[] { "Bruiser_Crush" }));
        Assert.That(ArtifactMgr.HasFinishedArtifactQuest(player, Bruiser), Is.True);
    }

    [Test]
    public void FullBackpack_KeepsEverything()
    {
        Player player = NewPlayer();
        _usable = ["Alvarus_Leggings_plate"];
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.True);
        DbInventoryItem first = Give(player, "Alvarus_Letter,_part_1_of_3");
        Give(player, "Alvarus_Letter,_part_2_of_3");
        Give(player, "Alvarus_Letter,_part_3_of_3");
        ArtifactAwakening.Deliver = (_, _, _) => false; // no room after all

        string reply = ArtifactAwakening.HandIn(player, Npc<Scholar>(), first, out _);

        Assert.That(reply, Does.Contain("too full").And.Contain(LeggingsName));
        Assert.That(PackNames(player), Is.EquivalentTo(new[] { Page1, Page2, Page3 }), "the pages come back");
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.True);
        Assert.That(ArtifactMgr.HasFinishedArtifactQuest(player, Leggings), Is.False);
    }

    [Test]
    public void EveryRefusalNamesTheArtifact()
    {
        Player player = NewPlayer();

        foreach (ArtifactRefusal refusal in Enum.GetValues<ArtifactRefusal>().Where(r => r != ArtifactRefusal.None))
        {
            string text = ArtifactAwakening.Explain(refusal, player, Leggings);
            Assert.That(text, Does.Contain(LeggingsName), refusal.ToString());
            Assert.That(text, Does.Not.Contain("1001"), refusal.ToString());
        }

        Assert.That(ArtifactAwakening.ArtifactList(player, Npc<Scholar>()), Does.Contain("[" + LeggingsName + "]"));
        Assert.That(ArtifactAwakening.Whisper(player, Npc<Scholar>(), "alvarus's leggings"), Does.Contain(LeggingsName));
        Assert.That(ArtifactAwakening.Progress(player), Does.Contain("Aphaestia"), "nothing begun yet: where to start");
    }

    [Test]
    public void ArtifactNames_ComeFromTheTemplate_AndTheMessagePlaceholdersAreNotShown()
    {
        Assert.That(ArtifactMgr.GetArtifactName(Leggings), Is.EqualTo(LeggingsName));
        Assert.That(ArtifactMgr.ReadableText("1001", "fallback"), Is.EqualTo("fallback"));
        Assert.That(ArtifactMgr.ReadableText(" ", "fallback"), Is.EqualTo("fallback"));
        Assert.That(ArtifactMgr.ReadableText("The pages glow.", "fallback"), Is.EqualTo("The pages glow."));
    }

    [Test]
    public void AwakenedRecord_SurvivesAReload()
    {
        Player player = NewPlayer();
        var row = new DbQuest
        {
            Character_ID = PlayerId,
            Name = typeof(ArtifactQuest).FullName,
            Step = ArtifactQuest.AWAKENED_STEP,
            CustomPropertiesString = $"{ArtifactQuest.ARTIFACT_ID_PROPERTY}={Leggings};{ArtifactQuest.ITEM_PROPERTY}=Alvarus_Leggings_plate;",
        };

        AbstractQuest loaded = AbstractQuest.LoadFromDatabase(player, row);

        Assert.That(loaded, Is.InstanceOf<ArtifactQuest>());
        Assert.That(((ArtifactQuest) loaded).ArtifactID, Is.EqualTo(Leggings));
        player.AddFinishedQuest(loaded); // what GamePlayer.HandleQuests does for Step < 0
        Assert.That(ArtifactMgr.HasFinishedArtifactQuest(player, Leggings), Is.True);
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, Leggings), Is.False, "no credit for an awakened artifact");
    }

    [Test]
    public void VersionLabels_ReadLikeTheItem()
    {
        DbItemTemplate Weapon(string id, eObjectType type, eInventorySlot slot, int damage) =>
            new() { Id_nb = id, Object_Type = (int) type, Item_Type = (int) slot, Type_Damage = damage };
        DbItemTemplate Magic(string id) => new() { Id_nb = id, Object_Type = (int) eObjectType.Magical, Item_Type = (int) eInventorySlot.Waist };
        DbItemTemplate Armor(string id, eObjectType type) => new() { Id_nb = id, Object_Type = (int) type, Item_Type = (int) eInventorySlot.TorsoArmor };
        DbItemTemplate ShieldOf(string id, int size) => new() { Id_nb = id, Object_Type = (int) eObjectType.Shield, Item_Type = (int) eInventorySlot.LeftHandWeapon, Type_Damage = size };

        Assert.That(ArtifactAwakening.Labels(
            [
                Weapon("Battler_Alb_Crush", eObjectType.CrushingWeapon, eInventorySlot.RightHandWeapon, 1),
                Weapon("Battler_Alb_Crush_2h", eObjectType.TwoHandedWeapon, eInventorySlot.TwoHandWeapon, 1),
                Weapon("Battler_Mid_Crush_2h", eObjectType.Hammer, eInventorySlot.TwoHandWeapon, 1),
                Weapon("Battler_Hib_Blade_2h", eObjectType.LargeWeapons, eInventorySlot.TwoHandWeapon, 2),
            ]), Is.EqualTo(new[] { "Crushing", "Two-Handed (Crush)", "Two-Handed Hammer", "Large Weapon (Slash)" }));

        Assert.That(ArtifactAwakening.Labels(
            [
                Armor("GSV_Leather", eObjectType.Leather),
                Armor("GSV_Reinforced_Archery", eObjectType.Reinforced),
                Armor("GSV_Reinforced_Melee", eObjectType.Reinforced),
                Armor("GOV_Cloth", eObjectType.Cloth),
                Armor("GOV_Cloth_Int", eObjectType.Cloth),
            ]), Is.EqualTo(new[] { "Leather", "Reinforced Archery", "Reinforced Melee", "Cloth", "Cloth Intelligence" }));

        Assert.That(ArtifactAwakening.Labels([Magic("Ceremonial_Bracers_Con"), Magic("Orions_Belt_Frair"), Magic("SOTG_Caster")]),
            Is.EqualTo(new[] { "Constitution", "Friar", "Caster" }));
        Assert.That(ArtifactAwakening.Labels([ShieldOf("Atens_Shield", 1), ShieldOf("Atens_Shield_Large", 3)]),
            Is.EqualTo(new[] { "Small Shield", "Large Shield" }));
        Assert.That(ArtifactAwakening.Labels([Magic("X_Same"), Magic("Y_Same")]), Is.EqualTo(new[] { "Same 1", "Same 2" }), "never twice");
    }

    [Test]
    public void TheArtifactNpcClassesResolveFromTheirMobRows()
    {
        Assembly server = typeof(GameServer).Assembly;

        Assert.That(server.GetType("DOL.GS.Scholar"), Is.EqualTo(typeof(Scholar)));
        Assert.That(typeof(Researcher).IsAssignableFrom(typeof(Scholar)), Is.True);
        Assert.That(server.GetType("DOL.GS.ArtifactScholar")?.BaseType, Is.EqualTo(typeof(Scholar)));
        Assert.That(server.GetType("DOL.GS.ArtifactCreditMerchant")?.BaseType, Is.EqualTo(typeof(GameBountyMerchant)));
        Assert.That(server.GetType("DOL.GS.Quests.Atlantis.ArtifactQuest")?.GetConstructor([typeof(GamePlayer), typeof(DbQuest)]), Is.Not.Null,
            "loadable by AbstractQuest.LoadFromDatabase");
        Assert.That(Npc<ArtifactScholar>().AwakensWithoutBook, Is.True);
        Assert.That(Npc<Scholar>().AwakensWithoutBook, Is.False);
    }
}
