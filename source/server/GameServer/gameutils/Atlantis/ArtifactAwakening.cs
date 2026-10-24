using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Quests.Atlantis;

namespace DOL.GS
{
	/// <summary>Why a scholar will not awaken an artifact (yet).</summary>
	public enum ArtifactRefusal
	{
		None,
		Unknown,
		AlreadyAwakened,
		AlreadyCarried,
		NotForClass,
		TooLow,
		NoCredit,
		NoBook,
	}

	/// <summary>One form an artifact can take for a player: the label the player names, and the template.</summary>
	public sealed record ArtifactChoice(string Label, string Version, DbItemTemplate Template);

	/// <summary>
	/// How a player earns an artifact (TOA_PLAN WP3.1b), data-driven on our Artifact rows.
	/// Every scholar (<see cref="Scholar"/>) awakens every artifact that has a scroll set;
	/// an <see cref="ArtifactScholar"/> also awakens the three without one on credit alone.
	///
	/// <list type="number">
	/// <item>Encounter credit: an encounter (WP3.3) or a credit token bought from Aphaestia
	/// for bounty points and handed to her or to a scholar.</item>
	/// <item>The story: three scroll pages (drops in Atlantis, or the binders Zosyne and
	/// Mnosus), combined in the backpack into the book, or handed in as pages.</item>
	/// <item>The scholar takes the book, asks which form the artifact should take when more
	/// than one version fits the player's class and realm, hands the item over, uses up the
	/// credit and records the artifact as awakened: one of each per character.</item>
	/// </list>
	/// The NPC classes only route Interact, WhisperReceive and ReceiveItem here; every text a
	/// scholar says comes from this class. docs/TOA_ARTIFACTS.md "Earning an artifact".
	/// </summary>
	public static class ArtifactAwakening
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>Lowest level a scholar awakens an artifact for (DOLSharp ArtifactQuest.Level).</summary>
		public const int MIN_LEVEL = 45;

		/// <summary>Whisper keyword for the list of artifacts.</summary>
		public const string ARTIFACTS_KEYWORD = "artifacts";

		/// <summary>Whisper keyword for the player's own state.</summary>
		public const string PROGRESS_KEYWORD = "progress";

		/// <summary>Suffix of the credit token item IDs (<c>lower(Credit) + "_credit"</c>).</summary>
		public const string CREDIT_SUFFIX = "_credit";

		/// <summary>Whether the player can use a version's template (armor, weapon skill). Test seam.</summary>
		public static Func<GamePlayer, DbItemTemplate, bool> CanUseTemplate { get; set; } = DefaultCanUse;

		/// <summary>Puts an item into the player's backpack; false if it does not fit. Test seam.</summary>
		public static Func<GamePlayer, GameLiving, DbInventoryItem, bool> Deliver { get; set; } = DefaultDeliver;

		private static bool DefaultCanUse(GamePlayer player, DbItemTemplate template)
		{
			try
			{
				return player.HasAbilityToUseItem(template);
			}
			catch (Exception)
			{
				return true;
			}
		}

		private static bool DefaultDeliver(GamePlayer player, GameLiving source, DbInventoryItem item)
		{
			return player.ReceiveItem(source, item);
		}

		#region Rules

		/// <summary>
		/// Whether a scholar may awaken the artifact for the player now, apart from the book.
		/// Order: unknown, already awakened, already carried, not for the class, level, credit.
		/// </summary>
		public static ArtifactRefusal Check(GamePlayer player, string artifactID)
		{
			if (player == null || ArtifactMgr.GetArtifact(artifactID) == null)
				return ArtifactRefusal.Unknown;

			if (ArtifactMgr.HasFinishedArtifactQuest(player, artifactID))
				return ArtifactRefusal.AlreadyAwakened;

			if (ArtifactMgr.GetArtifacts(player).Contains(artifactID))
				return ArtifactRefusal.AlreadyCarried;

			if (!player.CanReceiveArtifact(artifactID) || GetChoices(player, artifactID).Count == 0)
				return ArtifactRefusal.NotForClass;

			if (player.Level < MIN_LEVEL)
				return ArtifactRefusal.TooLow;

			if (!ArtifactMgr.HasEncounterCredit(player, artifactID))
				return ArtifactRefusal.NoCredit;

			return ArtifactRefusal.None;
		}

