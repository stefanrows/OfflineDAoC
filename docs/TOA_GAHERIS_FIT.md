# Trials of Atlantis: fit of the gaheris scripts to our server (WP0.2)

Static analysis only, 2026-10-24. Nothing was compiled, no server was started,
and no live database was read. Data counts come from the read-only dev snapshot
`playable-dev/db-snapshot/opendaoc.snapshot.db`. Plan: [TOA_PLAN.md](TOA_PLAN.md),
background: [TOA_RESEARCH.md](TOA_RESEARCH.md).

Source examined: `Pactor/gaheris` at commit `10d0f84` (2026-10-15), cloned
read-only at `/mnt/c/OfflineDAoC-ext/gaheris`. Our tree: `source/server`
(GameServer, CoreDatabase, CoreBase), .NET 10, same as gaheris.

## Summary

The API surface fits well: almost every type, method, event and property the
scripts call exists in our tree with the same signature. The real problems
are elsewhere:

1. **gaheris's artifact system is not wired end to end.** Nothing calls
   `ArtifactMgr.Init()`, no per-artifact quest or encounter classes exist,
   nothing combines scrolls, nothing marks artifact item templates as
   `InventoryArtifact`, and the four encounter mobs grant no artifact credit.
   Adopting the files gives us a compiled artifact library, not a working
   feature.
2. **Our artifact data uses numeric IDs and placeholders** (`ArtifactID`
   `1001`, `EncounterID`/`QuestID` `Alvarus_Leggings`, `ScholarID` `1001`,
   numeric `Version` keys). DOL's type-name-based quest lookup cannot work on
   it. The quest layer needs a data-driven rewrite.
3. **Our global `Dying` event carries no `PlayerKillers`.** Both gaheris XP
   hooks (ML and artifacts) rely on that list; on our server they would credit
   only the final killer. One-line core fix.
4. **Master Levels fit almost as is.** Only the mercenary references and the
   missing ML data (WP2.1) stand between `MasterLevels.cs` and a working
   Arbiter.

| Script | Verdict | Why (one line) |
|---|---|---|
| `toa/ArtifactLog.cs` | drop | replace with our `DOL.Logging.LoggerManager.Create`; same method names |
| `toa/ArtifactCompat.cs` | needs shim | `QuestList()` extension collides with our `GamePlayer.QuestList` property; rename |
| `toa/ArtifactMgr.cs` | needs shim | API fits; needs an `Init()` caller, credit-item matching fix, data-driven quest lookup |
| `toa/InventoryArtifact.cs` | fits, plus data | compiles against our `GameInventoryItem`; needs `itemtemplate.ClassType` set and a stat refresh on level-up |
| `toa/ArtifactExperience.cs` | needs shim | `GameMercenary`/`GaherisLoot` gone; depends on the `PlayerKillers` core fix |
| `toa/ArtifactEncounter.cs` | fits | 49-line base class, compiles unchanged |
| `toa/ArtifactQuest.cs` | needs rewrite | assumes one subclass per artifact and real scholar names; our data has neither |
| `toa/ArtifactTurnInQuest.cs` | needs rewrite or drop | relies on DOL `Version` keys (`dmg;wep;stat`); ours are `0`..`15`; its `FinishQuest` is broken on our quest API |
| `toa/Scholar.cs` | needs rewrite | built on `QuestListToGive` of per-artifact quests that do not exist; shows `[1001]` |
| `toa/ArtifactScholar.cs` | fits with shim | only the `QuestList()` rename; useful only if the turn-in quest is kept |
| `toa/ArtifactCreditMerchant.cs` | needs shim | compiles; credit match `artifact.Credit == item.Name` hits 3 of 57 in our DB |
| `toa/Encounters.cs` | fits, plus wiring | compiles; grants no artifact credit; spoils item `glowing_dreaded_seal` absent |
| `toa/MasterLevelsMerchant.cs` | fits | API identical; `GaherisArbiter.Announce` loses its mercenary loop |
| `progression/MasterLevels.cs` | needs shim | ML apparatus identical; remove `GameMercenary`/`MercenaryManager`/`GaherisLoot`; needs `PlayerKillers` fix |
| `progression/MasterLevelHolds.cs` | needs shim | ECS API identical; subclass-with-same-`[SpellHandler]` trick only works from a script assembly |
| `core/MovementWatch.cs` | fits | every member it reads exists in our `GameLiving` |