		/// <summary>
		/// The versions the player may choose from: the ArtifactXItem templates of the player's
		/// realm whose AllowedClasses name the class, narrowed to what the player can wear or
		/// wield ( nothing if that leaves none), in Version order, each with a readable label.
		/// </summary>
		public static List<ArtifactChoice> GetChoices(GamePlayer player, string artifactID)
		{
			if (player?.CharacterClass == null || string.IsNullOrEmpty(artifactID))
				return [];

			Dictionary<string, DbItemTemplate> versions = ArtifactMgr.GetArtifactVersions(artifactID, (eCharacterClass) player.CharacterClass.ID, player.Realm);

			if (versions == null || versions.Count == 0)
				return [];

			List<KeyValuePair<string, DbItemTemplate>> ordered = versions
				.OrderBy(v => int.TryParse(v.Key, out int n) ? n : int.MaxValue)
				.ThenBy(v => v.Key, StringComparer.Ordinal)
				.ToList();
			List<KeyValuePair<string, DbItemTemplate>> usable = ordered.Where(v => CanUseTemplate(player, v.Value)).ToList();

			// None the character could wear or wield: no choice at all (NotForClass), rather than an
			// artifact that stays in the pack for good.
			if (usable.Count == 0)
			{
				if (log.IsDebugEnabled)
					log.Debug($"Artifact {artifactID}: {ordered.Count} versions for {player.Name}'s class and realm, none usable");

				return [];
			}

			List<string> labels = Labels(usable.Select(v => v.Value).ToList());
			return usable.Select((v, i) => new ArtifactChoice(labels[i], v.Key, v.Value)).ToList();
		}