## How scripts load in our server

Our server has the same boot compiler as gaheris's target:
`GameServer.CompileScripts()` (`GameServer/GameServer.cs:710`) compiles every
`*.cs` under `<server root>/scripts` recursively with Roslyn
(`ScriptMgr.CompileScripts`, `DOLScriptCompiler`) into
`lib/GameServerScripts.dll`, referencing every loaded assembly.
`serverconfig.xml` in `playable/runtime/server` has
`<EnableCompilation>True</EnableCompilation>`. The runtime `scripts` folder
today holds only `dbupdater/` (no `.cs` files). Script assemblies are searched
before `GameServer.dll` for spell handlers, quest types and `ClassType`
strings. Server properties declared with `[ServerProperty]` in a script are
found, because scripts compile before `Properties.InitProperties`
(`GameServer.cs:355` then `:360`). `[ScriptLoadedEvent]` fires after the world
and quests are loaded (`GameServer.cs:550`), so `GetNPCsByName` in an
`Init` works.

So both options are technically possible:

| | A: drop into `scripts/` | B: ordinary GameServer source (recommended) |
|---|---|---|
| Build check | none; errors appear only at server boot | `winnet.sh build`, tests can reference the types |
| Failure mode | one bad file disables **every** script | normal build failure |
| Deploy | `deploy.sh` and the release pack do not ship `scripts/`; new plumbing needed | ships with `GameServer.dll` as today |
| Duplicate `[SpellHandler]` | script subclass wins (scripts searched first) | two handlers for one `eSpellType` in one assembly: order undefined; fix the core handler in place instead |
| `internal` core members | not reachable (gaheris could not raise `ItemBonusChanged`) | reachable |

**Recommendation: B.** Put the files under GameServer (for example
`gameobjects/Atlantis/` and `quests/Atlantis/`), keep every namespace and class
name that a DB row stores: `DOL.GS.Scholar`, `DOL.GS.ArtifactScholar`,
`DOL.GS.ArtifactCreditMerchant`, `DOL.GS.InventoryArtifact`,
`DOL.GS.Scripts.GaherisArbiter`, `DOL.GS.Scripts.MasterLevelsMerchant`. Our
`gameobjects/Atlantis/Scholar.cs` and `ArtifactScholar.cs` are fully commented
out; replace their contents in place so there is one definition per class.
`Arbiter.cs`, `HeadScholar.cs` and `Researcher.cs` are active and compile
today; `GaherisArbiter : Arbiter` builds on them unchanged.

## Companion and bot kill credit (task 3)

gaheris needs `GaherisLoot.Credit()` because its mercenaries are not pets. Ours
are: `GameBot : GameNPC` with `BotBrain : IControlledBrain`, and
`BotBrain.GetPlayerOwner()` / `GetLivingOwner()` return `GameBot.Owner`
(`bots/BotBrain.cs:115`). The core already resolves a companion to its owner in
two places:

- `GameNPC.ProcessDeath` (`gameobjects/GameNPC.cs:2906`) rewrites `killer` to
  `petBrain.GetLivingOwner()` for any controlled NPC with an owner, so the
  `Killer` in `DyingEventArgs` is the owning player for a companion kill and
  stays the bot for an ownerless autonomous bot.
- `GameLiving.ProcessDeath` (`gameobjects/GameLiving.cs:2015`) builds
  `playerAttackers`: every attacking player, every controlled attacker's
  `GetPlayerOwner()` (this includes companions), and their group members within
  `MAX_EXPFORKILL_DISTANCE`.

**The gap:** that list is passed only to `DataQuest` listeners
(`GameLiving.cs:2067`). The global notify at the end of the method
(`GameLiving.cs:2092`) is `new DyingEventArgs(killer)`, so `PlayerKillers` is
`null` for every global `Dying` handler. gaheris's `Earners()` would then fall
back to the killer alone: group members and anyone who was not the final killer
get nothing.