		/// <summary>The choice the player named: its label or its Version, ignoring case.</summary>
		public static ArtifactChoice FindChoice(List<ArtifactChoice> choices, string said)
		{
			string text = said?.Trim();

			if (string.IsNullOrEmpty(text))
				return null;

			return choices.FirstOrDefault(c => string.Equals(c.Label, text, StringComparison.OrdinalIgnoreCase)) ??
				choices.FirstOrDefault(c => string.Equals(c.Version, text, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The artifact whose readable name the player said, or null.</summary>
		public static string FindArtifactByName(string said)
		{
			string text = said?.Trim();

			if (string.IsNullOrEmpty(text))
				return null;

			foreach (DbArtifact artifact in ArtifactMgr.GetArtifacts().OrderBy(a => a.ArtifactID, StringComparer.Ordinal))
			{
				if (string.Equals(ArtifactMgr.GetArtifactName(artifact.ArtifactID), text, StringComparison.OrdinalIgnoreCase))
					return artifact.ArtifactID;
			}

			return null;
		}

		/// <summary>Whether this scholar awakens the artifact: books for everyone, the bookless ones only at an Artifact Scholar.</summary>
		public static bool Handles(Scholar scholar, string artifactID)
		{
			return ArtifactMgr.HasScrollSet(artifactID) || (scholar?.AwakensWithoutBook ?? false);
		}

		#endregion

		#region Version labels

		/// <summary>
		/// Labels for a list of version templates: armor class ("Plate"), shield size ("Large
		/// Shield"), weapon kind ("Two-Handed (Crush)"), and where that is not unique, or for
		/// jewellery, the last word of the item ID ("Caster", "Melee"). Never empty, never twice.
		/// </summary>
		public static List<string> Labels(IList<DbItemTemplate> templates)
		{
			var traits = templates.Select(TraitLabel).ToList();
			var labels = traits.ToList();

			for (int i = 0; i < labels.Count; i++)
			{
				string trait = traits[i];
				bool duplicate = trait != null && traits.Where((l, j) => j != i && l == trait).Any();

				if (trait == null || duplicate)
				{
					string suffix = SuffixLabel(templates[i].Id_nb, trait);
					labels[i] = trait == null ? suffix : string.IsNullOrEmpty(suffix) ? trait : $"{trait} {suffix}";
				}
			}

			for (int i = 0; i < labels.Count; i++)
			{
				if (string.IsNullOrWhiteSpace(labels[i]))
					labels[i] = "Standard";
			}

			for (int i = 0; i < labels.Count; i++)
			{
				string label = labels[i];

				if (labels.Count(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)) > 1)
				{
					int n = 1;

					for (int j = 0; j < labels.Count; j++)
					{
						if (string.Equals(labels[j], label, StringComparison.OrdinalIgnoreCase))
							labels[j] = $"{label} {n++}";
					}
				}
			}

			return labels;
		}

		private static string TraitLabel(DbItemTemplate template)
		{
			if (template == null)
				return null;

			var type = (eObjectType) template.Object_Type;

			if (type is >= eObjectType.Cloth and <= eObjectType.Scale)
				return type.ToString();

			if (type == eObjectType.Shield)
			{
				return template.Type_Damage switch
				{
					1 => "Small Shield",
					2 => "Medium Shield",
					3 => "Large Shield",
					_ => "Shield",
				};
			}

			if (type == eObjectType.Instrument)
				return "Instrument";

			if (template.Object_Type < (int) eObjectType._FirstWeapon || template.Object_Type > (int) eObjectType._LastWeapon)
				return null;

			string kind = type switch
			{
				eObjectType.CrushingWeapon => "Crushing",
				eObjectType.SlashingWeapon => "Slashing",
				eObjectType.ThrustWeapon => "Thrusting",
				eObjectType.TwoHandedWeapon => "Two-Handed",
				eObjectType.PolearmWeapon => "Polearm",
				eObjectType.Staff => "Staff",
				eObjectType.Longbow => "Longbow",
				eObjectType.Crossbow => "Crossbow",
				eObjectType.Flexible => "Flexible",
				eObjectType.Sword => "Sword",
				eObjectType.Hammer => "Hammer",
				eObjectType.Axe => "Axe",
				eObjectType.Spear => "Spear",
				eObjectType.CompositeBow => "Composite Bow",
				eObjectType.Thrown => "Thrown",
				eObjectType.LeftAxe => "Left Axe",
				eObjectType.HandToHand => "Hand to Hand",
				eObjectType.RecurvedBow => "Recurve Bow",
				eObjectType.Blades => "Blades",
				eObjectType.Blunt => "Blunt",
				eObjectType.Piercing => "Piercing",
				eObjectType.LargeWeapons => "Large Weapon",
				eObjectType.CelticSpear => "Celtic Spear",
				eObjectType.Scythe => "Scythe",
				eObjectType.FistWraps => "Fist Wraps",
				eObjectType.MaulerStaff => "Mauler Staff",
				_ => "Weapon",
			};

			bool twoHandedByKind = type is eObjectType.TwoHandedWeapon or eObjectType.PolearmWeapon or eObjectType.Staff or
				eObjectType.LargeWeapons or eObjectType.CelticSpear or eObjectType.Scythe or eObjectType.Spear or eObjectType.MaulerStaff or
				eObjectType.Longbow or eObjectType.Crossbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Fired;

			if (template.Item_Type == (int) eInventorySlot.TwoHandWeapon && !twoHandedByKind)
				kind = "Two-Handed " + kind;

			bool damageVaries = type is eObjectType.TwoHandedWeapon or eObjectType.PolearmWeapon or eObjectType.LargeWeapons or
				eObjectType.CelticSpear or eObjectType.Spear or eObjectType.Flexible;

			if (damageVaries)
			{
				string damage = template.Type_Damage switch
				{
					1 => "Crush",
					2 => "Slash",
					3 => "Thrust",
					_ => null,
				};

				if (damage != null)
					kind = $"{kind} ({damage})";
			}

			return kind;
		}

		private static readonly Dictionary<string, string> SuffixWords = new(StringComparer.OrdinalIgnoreCase)
		{
			["2h"] = "Two-Handed",
			["lw"] = "Large Weapon",
			["int"] = "Intelligence",
			["con"] = "Constitution",
			["dex"] = "Dexterity",
			["qui"] = "Quickness",
			["str"] = "Strength",
			["frair"] = "Friar",
			["trust"] = "Thrust",
		};

		private static string SuffixLabel(string itemID, string trait)
		{
			if (string.IsNullOrEmpty(itemID))
				return string.Empty;

			string last = itemID.Split('_', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;

			if (trait != null && trait.Contains(last, StringComparison.OrdinalIgnoreCase))
				return string.Empty;

			if (SuffixWords.TryGetValue(last, out string word))
				return word;

			return last.Length == 0 ? string.Empty : char.ToUpperInvariant(last[0]) + last[1..];
		}

		#endregion

		#region Conversation

		/// <summary>What a scholar says on a click.</summary>
		public static string Greeting(GamePlayer player, Scholar scholar)
		{
			var text = new StringBuilder();
			ArtifactQuest pending = ArtifactQuest.Pending(player);

			if (pending != null)
				text.Append(ChoicePrompt(player, pending.ArtifactID)).Append("\n\n");

			text.Append($"Greetings, {player.Salutation()}. I study the artifacts of Atlantis, relics whose power lies asleep. ")
				.Append("Bring me the complete story of an artifact, its three scrolls or the book they form, once you have earned ")
				.Append("credit for its encounter, and I will awaken it for you.");

			List<string> bookless = Awakenable(player, scholar).Where(id => !ArtifactMgr.HasScrollSet(id)).ToList();

			if (bookless.Count > 0)
				text.Append("\n\nThe story of ").Append(string.Join(", ", bookless.Select(Bracketed)))
					.Append(" was never written down. Those I awaken on your encounter credit alone; name one when you are ready.");

			text.Append($"\n\nAsk me about the [{ARTIFACTS_KEYWORD}] I can awaken for you, or about your [{PROGRESS_KEYWORD}].");
			return text.ToString();
		}

		/// <summary>
		/// What a scholar answers to a whisper, or null if the text means nothing to it.
		/// Handles the version choice, the two keywords and artifact names.
		/// </summary>
		public static string Whisper(GamePlayer player, Scholar scholar, string said)
		{
			if (player == null || string.IsNullOrWhiteSpace(said))
				return null;

			string text = said.Trim();
			ArtifactQuest pending = ArtifactQuest.Pending(player);

			if (pending != null)
			{
				ArtifactChoice choice = FindChoice(GetChoices(player, pending.ArtifactID), text);

				if (choice != null)
					return Award(player, scholar, pending.ArtifactID, choice, pending);
			}

			if (text.Equals(ARTIFACTS_KEYWORD, StringComparison.OrdinalIgnoreCase))
				return ArtifactList(player, scholar);

			if (text.Equals(PROGRESS_KEYWORD, StringComparison.OrdinalIgnoreCase))
				return Progress(player);

			string artifactID = FindArtifactByName(text);
			return artifactID == null ? null : AboutArtifact(player, scholar, artifactID);
		}

		/// <summary>What a scholar says about one artifact; awakens a bookless one at an Artifact Scholar.</summary>
		public static string AboutArtifact(GamePlayer player, Scholar scholar, string artifactID)
		{
			string name = ArtifactMgr.GetArtifactName(artifactID);

			if (ArtifactQuest.Pending(player)?.ArtifactID == artifactID)
				return ChoicePrompt(player, artifactID);

			ArtifactRefusal refusal = Check(player, artifactID);

			if (!ArtifactMgr.HasScrollSet(artifactID))
			{
				if (!Handles(scholar, artifactID))
					return $"The story of {name} was never written down, so I cannot read its power from a book. An Artifact Scholar can awaken it on your encounter credit alone.";

				if (refusal != ArtifactRefusal.None)
					return Explain(refusal, player, artifactID);

				ArtifactQuest pending = ArtifactQuest.Pending(player);

				if (pending != null)
					return ChoicePrompt(player, pending.ArtifactID);

				return Begin(player, scholar, artifactID, null, null);
			}

			if (refusal != ArtifactRefusal.None)
				return Explain(refusal, player, artifactID);

			if (ArtifactMgr.HasBook(player, artifactID) || HeldPages(player, artifactID, null).Pages == ArtifactMgr.Book.AllPages)
				return $"You have earned the right to {name} and you carry its whole story. Hand it to me and I will awaken the artifact.";

			return Explain(ArtifactRefusal.NoBook, player, artifactID);
		}

		/// <summary>The artifacts this scholar can awaken for the player's class and realm.</summary>
		public static string ArtifactList(GamePlayer player, Scholar scholar)
		{
			List<string> ids = Awakenable(player, scholar);

			if (ids.Count == 0)
				return $"I know of no artifact that would answer to a {player.Salutation()}.";

			return $"These artifacts would answer to a {player.Salutation()}: " + string.Join(", ", ids.Select(Bracketed)) +
				".\n\nName one and I will tell you what you still need.";
		}

		private static List<string> Awakenable(GamePlayer player, Scholar scholar)
		{
			return ArtifactMgr.GetArtifacts()
				.Where(a => Handles(scholar, a.ArtifactID) && player.CanReceiveArtifact(a.ArtifactID) && GetChoices(player, a.ArtifactID).Count > 0)
				.Select(a => a.ArtifactID)
				.OrderBy(ArtifactMgr.GetArtifactName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>The player's state for every artifact begun: awakened, credit, pages.</summary>
		public static string Progress(GamePlayer player)
		{
			var lines = new List<string>();

			foreach (DbArtifact artifact in ArtifactMgr.GetArtifacts().OrderBy(a => ArtifactMgr.GetArtifactName(a.ArtifactID), StringComparer.OrdinalIgnoreCase))
			{
				string id = artifact.ArtifactID;
				string name = ArtifactMgr.GetArtifactName(id);

				if (ArtifactMgr.HasFinishedArtifactQuest(player, id))
				{
					lines.Add($"{name}: awakened.");
					continue;
				}

				bool credit = ArtifactMgr.HasEncounterCredit(player, id);
				ArtifactMgr.Book pages = HeldPages(player, id, null).Pages;
				int pageCount = PageCount(pages);

				if (!credit && pageCount == 0)
					continue;

				string story = !ArtifactMgr.HasScrollSet(id) ? "no book needed"
					: pages == ArtifactMgr.Book.AllPages ? "the whole story in your pack"
					: $"{pageCount} of 3 pages";
				lines.Add($"{name}: {(credit ? "encounter credit earned" : "no encounter credit yet")}, {story}.");
			}

			ArtifactQuest pending = ArtifactQuest.Pending(player);

			if (pending != null)
				lines.Insert(0, ChoicePrompt(player, pending.ArtifactID));

			if (lines.Count == 0)
				return "You have not begun the study of any artifact. Their pages are carried by the creatures of Atlantis, and Zosyne and Mnosus trade them for bounty. " +
					"Credit for an artifact's encounter is won in battle, or bought from Aphaestia with tales of your fights.";

			return string.Join("\n", lines);
		}

		/// <summary>The readable reason for a refusal; always names the artifact.</summary>
		public static string Explain(ArtifactRefusal refusal, GamePlayer player, string artifactID)
		{
			string name = ArtifactMgr.GetArtifactName(artifactID);

			return refusal switch
			{
				ArtifactRefusal.AlreadyAwakened =>
					$"I have already awakened {name} for you, {player.Salutation()}. An artifact answers to each bearer only once; I cannot call it forth a second time.",
				ArtifactRefusal.AlreadyCarried =>
					$"You already carry {name}. I will not awaken a second one for you.",
				ArtifactRefusal.NotForClass =>
					$"{name} would not answer to a {player.Salutation()}. Its power is meant for other hands; there are artifacts in Atlantis better suited to you.",
				ArtifactRefusal.TooLow =>
					$"You are not ready to bear {name} yet. Return when you have seen your {MIN_LEVEL}th season.",
				ArtifactRefusal.NoCredit =>
					$"You have not yet earned the right to {name}. Face the encounter that guards its secret in Atlantis, or trade tales of your battles with Aphaestia for that knowledge, then come back to me.",
				ArtifactRefusal.NoBook =>
					$"To awaken {name} I need its whole story. You still lack: {string.Join(", ", MissingPages(player, artifactID))}. " +
					"Its pages are carried by the creatures of Atlantis, and Zosyne and Mnosus trade them for bounty.",
				_ => $"I know nothing of {name}.",
			};
		}

		private static string ChoicePrompt(GamePlayer player, string artifactID)
		{
			string options = string.Join(", ", GetChoices(player, artifactID).Select(c => "[" + c.Label + "]"));
			string name = ArtifactMgr.GetArtifactName(artifactID);
			string held = ArtifactMgr.HasScrollSet(artifactID) ? $"I hold your book of {name}." : $"I am ready to awaken {name}.";
			return $"{held} Tell me which form the artifact should take: {options}.";
		}

		private static string Bracketed(string artifactID)
		{
			return "[" + ArtifactMgr.GetArtifactName(artifactID) + "]";
		}

		#endregion

		#region Pages

		/// <summary>The pages of an artifact's story in the backpack, and the items that make them up.</summary>
		public sealed record HeldStory(ArtifactMgr.Book Pages, List<DbInventoryItem> Items);

		/// <summary>
		/// The pages the player carries for the artifact (scrolls, pairs, the book). Starts with
		/// <paramref name="first"/> if given, then adds items that bring new pages, so the items
		/// returned are just enough to make up what is held.
		/// </summary>
		public static HeldStory HeldPages(GamePlayer player, string artifactID, DbInventoryItem first)
		{
			ArtifactMgr.Book pages = ArtifactMgr.Book.NoPage;
			var items = new List<DbInventoryItem>();

			void Add(DbInventoryItem item, bool allowOverlap)
			{
				string id = null;
				ArtifactMgr.Book itemPages = ArtifactMgr.GetPageNumbers(item, ref id);

				if (itemPages == ArtifactMgr.Book.NoPage || id != artifactID || (itemPages & ~pages) == ArtifactMgr.Book.NoPage)
					return;

				if (!allowOverlap && (itemPages & pages) != ArtifactMgr.Book.NoPage)
					return;

				if (itemPages == ArtifactMgr.Book.AllPages)
				{
					// The book alone is the whole story.
					items.Clear();
					items.Add(item);
					pages = ArtifactMgr.Book.AllPages;
					return;
				}

				items.Add(item);
				pages |= itemPages;
			}

			if (first != null)
				Add(first, true);

			if (player?.Inventory != null && pages != ArtifactMgr.Book.AllPages)
			{
				// The book first, then pairs, then single pages: the fewest items for the most pages.
				List<DbInventoryItem> backpack = player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
					.Where(i => i != first)
					.OrderByDescending(i =>
					{
						string id = null;
						return PageCount(ArtifactMgr.GetPageNumbers(i, ref id));
					})
					.ThenBy(i => i.SlotPosition)
					.ToList();

				// Pieces that add only missing pages first, so no page is used up twice.
				foreach (bool allowOverlap in new[] { false, true })
				{
					foreach (DbInventoryItem item in backpack)
					{
						if (pages == ArtifactMgr.Book.AllPages)
							break;

						Add(item, allowOverlap);
					}
				}
			}

			return new HeldStory(pages, items);
		}

		private static int PageCount(ArtifactMgr.Book pages)
		{
			int count = 0;

			if ((pages & ArtifactMgr.Book.Page1) != 0)
				count++;

			if ((pages & ArtifactMgr.Book.Page2) != 0)
				count++;

			if ((pages & ArtifactMgr.Book.Page3) != 0)
				count++;

			return count;
		}

		/// <summary>The names of the scroll pages the player still lacks for the artifact.</summary>
		public static List<string> MissingPages(GamePlayer player, string artifactID)
		{
			DbArtifact artifact = ArtifactMgr.GetArtifact(artifactID);

			if (artifact == null)
				return [];

			ArtifactMgr.Book held = ArtifactMgr.HasBook(player, artifactID) ? ArtifactMgr.Book.AllPages : HeldPages(player, artifactID, null).Pages;
			var missing = new List<string>();

			if ((held & ArtifactMgr.Book.Page1) == 0)
				missing.Add(artifact.Scroll1);

			if ((held & ArtifactMgr.Book.Page2) == 0)
				missing.Add(artifact.Scroll2);

			if ((held & ArtifactMgr.Book.Page3) == 0)
				missing.Add(artifact.Scroll3);

			return missing;
		}

		/// <summary>A real scroll template the kept book can be rebuilt from (<see cref="ArtifactMgr.FindPageTemplate"/>).</summary>
		private static string BookTemplateOf(string artifactID, List<DbInventoryItem> book)
		{
			return ArtifactMgr.FindPageTemplate(artifactID, book?.ToArray() ?? [])?.Id_nb;
		}

		/// <summary>Gives a kept book back (an abandoned version choice); onto the ground if the backpack is full.</summary>
		public static void ReturnBook(GamePlayer player, string artifactID, string bookTemplateID)
		{
			if (player == null || !ArtifactMgr.HasScrollSet(artifactID))
				return;

			DbItemTemplate template = null;

			try
			{
				if (!string.IsNullOrEmpty(bookTemplateID))
					template = GameServer.Database?.FindObjectByKey<DbItemTemplate>(bookTemplateID);
			}
			catch (Exception)
			{
				// No database: CreatePages tries artifact_scroll alone.
			}

			// No stored template (older choice, or it vanished): any page template of the artifact.
			if (template == null || template is DbItemUnique)
				template = ArtifactMgr.FindPageTemplate(artifactID);

			WorldInventoryItem book = ArtifactMgr.CreatePages(artifactID, ArtifactMgr.Book.AllPages, template);

			if (book?.Item == null)
			{
				if (log.IsErrorEnabled)
					log.Error($"Could not rebuild the book of artifact {artifactID} for {player.Name}: no scroll template");

				return;
			}

			if (!player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, book.Item))
				player.CreateItemOnTheGround(book.Item);

			InventoryLogging.LogInventoryAction("(ARTIFACT;book)", player, eInventoryActionType.Quest, book.Item.Template, 1);
			player.Out.SendMessage($"You take back {book.Item.Name}.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
		}

		#endregion

		#region Hand-in

		/// <summary>
		/// An item handed to a scholar: a credit token, a page or a book. Returns the reply, or
		/// null with <paramref name="handled"/> false for anything else. A refused item stays
		/// with the player.
		/// </summary>
		public static string HandIn(GamePlayer player, Scholar scholar, DbInventoryItem item, out bool handled)
		{
			handled = false;

			if (player == null || item == null)
				return null;

			if (IsCreditToken(item))
			{
				handled = true;
				return RedeemCreditToken(player, scholar, item);
			}

			string artifactID = null;

			if (ArtifactMgr.GetPageNumbers(item, ref artifactID) == ArtifactMgr.Book.NoPage)
				return null;

			handled = true;
			ArtifactQuest pending = ArtifactQuest.Pending(player);

			if (pending != null)
				return pending.ArtifactID == artifactID
					? ChoicePrompt(player, artifactID)
					: $"One book at a time. {ChoicePrompt(player, pending.ArtifactID)}";

			ArtifactRefusal refusal = Check(player, artifactID);

			if (refusal != ArtifactRefusal.None)
				return Explain(refusal, player, artifactID);

			HeldStory story = HeldPages(player, artifactID, item);

			if (story.Pages != ArtifactMgr.Book.AllPages)
				return Explain(ArtifactRefusal.NoBook, player, artifactID);

			return Begin(player, scholar, artifactID, story.Items, BookTemplateOf(artifactID, story.Items));
		}

		/// <summary>
		/// Starts the awakening once every check passed: hands the artifact over at once when
		/// only one version fits, else keeps the book and asks for the version.
		/// </summary>
		private static string Begin(GamePlayer player, GameLiving scholar, string artifactID, List<DbInventoryItem> book, string bookTemplateID)
		{
			string name = ArtifactMgr.GetArtifactName(artifactID);
			List<ArtifactChoice> choices = GetChoices(player, artifactID);

			if (choices.Count == 0)
			{
				if (log.IsInfoEnabled)
					log.Info($"Artifact {artifactID} refused to {player.Name}: no version the class can use");

				return Explain(ArtifactRefusal.NotForClass, player, artifactID);
			}

			if (choices.Count == 1)
				return Award(player, scholar, artifactID, choices[0], null, book);

			if (!TakeBook(player, scholar, book))
				return $"I could not take the story of {name} from you. Try again.";

			if (ArtifactQuest.StartChoice(player, artifactID, bookTemplateID) == null)
			{
				GiveBack(player, book);
				return $"I cannot begin the awakening of {name} now. Try again.";
			}

			string options = string.Join(", ", choices.Select(c => "[" + c.Label + "]"));
			return $"{(book != null ? "So this is the story of " + name + ". " : string.Empty)}{name} can take more than one form for you. Which shall it be: {options}?";
		}

		/// <summary>
		/// Hands the chosen version over, then uses up the book (if not already kept), the
		/// encounter credit and the pending choice, and records the artifact as awakened.
		/// Nothing changes when the backpack has no room.
		/// </summary>
		private static string Award(GamePlayer player, GameLiving scholar, string artifactID, ArtifactChoice choice, ArtifactQuest pending, List<DbInventoryItem> book = null)
		{
			string name = ArtifactMgr.GetArtifactName(artifactID);

			if (pending == null)
			{
				ArtifactRefusal refusal = Check(player, artifactID);

				if (refusal != ArtifactRefusal.None)
					return Explain(refusal, player, artifactID);
			}

			if (player.Inventory != null &&
				player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid &&
				(book == null || book.Count == 0))
				return $"Your pack is too full to carry {name}. Make room and speak to me again.";

			DbInventoryItem artifact = GameInventoryItem.Create(choice.Template);

			if (!TakeBook(player, scholar, book))
				return $"I could not take the story of {name} from you. Try again.";

			if (!Deliver(player, scholar, artifact))
			{
				GiveBack(player, book);
				return $"Your pack is too full to carry {name}. Make room and speak to me again.";
			}

			InventoryLogging.LogInventoryAction(scholar, player, eInventoryActionType.Quest, choice.Template, 1);

			if (!ArtifactMgr.ConsumeEncounterCredit(player, artifactID) && log.IsWarnEnabled)
				log.Warn($"Artifact {artifactID} handed to {player.Name} without an encounter credit to consume");

			ArtifactQuest.RecordAwakened(player, artifactID, choice.Template.Id_nb, pending);

			if (log.IsInfoEnabled)
				log.Info($"Artifact {artifactID} ({choice.Template.Id_nb}) awakened for {player.Name}");

			DbArtifact row = ArtifactMgr.GetArtifact(artifactID);
			string received = ArtifactMgr.ReadableText(row?.MessageReceiveBook,
				$"The scholar reads the last lines aloud, and {name} stirs with power long asleep.");
			player.Out.SendMessage(received, eChatType.CT_Important, eChatLoc.CL_SystemWindow);

			return $"Here is {name}, {player.Salutation()}. It is bound to you now. It grows stronger as you fight; slay the enemies and monsters of the realms to awaken all of its abilities.";
		}

		private static bool TakeBook(GamePlayer player, GameLiving scholar, List<DbInventoryItem> book)
		{
			if (book == null || book.Count == 0)
				return true;

			var taken = new List<DbInventoryItem>();

			foreach (DbInventoryItem page in book)
			{
				if (!player.Inventory.RemoveItem(page))
				{
					GiveBack(player, taken);
					return false;
				}

				taken.Add(page);
				InventoryLogging.LogInventoryAction(player, scholar, eInventoryActionType.Quest, page.Template, page.Count);
			}

			return true;
		}

		private static void GiveBack(GamePlayer player, List<DbInventoryItem> items)
		{
			if (items == null)
				return;

			foreach (DbInventoryItem item in items)
			{
				if (!player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
					player.CreateItemOnTheGround(item);
			}
		}

		#endregion

		#region Credit tokens

		/// <summary>Whether the item is a credit token of one of our artifacts (<c>lower(Credit) + "_credit"</c>).</summary>
		public static bool IsCreditToken(DbInventoryItem item)
		{
			return item?.Id_nb != null && item.Id_nb.EndsWith(CREDIT_SUFFIX, StringComparison.OrdinalIgnoreCase) &&
				ArtifactMgr.GetArtifactIDFromCredit(item.Id_nb) != null;
		}

		/// <summary>
		/// Why the player should not buy or redeem this credit token, or null if it is useful:
		/// no artifact behind it, already awakened or carried, credit already held, or no
		/// version for the class and realm. For a purchase, also an unredeemed token of the
		/// same artifact already in the inventory.
		/// </summary>
		public static string CreditTokenProblem(GamePlayer player, string tokenID, bool forPurchase = false)
		{
			string artifactID = ArtifactMgr.GetArtifactIDFromCredit(tokenID);

			if (artifactID == null)
				return "That knowledge leads to no artifact I could help you with.";

			ArtifactRefusal refusal = Check(player, artifactID);

			return refusal switch
			{
				ArtifactRefusal.AlreadyAwakened or ArtifactRefusal.AlreadyCarried or ArtifactRefusal.NotForClass => Explain(refusal, player, artifactID),
				_ when ArtifactMgr.HasEncounterCredit(player, artifactID) =>
					$"You already hold credit for the encounter of {ArtifactMgr.GetArtifactName(artifactID)}.",
				_ when forPurchase && HoldsCreditToken(player, artifactID) =>
					$"You already carry my credit for {ArtifactMgr.GetArtifactName(artifactID)}. Hand it to me or to a scholar before you buy another.",
				_ => null,
			};
		}

		/// <summary>Whether the player carries a credit token of this artifact (backpack, character inventory, anywhere in the inventory).</summary>
		public static bool HoldsCreditToken(GamePlayer player, string artifactID)
		{
			ICollection<DbInventoryItem> items = player?.Inventory?.AllItems;

			return items != null && items.Any(item => IsCreditToken(item) && ArtifactMgr.GetArtifactIDFromCredit(item.Id_nb) == artifactID);
		}

		/// <summary>
		/// A credit token handed to Aphaestia or a scholar: grants the encounter credit and
		/// takes the token. A token that cannot be used stays with the player.
		/// </summary>
		public static string RedeemCreditToken(GamePlayer player, GameLiving npc, DbInventoryItem token)
		{
			string artifactID = ArtifactMgr.GetArtifactIDFromCredit(token?.Id_nb);
			string problem = CreditTokenProblem(player, token?.Id_nb);

			if (problem != null)
				return problem;

			string name = ArtifactMgr.GetArtifactName(artifactID);

			if (!ArtifactMgr.GrantArtifactBountyCredit(player, token.Id_nb))
				return $"I cannot record your credit for {name}. Keep the token and try again.";

			if (!player.Inventory.RemoveItem(token))
			{
				// Keep it consistent: no token removed, no credit granted.
				ArtifactMgr.ConsumeEncounterCredit(player, artifactID);
				return $"I cannot take your credit for {name}. Try again.";
			}

			InventoryLogging.LogInventoryAction(player, npc, eInventoryActionType.Merchant, token.Template, token.Count);
			return $"Your tales are worth the knowledge of {name}. You now hold credit for its encounter; bring its story to a scholar and the artifact can be awakened.";
		}

		#endregion
	}
}