**Hook points:**

| Purpose | Hook | Change |
|---|---|---|
| ML experience (needs the dead mob's level) | `GameLivingEvent.Dying` | pass `playerAttackers` in the final notify at `GameLiving.cs:2092`: `new DyingEventArgs(killer, playerAttackers)`. Only `DataQuest` reads `PlayerKillers` today, so nothing else changes |
| Artifact experience, DOL rule (XP actually awarded, NPC and player kills) | end of `GamePlayer.GainExperience` (`gameobjects/GamePlayer.cs:4261`) | re-add `GamePlayerEvent.GainedExperience` and notify it with the `GainedExperienceEventArgs`; then upstream `ArtifactMgr.PlayerGainedExperience` works unchanged. Every NPC kill reward (`AbstractServerRules.AwardPlayerOnNpcKill`, `:1465`), player kill and autonomous-bot kill (`AutonomousBotRealmPointRewards.Award`, `bots/autonomous/AutonomousBotRealmPointRewards.cs:209`) already passes through this method |
| Artifact experience, gaheris rule (dead mob's `ExperienceValue`) | `GameLivingEvent.Dying` | same one-line fix as ML |

Recommendation: the one-line `Dying` fix for ML, and the `GainedExperience`
notify for artifacts. The DOL rule levels artifacts on exactly the kills that
pay the character experience, including RvR kills of players and autonomous
bots; gaheris's rule skips every `GameBot` victim (its filter
`dead.Brain is IControlledBrain` is true for all bots) and all player victims.
Behaviour to know: under either rule a kill landed only by a persistent
companion credits the owner only if the owner is in the reward set; in
`AbstractServerRules.OnNpcKilled` persistent-companion damage is booked to the
companion, not the owner (`:1086`, `:1432`). Not measured in game.

Filter replacements in gaheris code:

- `dead is GameMercenary` becomes
  `dead is GameBot { IsPersistentPlayerCompanion: true } or GameBot { IsTemporaryGroupHelper: true }`.
- `dead.Brain is IControlledBrain` becomes `dead is not GameBot && dead.Brain is IControlledBrain`
  if enemy autonomous bots should count (owner decision; RvR fork).
- `GaherisLoot.Credit(death.Killer)` becomes `death.Killer as GamePlayer`
  (already the owner, see above).
- `MercenaryManager.GetCompany(player)` / `merc.Learn()` in
  `GaherisArbiter.Announce`: drop now; WP2.4 can use
  `PlayerCompanionRoster.GetActiveCompanions(owner)`.

## Detailed mismatch list

### Events and quest API

| Item | Our tree | Fix |
|---|---|---|
| `GameLivingEvent.Dying`, `DyingEventArgs.Killer`, `.PlayerKillers` | exist (`events/gameobjects/GameLivingEvent.cs:98`, `DyingEventArgs.cs:59`); global notify passes no killers list | core fix above |
| `GamePlayerEvent.GainedExperience` | absent; `GainedExperienceEventArgs` exists with every field `ArtifactMgr` reads (`ExpBase`, `ExpCampBonus`, `ExpGroupBonus`, `ExpOutpostBonus`, `XPSource`) | re-add event plus one notify, or keep gaheris's `Dying` path |
| `GamePlayerEvent.GameEntered`, `GameObjectEvent.Interact`/`InteractFailed`/`InteractWith`, `InteractEventArgs`, `InteractWithEventArgs` | exist | none; note `GaherisArbiter.Greet` skips the `DataQuest` notify our `GameObject.Interact` sends (harmless for the Arbiter) |
| `ScriptLoadedEvent` / `ScriptUnloadedEvent` | exist; raised for GameServer.dll too | none |
| `player.QuestList()` extension (ArtifactCompat) | `GamePlayer.QuestList` is a public `ConcurrentDictionary<AbstractQuest, byte>` property; `player.QuestList()` will not compile (non-invocable member) | rename extension to `ActiveQuests()` returning `QuestList.Keys.ToList()`; update Scholar, ArtifactScholar, ArtifactTurnInQuest |
| `GetActiveQuests()` | absent | covered by the rename above |
| `GetFinishedQuests()`, `HasFinishedQuest(Type)`, `IsDoingQuest(Type)`, `AddQuest`, `RemoveEncounterCredit(Type)` | exist, same signatures | see next two rows |
| `AbstractQuest.FinishQuest()` | sets `Step = -2` (finished) and calls `SendQuestRemove(index)` | `RemoveEncounterCredit` only removes credits with `Step == -1`, so `ArtifactQuest.FinishQuest` never consumes the encounter credit. Encounter credits are created outside `QuestList`, so `SendQuestRemove` sends index 0 and can blank the player's first quest in the journal. Give the credit its own finish method (`Step = -1`, `AddFinishedQuest`, save, no packet) |
| `ArtifactTurnInQuest.FinishQuest` | removes from a copied list (`QuestList().Remove`), so the quest stays active | call `QuestList.TryRemove` and `AvailableQuestIndexes.Enqueue` as `AbstractQuest.FinishQuest` does |
| `ArtifactMgr.GetQuestType(string)` | works (`ScriptMgr.Scripts` then GameServer assembly) | our `EncounterID`/`QuestID` values are not type names; replace with data-driven credit (WP3.2) |
| `GameNPC.QuestListToGive`, `AddQuestToGive(Type)`, `HasQuest(Type)` (protected) | exist; `AddQuestToGive` needs a parameterless ctor | fine for subclasses; generic per-artifact instances cannot carry an artifact ID this way, which is why Scholar needs a rewrite |
| `BaseQuest`/`AbstractQuest` ctors `(GamePlayer)`, `(GamePlayer, DbQuest)`, `Step`, `Level`, `SetCustomProperty`/`GetCustomProperty`/`RemoveCustomProperty`, `DeleteFromDatabase`, `m_questPlayer`, `QuestPlayer` | exist | none |

### Player, NPC and item API

| Item | Our tree | Fix |
|---|---|---|
| `GamePlayer.MLGranted`, `MLLevel`, `MLExperience`, `MLLine`, `GetMLExperienceForLevel(int)`, `ML_MAX_LEVEL` (10), `RefreshSpecDependantSkills(bool)` | exist (`GamePlayer.cs:13036`-`13233`, `:3382`); ML XP table is 32,000 per level; `MLLine` index matching in `GamePlayer.cs:9899` is the same loop gaheris mirrors | none |
| `IMasterLevelsSpecialization`, `LiveMasterLevelsSpecialization` | exist (`gameutils/Specializations/LiveMasterLevelsSpecialization.cs:25`, `:32`); snapshot `specialization` rows for the eight lines already name `DOL.GS.LiveMasterLevelsSpecialization` | none; `classxspecialization` has 0 ML rows (WP2.1, gaheris migration 39) |
| `Out.SendMasterLevelWindow(byte)`, `SendUpdatePlayerSkills(bool)`, `SendUpdatePlayer()`, `SendUpdatePoints()`, `SendQuestListUpdate()`, `SendModelChange` | exist in `IPacketLib`; `BotDummyPacketLib` implements them | none |
| `GameBountyMerchant` | exists (`gameobjects/GameMerchant.cs:457`) | none |
| `GainBountyPoints(long,bool,bool,bool)`, `GainRealmPoints(long,bool,bool,bool)`, `IsPraying`, `Guild.BonusType`/`eBonusType.ArtifactXP` | exist | none |
| `CharacterClass.GetSalutation` | absent | ArtifactCompat already supplies `Salutation()` |
| `GameInventoryItem` ctors `(DbItemTemplate)`, `(DbInventoryItem)`; `Delve(List<string>, GamePlayer)`; `DelveWeaponStats`/`DelveArmorStats` protected virtual; `WriteTechnicalInfo(List<string>, GameClient)`; `RepairCost`, `CanUseEvery`, `Experience`, settable `Template`; `DataObject.Clone()` | all exist with matching signatures | none |
| `DbItemTemplate.Get/SetBonusType/Amount(DbArtifactBonus.ID, int)`, `DbArtifactBonus.ID` (`Min`, `MinStat`, `MaxStat`, `Spell`, `Spell1`, `ProcSpell`, `ProcSpell1`, `Max`) | exist | none |
| Item activation as `InventoryArtifact` on login | `GamePlayerInventory` builds items via `GameInventoryItem.Create(item)` from `itemtemplate.ClassType` (`gameutils/GamePlayerInventory.cs:90`); all 197 artifact templates have `ClassType` NULL; gaheris never sets it either | SQL: set `ClassType = 'DOL.GS.InventoryArtifact'` for every `ArtifactXItem.ItemID`; otherwise an artifact reloads as a plain item with every bonus active at level 0 |
| `InventoryArtifact.AddAbilities` on level-up | gaheris left the `ItemBonusChanged` notify out (args internal to core) | in GameServer source, apply the newly unlocked bonus to the wearer (our `GamePlayer.OnItemBonusChanged`, `GamePlayer.cs:9360`, is protected virtual; expose an internal entry) |
| `WorldInventoryItem.CreateUniqueFromTemplate(string)`, `Money.GetMoney(int,int,int,int,int)`, `Money.GetShortString`, `Util.SplitCSV(string,bool)`, `Util.Chance`, `Util.Random` | exist; `SplitCSV` splits on `;`, matching our `AllowedClasses` format | `artifact_scroll` template is absent in our DB (gaheris migration 37 creates it) |
| `InventoryLogging.LogInventoryAction(GameObject/string, GameObject/string, eInventoryActionType, DbItemTemplate, int)` | exists | none |
| `GlobalConstants.*` helpers, `eObjectType.*`, `eProperty.*` ranges (`StatCapBonus_First`, `ToABonus_First`, `Focus_EtherealShriek`, `AllFocusLevels`, `ArcaneSyphon`, ...), `SkillBase.GetPropertyName`, `GetSpellLine`, `GetSpellList(string)`, `GetSpecializationCareer(int)`, `GlobalSpellsLines.Item_Effects` | exist | none |
| `WorldMgr.GetNPCsByName(string, eRealm)`, `VISIBILITY_DISTANCE`, `INTERACT_DISTANCE`, `GetPlayersInRadius(ushort)`, `SayTo` overloads, `TurnTo(GameObject, int)`, `InteractDistance`, `CanTradeAnyItem`, `SetOwnBrain`, `AddBrain`, `StartAttack`, `TakeDamage(GameObject, eDamageType, int, int)`, `HealthPercent`, `RespawnInterval`, `MaxSpeedBase`, `Delete()` | exist | none |
| `GameStaticItemTimed(uint)` with abstract `TryAutoPickUp`/`TryPickUp`, `TryPickUpResult`, `IGameStaticItemOwner` | exist with the same abstract contract gaheris implements | none |
| `ECSGameTimer(GameObject, callback[, int])` | exist | none |
| Language keys (`ArtifactQuest.*`, `ArtifactTurnInQuest.*`, `{Realm}.Arbiter.*`, `Researcher.*`) | present in `GameServer/language/EN` and `DE` | none |

### ECS effects and Master Level holds

| Item | Our tree | Fix |
|---|---|---|
| `SpellHandler.CreateECSEffect(in ECSGameEffectInitParams)`, `ECSGameEffectInitParams`, `ECSGameEffectFactory.Create<T>(in, CreatorDelegate<T>)`, `ECSGameSpellEffect` (`OnStartEffect`, `OnStopEffect`, `End`, `Owner`, `SpellHandler`) | identical (`spells/SpellHandler.cs:279`, `ECS-Effects/ECSGameEffectFactory.cs`) | none |
| `FocusingWindsSpellHandler`, `GroupstealthHandler`, `LoockoutSpellHandler`, `BattlewarderSpellHandler` | exist, public, non-sealed, public `(GameLiving, Spell, SpellLine)` ctors, still hang their breaks on the dead `Moving`/`AttackFinished`/`CastStarting` events | in option B, move gaheris's `CreateECSEffect` override and effect classes into the core handlers instead of subclassing with a second `[SpellHandler]` of the same type |
| `GameStorm.Movable`, `LoockoutOwner`, `MLBrain`, `SpellHandler.FindStaticEffectOnTarget`, `BaseBuffBonusCategory`, `Stealth(bool)`, `IsStealthed`, `GroundTarget.IsValid`, `GroundTargetInView`, `LastAttackTickPvE/PvP`, `castingComponent.SpellHandler`, `IsMoving` | exist | none |
| Dead events (`Moving`, `AttackFinished`, `CastStarting`) | also dead in our tree (only `NecromancerPetBrain` raises two of them) | `MovementWatch` sampling is the right replacement here too |
| Forceful Zephyr, Phaseshift, Grapple | gaheris leaves them; in an RvR fork they target enemy players, so the "no enemy players" excuse does not apply | WP2.3 decision |

### Logging, properties, names

| Item | Our tree | Fix |
|---|---|---|
| `ILog`/`LogManager` shim in `DOL.GS.Scripts` | no collision, but our code uses `DOL.Logging.Logger` (`Info`, `Warn`, `Error(string, Exception)`, `ErrorFormat`, `Is*Enabled` all exist) | delete `ArtifactLog.cs`; declare `Logging.LoggerManager.Create(...)` in each file |
| `[ServerProperty(category, key, description, default)]` | same attribute and ctor; properties `artifact_xp_rate` (350) and `guild_buff_artifact_xp` (5) already exist in `ServerProperties.cs:1904`, `:2343` | none; new `ml_xp_per_level` and `ml_holds_log` can stay on their classes or move to `ServerProperties.cs` |
| `GameMercenary`, `MercenaryManager`, `GaherisLoot` | absent | replacements listed under the companion section |

### Database (SQLite)

| Table / column | DbXxx class | Snapshot contents | Notes |
|---|---|---|---|
| `Artifact` | `CoreDatabase/Tables/DbArtifact.cs` | 57 rows | `ArtifactID` numeric (`1001`...), `EncounterID`=`QuestID`=`Credit`=`Alvarus_Leggings`, `ScholarID`=`1001`, `Zone`=`0`, `Scroll1..23` placeholders, messages `1001`. gaheris migration 37 fixes the scroll titles only |
| `ArtifactBonus` | `DbArtifactBonus.cs` | 516 rows | fits |
| `ArtifactXItem` | `DbArtifactXItem.cs` | 200 rows, 197 resolve | `Version` is `0`..`15`, not DOL's `dmg;wep;stat` keys; `AllowedClasses` on all 197 templates lists every class |
| `CharacterXMasterLevel` | `DbCharacterXMasterLevel.cs` | 0 rows | only used by ML step tracking; gaheris does not write it |
| `DOLCharacters.MLGranted`, `MLLevel`, `MLExperience`, `ML` | `DbCoreCharacter` | columns present | fits |
| `Quest` | `DbQuest` | 6 rows | encounter credits and artifact quests are stored here |
| `ItemTemplate` credit items | | 82 items named `... Credit`, ids `<artifact>_credit` | `ArtifactCreditMerchant` compares `artifact.Credit` to `item.Name`: 3 of 57 match. Compare `item.Id_nb` to `artifact.Credit.ToLower() + "_credit"` instead |
| `MerchantItem` | | `artifact_credits` list with 62 items; `Master Level Credits` list and the 11 `master_level_*_bptoken*` templates exist | fits `MasterLevelsMerchant` |
| `Mob.ClassType` names used by the gaheris SQL | | none placed yet | ours exist for `DOL.GS.Arbiter`, `Researcher`, `GameBountyMerchant`, `GameAtlanteanGlassMerchant`, `Recharger`, `Enchanter`, `Blacksmith`, `MidgardTeleporter`, `GameBoatStableMaster`, `GameHastener`, `Trainer.WizardTrainer`; `DOL.GS.Scholar`, `ArtifactScholar`, `ArtifactCreditMerchant`, `Scripts.GaherisArbiter`, `Scripts.MasterLevelsMerchant` come with the port; **`DOL.GS.Scripts.Recharger` (13 rows in gaheris 13) does not exist**, ours is `DOL.GS.Recharger` (WP0.3) |
| `GameTaxiBoat` (20 rows in gaheris 13) | | | ours derives from `GameMovingObject`, not `GameNPC`; check how WP0.3 loads these rows |

## What gaheris does not supply (artifact path end to end)

Needed before a player can earn an artifact, none of it in gaheris:

1. `ArtifactMgr.Init()` call at startup (a `[ScriptLoadedEvent]` or a
   `GameServer.cs` component). Without it the first scholar interaction
   throws on a null dictionary.
2. Scroll combining: nothing calls `ArtifactMgr.CombineScrolls`. DOL did this
   when a scroll is dropped onto another in the backpack. Our hook point:
   `packets/Client/168/PlayerMoveItemRequestHandler.cs` or the backpack move in
   `GamePlayerInventory`. `eSpellType.CombineScrolls` exists but has no
   handler.
3. A data-driven artifact quest: one quest class that reads the artifact ID
   from a custom property, a scholar that lists `ArtifactMgr.GetArtifacts()`
   with readable names (the first `ArtifactXItem` item name or the credit
   item name) instead of `[1001]`, and scholar assignment per realm instead of
   `ScholarID`.
4. Encounter credit keyed by artifact ID (custom property on an
   `ArtifactEncounter` row), with its own "has credit" and "consume credit"
   helpers, because `HasFinishedQuest(Type)` cannot tell artifacts apart once
   all share one type.
5. A link from the encounter mobs to that credit (for example an artifact ID
   in a mob column the four `Gaheris*` classes read, granting credit to
   `PlayerKillers` on death).
6. The `ClassType` SQL update for artifact templates.

## Recommended porting order

| Step | Files | Effort | Depends on |
|---|---|---|---|
| 1. Core hooks | `GameLiving.ProcessDeath` (pass `playerAttackers`), `GamePlayer.GainExperience` (raise `GainedExperience`), `GamePlayerEvent` field | 0.25 session | none |
| 2. Master Level progression | `MasterLevels.cs` (drop mercenaries), `MasterLevelsMerchant.cs` | 0.5 session | step 1, WP2.1 data (`classxspecialization` ML rows, ML spells) |
| 3. ML holds | `MovementWatch.cs` into core; the four effect classes merged into `Stormlord.cs`, `Spymaster.cs`, `Convoker.cs` handlers | 0.5 session | none |
| 4. Artifact core | `ArtifactMgr.cs` (+ `Init` caller, credit matching), `InventoryArtifact.cs` (+ stat refresh), `ArtifactCompat.cs` (rename), `ArtifactEncounter.cs`; delete `ArtifactLog.cs`; artifact XP via step 1; `ClassType` SQL | 1 session | step 1 |
| 5. Earning artifacts | rewrite `Scholar.cs` + `ArtifactQuest.cs` data-driven, encounter credit helpers, scroll-combine hook, `ArtifactCreditMerchant.cs` | 1.5-2 sessions | step 4, WP1.1 scholars placed, gaheris migrations 36/37 |
| 6. Encounters | `Encounters.cs` four mob classes plus credit grant; swap `glowing_dreaded_seal` for an item we hold; decide on `GaherisRewardMob` (flat RP payout to everyone within 3000 units clashes with Old Frontiers RP) | 0.5 session | step 5, WP0.1 artifact-to-mob mapping |
| 7. Turn-in quest (optional) | `ArtifactTurnInQuest.cs`, `ArtifactScholar.cs` | 1 session, or drop | step 5; needs readable version choices from the numeric `Version` rows |

Total: about 5 to 6 sessions for ML plus artifacts end to end, excluding data
import (WP0.3/WP1.x) and the real-client check.

## Not checked

- No compile run; signatures were matched by reading and grep only.
- Artifact and ML XP pacing on our rates (`artifact_xp_rate` 350, 50 M XP per
  artifact level, `ml_xp_per_level` 10, 32,000 per ML) is unmeasured.
- Whether a kill landed only by a persistent companion puts the owner in the
  XP reward set (and so in the artifact XP path) was read from code, not
  tested.
- `GameTaxiBoat` and other non-`GameNPC` class types in gaheris migration 13.
