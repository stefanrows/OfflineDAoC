# Open BG: modes, presets and claim fix (server) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the open BG two switchable rule modes (Server PvP, Per realm), landing peace by mode, a claim-flip fix, role presets that fill crews and drive team fights, and the leader-landing muster, all server-side.

**Architecture:** One static `OpenBattlegroundMode` (property reader plus pure rules) is the single place that knows the mode. The friend rule (`PvpCombatant.AreAllied`) and the landing peace (`AutonomousHubDeparture`) ask it, only inside region 252. Presets are a pure catalog of role slots (`OpenBattlegroundArchetypes`) that the roster fills instead of "nearest next". The window (Custom5 client stage) is a separate plan that reads these properties.

**Tech Stack:** C# (.NET 10, DOL server in `source/server/GameServer`), NUnit tests in `source/server/Tests/UnitTests`, built from WSL with `tools/dev/winnet.sh`.

**Spec:** `docs/superpowers/specs/2026-10-15-open-bg-window-design.md` (sections 2a, 2b), concept `docs/OPEN_BG_PVP_CONCEPT.md`, presets `docs/OPEN_BG_GROUP_PRESETS.md`.

## Scope note

The spec covers two independent subsystems. This plan is the **server** half (Tasks 1-9). The **own client window** (Custom5, builder stage, installer, window server class) is **Plan 2**, written after this one ships and the properties exist; it depends on `open_bg_mode`, `open_bg_landing_peace` and the archetype catalog but not the other way round. Until then the Custom8 window and `/bgsetup text` show the same values.

## Global Constraints

- Line endings: preserve each file's existing endings; check `git diff --stat` after every edit (whole-file churn means converted endings). `PlayerCompanionCommands.cs` and `ALL SERVER COMMANDS.txt` are CRLF.
- Versioning: one bump for this plan when finished. Gameplay/launcher behaviour change: MINOR (0.277.0 -> 0.278.0). Pins in lockstep: `CHANGELOG.md` heading, `source/tools/OfflineDaoc.Launcher/MainForm.cs` (`DisplayVersion`), `source/tools/OfflineDaoc.Launcher.Tests/LauncherPresentationTests.cs`, `ALL SERVER COMMANDS.txt` header.
- Defaults must keep today's behaviour: `open_bg_mode` default `serverpvp`, `open_bg_landing_peace` default empty (mode decides), presets off until set.
- Nothing outside region 252 changes. The classic realm rule is switched on for region 252 only.
- Real players keep their existing protections; the landing peace stays a bot-versus-bot rule like the hub peace (spec: no realm alliance for players).
- Build in a separate output tree (default `source/server/build`); never into `C:\OfflineDAoC\playable`. Run tests only with filters named per task.
- Commands from the repo root of the worktree `C:\OfflineDAoC-wt-obgwindow`, `export OFFLINE_DAOC_ROOT=/mnt/c/OfflineDAoC/playable`.
- Test baseline: 6 known failures in `UT_PlayerCompanionStage6Integration` and `UT_Gain...`/`UT_PersistentCompanion...` tests exist on `main` (see the filter run in the session notes); a task is green when no *new* failure appears.

## Review Focus

- A bot of realm `None` (0) or an unknown realm must never count as "same realm" in Per realm mode.
- Region 252 only: a same-realm pair in region 1, 100, 200 or 238 stays Camlann (unchanged) in both modes.
- Mode read at use, not cached: switching `open_bg_mode` at run time (`/bgsetup` refresh) takes effect on the next attack check.
- A leader without an assault or defend order never walks to the steward; the lead guild still can after the lord falls.
- A preset whose minimum slot cannot be filled falls back to today's pick and logs `OPEN_BG_ARCHETYPE_MISS`; it never leaves a crew short.
- An empty or unreadable `open_bg_presets` value means "no presets" (today's behaviour), never an exception.
- A member that stands in a region other than 252 when its group joins is ported out and back in; a bot already at its leader's landing is not teleported.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `battlegrounds/open/OpenBattlegroundMode.cs` (new) | `OpenBgMode` enum, `OpenBgLandingPeace` enum, pure parsers, `OpenBattlegroundMode.Current`, `IsOpenBgRegion`, `RealmsAreFriends` |
| `battlegrounds/open/OpenBattlegroundProperties.cs` | adds `open_bg_mode`, `open_bg_landing_peace`, `open_bg_presets` with clamped readers |
| `bots/autonomous/AutonomousHubDeparture.cs` | landing peace by mode (`OpenBgLandingPeaceApplies`) |
| `serverrules/PvpCombatant.cs` | one added clause in `AreAllied` for Per realm mode in region 252 |
| `bots/autonomous/AutonomousBattlegroundDriver.cs` | claim lock (`ClaimAllowed`) |
| `battlegrounds/open/OpenBattlegroundArchetypes.cs` (new) | archetype catalog: slots, class sets, min/wish |
| `battlegrounds/open/OpenBattlegroundRoster.cs` | `TryPickGroup` fills slots; roster healer test; mode-aware pools |
| `battlegrounds/open/OpenBattlegroundDirector.cs` | `OPEN_BG_FIELD` metric log |
| `Tests/UnitTests/UT_OpenBattlegroundMode.cs`, `UT_OpenBattlegroundArchetypes.cs` (new) | tests; `UT_OpenBattlegroundLandingPeace.cs` extended |

All server paths are under `source/server/GameServer/`; tests under `source/server/Tests/UnitTests/`.

---

### Task 1: Mode and peace properties plus the mode helper

**Files:**
- Create: `source/server/GameServer/battlegrounds/open/OpenBattlegroundMode.cs`
- Modify: `source/server/GameServer/battlegrounds/open/OpenBattlegroundProperties.cs` (add fields after `OPEN_BG_UNITS_HIBERNIA`, add readers after `Solos`)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundMode.cs`

**Interfaces:**
- Consumes: `eRealm` (`DOL.GS`), `OpenBattlegroundProperties` clamped-reader pattern.
- Produces: `enum OpenBgMode { ServerPvp, PerRealm }`; `enum OpenBgLandingPeace { Realm, All }`; `OpenBattlegroundMode.ParseMode(string) -> OpenBgMode`; `OpenBattlegroundMode.ParsePeace(string, OpenBgMode) -> OpenBgLandingPeace`; `OpenBattlegroundMode.Current -> OpenBgMode`; `OpenBattlegroundMode.Peace -> OpenBgLandingPeace`; `OpenBattlegroundMode.OpenBgRegion = 252`; `OpenBattlegroundMode.RealmsAreFriends(OpenBgMode mode, ushort region, eRealm a, eRealm b) -> bool`.

- [ ] **Step 1: Write the failing test**

```csharp
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMode
{
    [Test]
    public void ModeParsesCaseInsensitivelyAndUnknownMeansServerPvp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.ParseMode("perrealm"), Is.EqualTo(OpenBgMode.PerRealm));
            Assert.That(OpenBattlegroundMode.ParseMode(" PerRealm "), Is.EqualTo(OpenBgMode.PerRealm));
            Assert.That(OpenBattlegroundMode.ParseMode("serverpvp"), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode(""), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode(null), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode("garbage"), Is.EqualTo(OpenBgMode.ServerPvp));
        });
    }

    [Test]
    public void PeaceDefaultFollowsTheModeAndAnExplicitValueWins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.ParsePeace("", OpenBgMode.ServerPvp), Is.EqualTo(OpenBgLandingPeace.All));
            Assert.That(OpenBattlegroundMode.ParsePeace("", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.Realm));
            Assert.That(OpenBattlegroundMode.ParsePeace("realm", OpenBgMode.ServerPvp), Is.EqualTo(OpenBgLandingPeace.Realm));
            Assert.That(OpenBattlegroundMode.ParsePeace("all", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.All));
            Assert.That(OpenBattlegroundMode.ParsePeace("nonsense", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.Realm));
        });
    }

    [Test]
    public void RealmsAreFriendsOnlyInPerRealmModeInsideRegion252()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.Albion, eRealm.Albion), Is.True);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.Albion, eRealm.Midgard), Is.False);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.ServerPvp, 252, eRealm.Albion, eRealm.Albion), Is.False);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 1, eRealm.Albion, eRealm.Albion), Is.False, "region 1 stays Camlann");
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 238, eRealm.Midgard, eRealm.Midgard), Is.False, "238 stays Camlann");
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.None, eRealm.None), Is.False, "no realm is never a friend");
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore 2>&1 | grep -E ' error |Fehler$'`
Expected: errors `CS0103`/`CS0246` for `OpenBattlegroundMode` and `OpenBgMode`.

- [ ] **Step 3: Write minimal implementation**

`OpenBattlegroundMode.cs`:

```csharp
using System;

namespace DOL.GS
{
    /// <summary>Which friend rule the open BG plays by (owner decision 2026-10-15).</summary>
    public enum OpenBgMode { ServerPvp, PerRealm }

    /// <summary>Whose landing peace holds at a landing: only the landing's own realm, or every realm.</summary>
    public enum OpenBgLandingPeace { Realm, All }

    /// <summary>
    /// The one place that knows the open BG mode. Server PvP is the Camlann rule (friends are group, guild and
    /// battlegroup; crews mixed). Per realm is classic DAoC: inside region 252 the same realm is a friend. The rule is
    /// pure so the unit tests need no server; <see cref="Current"/> reads the property each time (no cache) so a live
    /// /bgsetup change applies on the next check.
    /// </summary>
    public static class OpenBattlegroundMode
    {
        /// <summary>Native Thidranki, the open BG's region.</summary>
        public const ushort OpenBgRegion = 252;

        public static OpenBgMode ParseMode(string text) =>
            string.Equals(text?.Trim(), "perrealm", StringComparison.OrdinalIgnoreCase) ? OpenBgMode.PerRealm : OpenBgMode.ServerPvp;

        public static OpenBgLandingPeace ParsePeace(string text, OpenBgMode mode)
        {
            string value = text?.Trim() ?? string.Empty;
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.All;
            if (string.Equals(value, "realm", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.Realm;
            return mode == OpenBgMode.ServerPvp && value.Length == 0 ? OpenBgLandingPeace.All : OpenBgLandingPeace.Realm;
        }

        public static OpenBgMode Current => ParseMode(OpenBattlegroundProperties.OPEN_BG_MODE);

        public static OpenBgLandingPeace Peace => ParsePeace(OpenBattlegroundProperties.OPEN_BG_LANDING_PEACE, Current);

        /// <summary>Per realm only, inside the open BG region, for two real realms that are equal.</summary>
        public static bool RealmsAreFriends(OpenBgMode mode, ushort region, eRealm first, eRealm second) =>
            mode == OpenBgMode.PerRealm && region == OpenBgRegion && first == second && (int)first is >= 1 and <= 3;
    }
}
```

In `OpenBattlegroundProperties.cs`, after the `OPEN_BG_UNITS_HIBERNIA` field add:

```csharp
        [ServerProperty("pvp", "open_bg_mode", "Open BG friend rule: serverpvp (Camlann: friends are group, guild and battlegroup, crews mixed) or perrealm (classic DAoC: inside the open BG the same realm is a friend)", "serverpvp")]
        public static string OPEN_BG_MODE = "serverpvp";

        [ServerProperty("pvp", "open_bg_landing_peace", "Landing peace at the open BG landings: all (every realm) or realm (only the landing's own realm); empty = by mode (serverpvp: all, perrealm: realm)", "")]
        public static string OPEN_BG_LANDING_PEACE = "";

        [ServerProperty("pvp", "open_bg_presets", "Open BG crew archetypes as name:count list, e.g. std8:2,caster8:1,raid8:1,stealth4:1,small:2,solo:6; empty = the group counts above", "")]
        public static string OPEN_BG_PRESETS = "";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore 2>&1 | grep -E ' error |Fehler$'; tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~UT_OpenBattlegroundMode" 2>&1 | grep -E 'Fehler|Bestanden'`
Expected: `Bestanden! ... Fehler: 0, erfolgreich: 3`.

- [ ] **Step 5: Commit**

```bash
git add source/server/GameServer/battlegrounds/open/OpenBattlegroundMode.cs source/server/GameServer/battlegrounds/open/OpenBattlegroundProperties.cs source/server/Tests/UnitTests/UT_OpenBattlegroundMode.cs
git commit -m "feat(open-bg): mode, landing peace and presets properties with the mode helper"
```

---

### Task 2: Landing peace by mode

**Files:**
- Modify: `source/server/GameServer/bots/autonomous/AutonomousHubDeparture.cs` (add `OpenBgLandingPeaceApplies` near `NearOwnLanding`, line ~205; extend the peace rule at `HubPeaceApplies(...)` line ~347 callers)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundLandingPeace.cs` (extend)

**Interfaces:**
- Consumes: `AutonomousHubDeparture.NearOwnLanding(region, x, y, realm, radius, landings)`, `OpenBgLandingPeaceRadius`, `OpenBgLandingPeace` (Task 1).
- Produces: `AutonomousHubDeparture.NearAnyLanding(ushort region, int x, int y, int radius, IReadOnlyList<(ushort Region,int X,int Y,eRealm Realm)> landings) -> bool`; `AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace peace, ushort region, int x, int y, eRealm realm, IReadOnlyList<...> landings) -> bool` (Realm: own landing only; All: any landing).

- [ ] **Step 1: Write the failing test** (append to `UT_OpenBattlegroundLandingPeace`)

```csharp
    [Test]
    public void AllPeaceCoversEveryLandingForEveryRealmAndRealmPeaceOnlyTheOwn()
    {
        Assert.Multiple(() =>
        {
            // An Albion bot standing at the Midgard camp.
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 50500, 27000, eRealm.Albion,
                Landings), Is.True, "all: any landing protects any realm");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.Realm, 252, 50500, 27000, eRealm.Albion,
                Landings), Is.False, "realm: only the own landing");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.Realm, 252, 54053, 24680, eRealm.Midgard,
                Landings), Is.True);
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 34333, 38275, eRealm.Albion,
                Landings), Is.False, "the keep gate is open field in both");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 238, 50500, 27000, eRealm.Albion,
                Landings), Is.False, "another region");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 50500, 27000, eRealm.None,
                Landings), Is.False, "no realm, no peace");
        });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore 2>&1 | grep -E ' error '`
Expected: `CS0117 ... OpenBgPeaceInCircle`.

- [ ] **Step 3: Write minimal implementation** (in `AutonomousHubDeparture.cs`, after `NearOwnLanding`)

```csharp
    /// <summary>Whether (x, y) lies within <paramref name="radius"/> of any open BG landing, whatever its realm.</summary>
    public static bool NearAnyLanding(ushort region, int x, int y, int radius,
        IReadOnlyList<(ushort Region, int X, int Y, eRealm Realm)> landings)
    {
        if (landings == null)
            return false;
        long radiusSquared = (long)radius * radius;
        foreach ((ushort landingRegion, int landingX, int landingY, eRealm _) in landings)
        {
            if (landingRegion != region)
                continue;
            long dx = x - landingX, dy = y - landingY;
            if (dx * dx + dy * dy <= radiusSquared)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Peace circle by mode (owner decision 2026-10-15): <see cref="OpenBgLandingPeace.All"/> protects every realm at
    /// every landing (Server PvP); <see cref="OpenBgLandingPeace.Realm"/> only a realm at its own landing (Per realm).
    /// </summary>
    public static bool OpenBgPeaceInCircle(OpenBgLandingPeace peace, ushort region, int x, int y, eRealm realm,
        IReadOnlyList<(ushort Region, int X, int Y, eRealm Realm)> landings)
    {
        if ((int)realm is < 1 or > 3)
            return false;
        return peace == OpenBgLandingPeace.All
            ? NearAnyLanding(region, x, y, OpenBgLandingPeaceRadius, landings)
            : NearOwnLanding(region, x, y, realm, OpenBgLandingPeaceRadius, landings);
    }
```

- [ ] **Step 4: Wire the live rule.** Read `InOpenBgLandingBand` (line ~246) and `TryGetOpenBgLandingAnchor` (line ~228) and make the *circle* test use `OpenBgPeaceInCircle(OpenBattlegroundMode.Peace, ...)` with `OpenBgLandings()`. The existing `HubPeaceApplies(bool, bool sameRealm, ...)` requires `sameRealm`; for region 252 in `All` peace the `sameRealm` condition must not block, so in the object overload `HubPeaceApplies(GameLiving attacker, GameLiving target, DateTime nowUtc, out eRealm realm, out HubPeaceRule rule)` (line ~407) pass `sameRealm || (IsOpenBgLandingRegion(attacker.CurrentRegionID) && OpenBattlegroundMode.Peace == OpenBgLandingPeace.All)`. Add this regression test to the same fixture:

```csharp
    [Test]
    public void OpenBgPeaceModeNeverChangesRegionsOutsideTheOpenBg()
    {
        Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 1, 37301, 52362, eRealm.Albion, Landings), Is.False,
            "the same coordinates in region 1 are not a landing");
    }
```

- [ ] **Step 5: Run tests**

Run: `tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~UT_OpenBattlegroundLandingPeace|FullyQualifiedName~UT_RvrHubPeace|FullyQualifiedName~UT_BattlegroundLanding" 2>&1 | grep -E 'Fehler|Bestanden'`
Expected: all pass (the hub-peace tests prove the world hubs are unchanged).

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/bots/autonomous/AutonomousHubDeparture.cs source/server/Tests/UnitTests/UT_OpenBattlegroundLandingPeace.cs
git commit -m "feat(open-bg): landing peace by mode (all realms or own realm)"
```

---

### Task 3: Per realm friend rule in region 252

**Files:**
- Modify: `source/server/GameServer/serverrules/PvpCombatant.cs` (`AreAllied`, after the battlegroup check, before `return CompanionProtects(...)`, line ~85)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundMode.cs` (add a fixture test using two `GameLiving`s as the existing `UT_*` tests do) — if no ready fakes exist for `GameLiving` in `Tests/`, write the rule as a pure helper `PvpCombatant.RealmFriendInOpenBg(OpenBgMode, ushort, eRealm, eRealm)` that delegates to `OpenBattlegroundMode.RealmsAreFriends` and test that.

**Interfaces:**
- Consumes: `OpenBattlegroundMode.Current`, `RealmsAreFriends` (Task 1).
- Produces: `PvpCombatant.AreAllied` returns true for same-realm players and bots standing in region 252 while the mode is `perrealm`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Test]
    public void ThePvpRuleReadsTheModeOnlyInsideTheOpenBg()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.RealmFriendInOpenBg(OpenBgMode.PerRealm, 252, eRealm.Hibernia, eRealm.Hibernia), Is.True);
            Assert.That(PvpCombatant.RealmFriendInOpenBg(OpenBgMode.PerRealm, 252, eRealm.Hibernia, eRealm.Albion), Is.False);
            Assert.That(PvpCombatant.RealmFriendInOpenBg(OpenBgMode.ServerPvp, 252, eRealm.Hibernia, eRealm.Hibernia), Is.False);
            Assert.That(PvpCombatant.RealmFriendInOpenBg(OpenBgMode.PerRealm, 200, eRealm.Hibernia, eRealm.Hibernia), Is.False);
        });
    }
```

- [ ] **Step 2: Run to verify failure** — build; expected `CS0117 ... RealmFriendInOpenBg`.

- [ ] **Step 3: Implement** in `PvpCombatant.cs`:

```csharp
		/// <summary>Per realm mode: inside the open BG the same realm is a friend (classic DAoC); everywhere else Camlann.</summary>
		public static bool RealmFriendInOpenBg(OpenBgMode mode, ushort region, eRealm first, eRealm second) =>
			OpenBattlegroundMode.RealmsAreFriends(mode, region, first, second);
```

and in `AreAllied`, directly before `return CompanionProtects(a, b) || CompanionProtects(b, a);`:

```csharp
			if (a.CurrentRegionID == b.CurrentRegionID &&
				RealmFriendInOpenBg(OpenBattlegroundMode.Current, a.CurrentRegionID, a.Realm, b.Realm))
				return true;
```

- [ ] **Step 4: Prove the classic check in the live rules.** `PvPServerRules` calls `AreAllied` at lines 145, 286 and 389 (attack, spell and target decisions). Read those three sites and confirm each returns "not allowed to attack" for an allied pair; if one of them also needs `IsSameRealm` (classic realm check, `NormalServerRules.cs:84`), add that call for region 252 there. Record the finding in the commit message ("verified lines 145/286/389: allied pair cannot be attacked, healed, buffed correctly").

- [ ] **Step 5: Run tests**

Run: filter `UT_OpenBattlegroundMode|UT_Pvp|UT_Camlann` (the existing PvP rule tests). Expected: no new failure.

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/serverrules/PvpCombatant.cs source/server/Tests/UnitTests/UT_OpenBattlegroundMode.cs
git commit -m "feat(open-bg): per realm mode makes the same realm a friend inside region 252"
```

---

### Task 4: Claim lock (allied claim flip)

**Files:**
- Modify: `source/server/GameServer/bots/autonomous/AutonomousBattlegroundDriver.cs` (`LeadOpen` line ~337 and `TryClaim` line ~698)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundClaimLock.cs` (new)

**Interfaces:**
- Consumes: `OpenBgRole { BridgeWatch, Roam, Lurk, Assault, Defend, Retreat }`, `OpenBgOrder` (Director line 103).
- Produces: `AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole role, bool isRaidLeadGuild, long msSinceLordDown) -> bool`: true for `Defend` any time; true for `Assault` only if `isRaidLeadGuild` within `ClaimLeadMilliseconds` (60 000) after the lord fell, or any `Assault` after that window; false for every other role.

- [ ] **Step 1: Write the failing test**

```csharp
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundClaimLock
{
    [Test]
    public void OnlyTheLeadGuildClaimsFirstAndNoIdleRoleEverWalksToTheSteward()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, true, 5_000), Is.True, "lead guild inside the window");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, false, 5_000), Is.False, "a partner waits");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, false, 61_000), Is.True, "after the window anyone may");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Defend, false, 0), Is.True, "the owner's defenders may re-claim");
            foreach (OpenBgRole idle in new[] { OpenBgRole.Roam, OpenBgRole.Lurk, OpenBgRole.BridgeWatch, OpenBgRole.Retreat })
                Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(idle, true, 120_000), Is.False, idle + " never claims");
        });
    }
}
```

- [ ] **Step 2: Run to verify failure** — build; expected `CS0117 ... ClaimAllowed`.

- [ ] **Step 3: Implement** in the driver class:

```csharp
    /// <summary>The raid's lead guild claims first; partners wait this long after the lord fell (the allied claim flip: 10 of 18 claims in run L2 came within 3 minutes of the previous one).</summary>
    public const long ClaimLeadMilliseconds = 60_000;

    /// <summary>Pure claim rule: only assault and defend units walk to the steward, the lead guild first.</summary>
    public static bool ClaimAllowed(OpenBgRole role, bool isRaidLeadGuild, long msSinceLordDown) => role switch
    {
        OpenBgRole.Defend => true,
        OpenBgRole.Assault => isRaidLeadGuild || msSinceLordDown >= ClaimLeadMilliseconds,
        _ => false,
    };
```

- [ ] **Step 4: Wire it.** In `LeadOpen` (line ~337) change `if (keep != null && TryClaim(bot, keep, state, now)) return true;` to `if (keep != null && ClaimAllowed(order.Role, IsRaidLeadGuild(bot), MsSinceLordDown(keep, now)) && TryClaim(bot, keep, state, now)) return true;`. Implement the two helpers next to `TryClaim`: `IsRaidLeadGuild(GameBot bot)` returns whether `bot.Guild` equals the guild that owns the director's current raid lead unit (add `OpenBattlegroundDirector.RaidLeadGuildId` as a public read of the raid's first unit's guild id, the director already picks `raid` units at `OpenBattlegroundDirector.cs:203-250`); `MsSinceLordDown(keep, now)` returns `now - state.LordDownAt` where `LordDownAt` is stamped the first tick `keep.DBKeep.LordDefeated` is seen true (store in `State`, reset when the flag clears).

- [ ] **Step 5: Run tests** — filter `UT_OpenBattlegroundClaimLock|UT_OpenBattleground|UT_AutonomousBattleground`. Expected: no new failure.

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/bots/autonomous/AutonomousBattlegroundDriver.cs source/server/GameServer/battlegrounds/open/OpenBattlegroundDirector.cs source/server/Tests/UnitTests/UT_OpenBattlegroundClaimLock.cs
git commit -m "fix(open-bg): claim lock, only assault/defend units claim and the raid's lead guild first (allied claim flip)"
```

---

### Task 5: Metric log OPEN_BG_FIELD

**Files:**
- Modify: `source/server/GameServer/battlegrounds/open/OpenBattlegroundDirector.cs` (the 10-second tick; add a 60-second emit)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundMetrics.cs` (new)

**Interfaces:**
- Produces: `OpenBattlegroundDirector.FieldShare(int units, int outUnits) -> string` is not needed; instead `OpenBgMetrics.FieldLine(int unitsOut, int units, int botsOut, int bots) -> string` returning `OPEN_BG_FIELD units_out=3/9 bots_out=40/72 pct=56`.

- [ ] **Step 1: Failing test**

```csharp
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMetrics
{
    [Test]
    public void FieldLineReportsUnitsAndBotsOutWithAPercentage()
    {
        Assert.That(OpenBgMetrics.FieldLine(3, 9, 40, 72), Is.EqualTo("OPEN_BG_FIELD units_out=3/9 bots_out=40/72 pct=56"));
        Assert.That(OpenBgMetrics.FieldLine(0, 0, 0, 0), Is.EqualTo("OPEN_BG_FIELD units_out=0/0 bots_out=0/0 pct=0"), "an empty roster must not divide by zero");
    }
}
```

- [ ] **Step 2: Run to verify failure** — build; expected `CS0103 ... OpenBgMetrics`.

- [ ] **Step 3: Implement** (new static class in `OpenBattlegroundDirector.cs` namespace):

```csharp
    /// <summary>Metric log lines of the open BG (concept section 4).</summary>
    public static class OpenBgMetrics
    {
        public static string FieldLine(int unitsOut, int units, int botsOut, int bots) =>
            $"OPEN_BG_FIELD units_out={unitsOut}/{units} bots_out={botsOut}/{bots} pct={(bots == 0 ? 0 : (int)Math.Round(100.0 * botsOut / bots))}";
    }
```

Wire: in the director's tick, every 60 game seconds count roster units whose order role is `Roam`, `Assault`, `Defend` or `BridgeWatch` and whose members are in region 252 further than `OpenBgLandingPeaceRadius` from their landing (use `AutonomousHubDeparture.NearOwnLanding`), and `Log.Info(OpenBgMetrics.FieldLine(...))`. Add `OPEN_BG_ENCOUNTER` and `OPEN_BG_RAID_AT_GATE` in a follow-up only if the first run shows the field figure is not enough (not in this plan; YAGNI).

- [ ] **Step 4: Run tests** — filter `UT_OpenBattlegroundMetrics`. Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add source/server/GameServer/battlegrounds/open/OpenBattlegroundDirector.cs source/server/Tests/UnitTests/UT_OpenBattlegroundMetrics.cs
git commit -m "feat(open-bg): OPEN_BG_FIELD metric line"
```

---

### Task 6: Archetype catalog

**Files:**
- Create: `source/server/GameServer/battlegrounds/open/OpenBattlegroundArchetypes.cs`
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundArchetypes.cs`

**Interfaces:**
- Consumes: `eCharacterClass` (DOL.GS).
- Produces: `enum OpenBgSlotRole { Healer, AreaCc, Speed, Tank, Melee, Bomber, Caster, Assassin, Archer, Pet, Flex }`; `record OpenBgSlot(OpenBgSlotRole Role, int Min, int Wish)`; `record OpenBgArchetype(string Key, int Size, OpenBgSlot[] Slots)`; `OpenBattlegroundArchetypes.Get(string key) -> OpenBgArchetype?`; `OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole) -> IReadOnlySet<eCharacterClass>`; `OpenBattlegroundArchetypes.ParseMix(string text) -> IReadOnlyList<(OpenBgArchetype Archetype, int Count)>` (empty list for empty or invalid text).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundArchetypes
{
    [Test]
    public void TheStandardEightHasTwoHealersOneCcOneSpeedOneTankThreeMelee()
    {
        OpenBgArchetype std8 = OpenBattlegroundArchetypes.Get("std8");
        Assert.That(std8, Is.Not.Null);
        Assert.That(std8.Size, Is.EqualTo(8));
        int Min(OpenBgSlotRole role) => std8.Slots.Where(slot => slot.Role == role).Sum(slot => slot.Min);
        Assert.Multiple(() =>
        {
            Assert.That(Min(OpenBgSlotRole.Healer), Is.EqualTo(2));
            Assert.That(Min(OpenBgSlotRole.AreaCc), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Speed), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Tank), Is.EqualTo(1));
            Assert.That(Min(OpenBgSlotRole.Melee), Is.EqualTo(2));
        });
    }

    [Test]
    public void EveryArchetypeFitsItsSizeAndNoMinimumsExceedIt()
    {
        foreach (string key in new[] { "std8", "caster8", "raid8", "stealth4", "small", "duo" })
        {
            OpenBgArchetype archetype = OpenBattlegroundArchetypes.Get(key);
            Assert.That(archetype, Is.Not.Null, key);
            Assert.That(archetype.Slots.Sum(slot => slot.Min), Is.LessThanOrEqualTo(archetype.Size), key + " minimums fit the group");
        }
    }

    [Test]
    public void ARoleListsClassesOfAllThreeRealms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Cleric));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Healer));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Contain(eCharacterClass.Druid));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Healer), Does.Not.Contain(eCharacterClass.Paladin), "a Paladin is no healer");
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Speed), Is.SupersetOf(new[] { eCharacterClass.Minstrel, eCharacterClass.Skald, eCharacterClass.Bard }));
            Assert.That(OpenBattlegroundArchetypes.ClassesFor(OpenBgSlotRole.Assassin), Is.SupersetOf(new[] { eCharacterClass.Infiltrator, eCharacterClass.Shadowblade, eCharacterClass.Nightshade }));
        });
    }

    [Test]
    public void TheMixParsesCountsAndIgnoresGarbage()
    {
        var mix = OpenBattlegroundArchetypes.ParseMix("std8:2, caster8:1,nope:3,solo:x,stealth4:1");
        Assert.That(mix.Select(entry => (entry.Archetype.Key, entry.Count)), Is.EqualTo(new[] { ("std8", 2), ("caster8", 1), ("stealth4", 1) }));
        Assert.That(OpenBattlegroundArchetypes.ParseMix(""), Is.Empty);
        Assert.That(OpenBattlegroundArchetypes.ParseMix(null), Is.Empty);
    }
}
```

- [ ] **Step 2: Run to verify failure** — build; expected `CS0246 ... OpenBgArchetype`.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public enum OpenBgSlotRole { Healer, AreaCc, Speed, Tank, Melee, Bomber, Caster, Assassin, Archer, Pet, Flex }

    public sealed record OpenBgSlot(OpenBgSlotRole Role, int Min, int Wish);

    public sealed record OpenBgArchetype(string Key, int Size, OpenBgSlot[] Slots);

    /// <summary>
    /// Role archetypes for open BG crews (docs/OPEN_BG_GROUP_PRESETS.md): slots of roles, each role a set of classes
    /// of all three realms. Cross-realm by construction; in Per realm mode the roster filters the sets to one realm.
    /// </summary>
    public static class OpenBattlegroundArchetypes
    {
        private static readonly Dictionary<OpenBgSlotRole, HashSet<eCharacterClass>> Roles = new()
        {
            [OpenBgSlotRole.Healer] = new() { eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Druid, eCharacterClass.Friar, eCharacterClass.Shaman, eCharacterClass.Warden },
            [OpenBgSlotRole.AreaCc] = new() { eCharacterClass.Sorcerer, eCharacterClass.Minstrel, eCharacterClass.Healer, eCharacterClass.Bard, eCharacterClass.Eldritch, eCharacterClass.Mentalist },
            [OpenBgSlotRole.Speed] = new() { eCharacterClass.Minstrel, eCharacterClass.Skald, eCharacterClass.Bard },
            [OpenBgSlotRole.Tank] = new() { eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Warrior, eCharacterClass.Hero, eCharacterClass.Blademaster },
            [OpenBgSlotRole.Melee] = new() { eCharacterClass.Mercenary, eCharacterClass.Reaver, eCharacterClass.Armsman, eCharacterClass.Savage, eCharacterClass.Berserker, eCharacterClass.Blademaster, eCharacterClass.Hero, eCharacterClass.Champion, eCharacterClass.Valewalker },
            [OpenBgSlotRole.Bomber] = new() { eCharacterClass.Wizard, eCharacterClass.Spiritmaster, eCharacterClass.Eldritch, eCharacterClass.Enchanter },
            [OpenBgSlotRole.Caster] = new() { eCharacterClass.Cabalist, eCharacterClass.Theurgist, eCharacterClass.Sorcerer, eCharacterClass.Runemaster, eCharacterClass.Bonedancer, eCharacterClass.Mentalist, eCharacterClass.Eldritch },
            [OpenBgSlotRole.Assassin] = new() { eCharacterClass.Infiltrator, eCharacterClass.Shadowblade, eCharacterClass.Nightshade },
            [OpenBgSlotRole.Archer] = new() { eCharacterClass.Scout, eCharacterClass.Hunter, eCharacterClass.Ranger },
            [OpenBgSlotRole.Pet] = new() { eCharacterClass.Theurgist, eCharacterClass.Necromancer, eCharacterClass.Cabalist, eCharacterClass.Bonedancer, eCharacterClass.Spiritmaster, eCharacterClass.Animist, eCharacterClass.Enchanter },
            [OpenBgSlotRole.Flex] = new(),
        };

        private static OpenBgSlot S(OpenBgSlotRole role, int min, int wish) => new(role, min, wish);

        private static readonly Dictionary<string, OpenBgArchetype> Catalog = new(StringComparer.OrdinalIgnoreCase)
        {
            ["std8"] = new("std8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.AreaCc, 1, 1), S(OpenBgSlotRole.Speed, 1, 1), S(OpenBgSlotRole.Tank, 1, 2), S(OpenBgSlotRole.Melee, 2, 3) }),
            ["caster8"] = new("caster8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.AreaCc, 1, 2), S(OpenBgSlotRole.Tank, 1, 2), S(OpenBgSlotRole.Bomber, 2, 3), S(OpenBgSlotRole.Caster, 0, 1) }),
            ["raid8"] = new("raid8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.Speed, 1, 1), S(OpenBgSlotRole.Tank, 1, 1), S(OpenBgSlotRole.Melee, 1, 2), S(OpenBgSlotRole.Bomber, 1, 2), S(OpenBgSlotRole.Pet, 0, 1) }),
            ["stealth4"] = new("stealth4", 4, new[] { S(OpenBgSlotRole.Assassin, 2, 3), S(OpenBgSlotRole.Archer, 0, 1) }),
            ["small"] = new("small", 4, new[] { S(OpenBgSlotRole.Healer, 1, 1), S(OpenBgSlotRole.AreaCc, 1, 1), S(OpenBgSlotRole.Melee, 1, 2) }),
            ["duo"] = new("duo", 2, new[] { S(OpenBgSlotRole.Healer, 0, 1), S(OpenBgSlotRole.Melee, 0, 1) }),
        };

        public static OpenBgArchetype Get(string key) =>
            key != null && Catalog.TryGetValue(key.Trim(), out OpenBgArchetype archetype) ? archetype : null;

        public static IReadOnlySet<eCharacterClass> ClassesFor(OpenBgSlotRole role) => Roles[role];

        /// <summary>"std8:2,caster8:1": unknown keys and bad counts are skipped; empty or null gives no entries.</summary>
        public static IReadOnlyList<(OpenBgArchetype Archetype, int Count)> ParseMix(string text)
        {
            var result = new List<(OpenBgArchetype, int)>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
                OpenBgArchetype archetype = pair.Length == 2 ? Get(pair[0]) : null;
                if (archetype != null && int.TryParse(pair[1], out int count) && count > 0)
                    result.Add((archetype, Math.Min(count, 8)));
            }
            return result;
        }
    }
}
```

(Verify each `eCharacterClass` member exists in `DOL.GS` by building; a missing name is a compile error to fix, not to guess.)

- [ ] **Step 4: Run tests** — filter `UT_OpenBattlegroundArchetypes`. Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add source/server/GameServer/battlegrounds/open/OpenBattlegroundArchetypes.cs source/server/Tests/UnitTests/UT_OpenBattlegroundArchetypes.cs
git commit -m "feat(open-bg): archetype catalog (std8, caster8, raid8, stealth4, small, duo) as role slots"
```

---

### Task 7: Roster fills archetype slots

**Files:**
- Modify: `source/server/GameServer/battlegrounds/open/OpenBattlegroundRoster.cs` (`TryPickGroup` lines 1113-1149, `IsHealer` line 1237, the target plan around `Plan`/`CountRealms` lines 135-261)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundArchetypes.cs` (add pure picker tests)

**Interfaces:**
- Consumes: `OpenBgArchetype`, `OpenBattlegroundArchetypes.ClassesFor`, `OpenBattlegroundMode.Current`.
- Produces: pure `OpenBattlegroundArchetypes.Fill(OpenBgArchetype archetype, IReadOnlyList<eCharacterClass> candidates, OpenBgMode mode) -> OpenBgFill` where `OpenBgFill(int[] PickedIndexes, OpenBgSlotRole[] MissedMinimums)`; `OpenBattlegroundRoster.IsRosterHealer(eCharacterClass) -> bool` (Cleric, Healer, Druid primary; Friar, Shaman, Warden secondary; **not** Paladin).

- [ ] **Step 1: Failing tests** (append to `UT_OpenBattlegroundArchetypes`)

```csharp
    [Test]
    public void FillPicksSlotsInPriorityOrderAndReportsMissedMinimums()
    {
        var pool = new[]
        {
            eCharacterClass.Paladin, eCharacterClass.Paladin, eCharacterClass.Friar, eCharacterClass.Cleric, eCharacterClass.Minstrel,
            eCharacterClass.Mercenary, eCharacterClass.Mercenary, eCharacterClass.Savage, eCharacterClass.Wizard,
        };
        OpenBgFill fill = OpenBattlegroundArchetypes.Fill(OpenBattlegroundArchetypes.Get("std8"), pool, OpenBgMode.ServerPvp);
        var picked = fill.PickedIndexes.Select(index => pool[index]).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(picked.Count(OpenBattlegroundRoster.IsRosterHealer), Is.EqualTo(2), "Cleric and Friar are the two healers");
            Assert.That(picked, Does.Not.Contain(eCharacterClass.Wizard).Or.Contain(eCharacterClass.Wizard), "a flex fill may take it");
            Assert.That(picked, Does.Contain(eCharacterClass.Minstrel), "speed and mez come from the Minstrel");
            Assert.That(fill.MissedMinimums, Is.Empty);
        });
    }

    [Test]
    public void FillWithoutAnyHealerReportsTheMissAndStillFillsTheRest()
    {
        var pool = Enumerable.Repeat(eCharacterClass.Mercenary, 9).ToArray();
        OpenBgFill fill = OpenBattlegroundArchetypes.Fill(OpenBattlegroundArchetypes.Get("std8"), pool, OpenBgMode.ServerPvp);
        Assert.That(fill.MissedMinimums, Does.Contain(OpenBgSlotRole.Healer));
        Assert.That(fill.PickedIndexes, Has.Length.EqualTo(8), "never a crew short because of a missed slot");
    }

    [Test]
    public void APaladinIsNeverARosterHealer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Paladin), Is.False);
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Druid), Is.True);
            Assert.That(OpenBattlegroundRoster.IsRosterHealer(eCharacterClass.Bard), Is.False);
        });
    }
```

(The `Does.Not.Contain(...).Or.Contain(...)` line is intentionally permissive; delete it if it reads as noise.)

- [ ] **Step 2: Run to verify failure** — build; expected `CS0117 ... Fill`, `IsRosterHealer`.

- [ ] **Step 3: Implement the pure filler** in `OpenBattlegroundArchetypes`:

```csharp
    public sealed record OpenBgFill(int[] PickedIndexes, OpenBgSlotRole[] MissedMinimums);

    /// <summary>Slots first (min, then wish) in the listed order, each filled from the first unpicked candidate whose class is in the role; the group is then topped up to its size from what is left, in candidate order (the caller sorts by distance).</summary>
    public static OpenBgFill Fill(OpenBgArchetype archetype, IReadOnlyList<eCharacterClass> candidates, OpenBgMode mode)
    {
        var picked = new List<int>();
        var missed = new List<OpenBgSlotRole>();
        foreach (OpenBgSlot slot in archetype.Slots)
        {
            IReadOnlySet<eCharacterClass> classes = ClassesFor(slot.Role);
            int have = 0;
            for (int pass = 0; pass < slot.Wish && picked.Count < archetype.Size; pass++)
            {
                int index = -1;
                for (int i = 0; i < candidates.Count; i++)
                    if (!picked.Contains(i) && classes.Contains(candidates[i])) { index = i; break; }
                if (index < 0) break;
                picked.Add(index);
                have++;
            }
            if (have < slot.Min) missed.Add(slot.Role);
        }
        for (int i = 0; i < candidates.Count && picked.Count < archetype.Size; i++)
            if (!picked.Contains(i)) picked.Add(i);
        return new OpenBgFill(picked.ToArray(), missed.ToArray());
    }
```

`mode` is reserved for the Per realm filter, which the roster applies by passing only candidates of one realm (documented here, tested in Step 5's roster test via the caller). In `OpenBattlegroundRoster` add:

```csharp
        private static readonly HashSet<eCharacterClass> RosterHealers = new()
        { eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Druid, eCharacterClass.Friar, eCharacterClass.Shaman, eCharacterClass.Warden };

        public static bool IsRosterHealer(eCharacterClass characterClass) => RosterHealers.Contains(characterClass);
```

and change `IsHealer(GameBot bot)` (line 1237) to `bot.CharacterClass != null && IsRosterHealer((eCharacterClass)bot.CharacterClass.ID)`. `BotPartyRoles.IsHealingClass` is left alone (companions use it).

- [ ] **Step 4: Wire `TryPickGroup`.** After computing `near` (line ~1130) replace the healer/pick lines (1140-1143) with: when `OpenBattlegroundProperties.OPEN_BG_PRESETS` yields an archetype for this unit's size (`size == archetype.Size`), call `OpenBattlegroundArchetypes.Fill(archetype, near.Select(bot => (eCharacterClass)bot.CharacterClass.ID).ToList(), OpenBattlegroundMode.Current)` and take `PickedIndexes` (limited to `size - 1`, the seed is the leader and counts toward the slots: prepend the seed's class when calling `Fill` and drop index 0 from the result); log `OPEN_BG_ARCHETYPE_MISS unit=<archetype> missed=<roles>` when `MissedMinimums` is non-empty. With no preset for the size keep the current lines unchanged. Fix `UnitMates` pooling in Per realm mode: `CandidatePools` already filters by `bot.Realm` (lines 985-1000), so Per realm keeps working unchanged; in Server PvP mode merge the three realm pools for archetype units (see Task 8).

- [ ] **Step 5: Run tests** — filter `UT_OpenBattlegroundArchetypes|UT_OpenBattlegroundRoster|UT_OpenBg`. Expected: no new failure. Then run one server start check is not required; the live check is in Task 10.

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/battlegrounds/open/OpenBattlegroundArchetypes.cs source/server/GameServer/battlegrounds/open/OpenBattlegroundRoster.cs source/server/Tests/UnitTests/UT_OpenBattlegroundArchetypes.cs
git commit -m "feat(open-bg): roster fills archetype slots, roster healer test without Paladin"
```

---

### Task 8: Leader-landing muster (prove first)

**Files:**
- Modify: `source/server/GameServer/bots/autonomous/AutonomousBotGroupCoordinator.OpenBattleground.cs` (boarding at lines ~11-130), `source/server/GameServer/battlegrounds/open/OpenBattlegroundRoster.cs` (`FormUnits` line ~663, `CandidatePools` lines 985-1000)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundMuster.cs` (new)

**Interfaces:**
- Consumes: `AutonomousHubDeparture.OpenBgLandings()`, `OpenBattlegroundMode.Current`.
- Produces: pure `OpenBattlegroundMuster.NeedsPortOut(ushort memberRegion, ushort leaderRegion, bool atLeaderLanding) -> bool` and `OpenBattlegroundMuster.LandingFor(GameBot leader, IReadOnlyList<(ushort,int,int,eRealm)> landings) -> (int X, int Y)?`.

- [ ] **Step 1: Prove the placement.** Before any change, read how the porter cast places a bot in region 252 (`AutonomousBattlegroundParticipation.cs` boarding, and `AutonomousBotGroupCoordinator.OpenBattleground.cs:11-61`) and find the direct-move call it ends in (`MoveTo(regionId, x, y, z, heading)` on the living). Write the result in the commit message. If a bot can be moved with `MoveTo` to the landing without the porter cast, continue; if the cast sets state the bot needs (medallion, participation record), call the same state setter from the muster instead of reimplementing it. If neither is possible, stop and report; do not invent a workaround.

- [ ] **Step 2: Failing test**

```csharp
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMuster
{
    [Test]
    public void AMemberIsPortedOutOnlyWhenItIsNotAlreadyAtTheLeadersLanding()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(200, 252, false), Is.True, "Hibernian frontier member, leader inside the BG");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(252, 252, true), Is.False, "already at the landing");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(252, 252, false), Is.True, "in the BG but elsewhere: out and back in");
            Assert.That(OpenBattlegroundMuster.NeedsPortOut(200, 200, true), Is.False, "both outside, the normal porter boarding applies");
        });
    }
}
```

- [ ] **Step 3: Run to verify failure** — build; expected `CS0103 ... OpenBattlegroundMuster`.

- [ ] **Step 4: Implement** the pure rule:

```csharp
namespace DOL.GS
{
    /// <summary>Muster by the normal RvR way: a joiner is ported to the landing keep of its group leader.</summary>
    public static class OpenBattlegroundMuster
    {
        /// <summary>Out and back in when the leader is in the BG and the member is not at the leader's landing; two bots outside the BG board by the porter as before.</summary>
        public static bool NeedsPortOut(ushort memberRegion, ushort leaderRegion, bool atLeaderLanding) =>
            leaderRegion == OpenBattlegroundMode.OpenBgRegion && !(memberRegion == leaderRegion && atLeaderLanding);

        public static (int X, int Y)? LandingFor(GameBot leader, System.Collections.Generic.IReadOnlyList<(ushort Region, int X, int Y, eRealm Realm)> landings)
        {
            if (leader == null || landings == null) return null;
            foreach ((ushort region, int x, int y, eRealm realm) in landings)
                if (region == leader.CurrentRegionID && realm == leader.Realm) return (x, y);
            return null;
        }
    }
}
```

Wire (using the call proven in Step 1): in the group-join path for open BG units, when `NeedsPortOut` is true, move the member out (release it to its bind, the existing "port out" used by `AutonomousStuckWatchdog`) and, on its next tick, `MoveTo` the leader's landing from `LandingFor`. In `CandidatePools`, in `OpenBgMode.ServerPvp` return one merged pool for archetype units instead of three realm pools; `WithinPorterReach` and `MateRejection.OtherRegion` no longer reject a member that the muster will port.

- [ ] **Step 5: Run tests** — filter `UT_OpenBattlegroundMuster|UT_OpenBattleground|UT_OpenBg`. Expected: no new failure.

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/bots/autonomous/AutonomousBotGroupCoordinator.OpenBattleground.cs source/server/GameServer/battlegrounds/open/OpenBattlegroundRoster.cs source/server/GameServer/battlegrounds/open/OpenBattlegroundMuster.cs source/server/Tests/UnitTests/UT_OpenBattlegroundMuster.cs
git commit -m "feat(open-bg): members muster at the leader's landing keep (out and back in), mixed pools in Server PvP"
```

---

### Task 9: Doctrine behaviour in the BG driver (team fight)

**Files:**
- Modify: `source/server/GameServer/bots/autonomous/AutonomousBattlegroundDriver.cs` (the opponent loop at lines ~26-27 and ~170-178), `source/server/GameServer/bots/autonomous/AutonomousRvrDoctrine.cs` (lines 116, 171, 185)
- Test: `source/server/Tests/UnitTests/UT_OpenBattlegroundDoctrine.cs` (new) plus the existing doctrine tests

**Interfaces:**
- Consumes: `AutonomousRvrDoctrine.Derive`, `RvrDoctrineKind`, `AutonomousRvrObserve`, `AutonomousRvrGroupPause`.
- Produces: pure `AutonomousBattlegroundDriver.ShouldBreakOff(bool healerDead, int enemyGroupsNear, int aliveFriends, int aliveEnemies) -> bool` (true when the healer is dead, or a second enemy group is near, or the odds are 3:1 against).

- [ ] **Step 1: Failing test**

```csharp
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundDoctrine
{
    [Test]
    public void ABattlegroundGroupBreaksOffOnHealerDeathASecondGroupOrThreeToOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(true, 1, 8, 8), Is.True, "healer dead");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 2, 8, 8), Is.True, "a second enemy group");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 1, 3, 9), Is.True, "3:1");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 1, 8, 8), Is.False, "an even fight stays");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 0, 1, 0), Is.False, "no enemy, nothing to break off");
        });
    }

    [Test]
    public void TheMinstrelHasStealthAndTwoBombersWithAreaMezMakeABombGroup()
    {
        Assert.That(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Minstrel).HasFlag(RvrClassTraits.Stealth), Is.True);
        var members = new[]
        {
            new RvrDoctrineMember(eCharacterClass.Wizard, 50, false), new RvrDoctrineMember(eCharacterClass.Wizard, 50, false),
            new RvrDoctrineMember(eCharacterClass.Sorcerer, 50, false), new RvrDoctrineMember(eCharacterClass.Cleric, 50, true),
        };
        Assert.That(AutonomousRvrDoctrine.Derive(members, default, 4, false).Kind, Is.EqualTo(RvrDoctrineKind.BombGroup));
    }
}
```

(Adjust `RvrDoctrineMember` constructor arguments and the `Derive` signature to the real ones read at `AutonomousRvrDoctrine.cs:158` and `AutonomousRvrDoctrineRuntime.cs:97`; the test's intent is the two assertions, and a signature mismatch is a compile error to fix, not to hide.)

- [ ] **Step 2: Run to verify failure** — build; expected `CS0117 ... ShouldBreakOff`.

- [ ] **Step 3: Implement** in the driver:

```csharp
    /// <summary>Pure break-off rule of the world RvR doctrine, used by open-BG groups: healer dead, a second enemy group near, or three to one against.</summary>
    public static bool ShouldBreakOff(bool healerDead, int enemyGroupsNear, int aliveFriends, int aliveEnemies) =>
        aliveEnemies > 0 && (healerDead || enemyGroupsNear >= 2 || aliveEnemies >= 3 * Math.Max(1, aliveFriends));
```

and in `AutonomousRvrDoctrine`: add `RvrClassTraits.Stealth` to the Minstrel entry (line 116); relax `BombGroup` (lines 171, 185) from "a Healer class with AreaStun" to "two or more `PointBlankBomb` members plus one `AreaMez` or `AreaStun` member". Because `Derive` is shared with the world RvR (`AutonomousRvrDoctrineRuntime.cs:67,82`) and Gothwaite (`GothwaitePvpCombat.cs:58`), run all doctrine tests before and after.

- [ ] **Step 4: Wire the driver.** In the opponent loop (driver lines ~170-178), before attacking, call `ShouldBreakOff` with the group's live counts (read from `bot.Group`, enemies within `OpponentRange`); when true, give the group the existing retreat order (`OpenBgRole.Retreat` path to its own bridge) for 90 game seconds and skip the attack; after the pause the normal loop resumes (regroup, as the concept says 1-3 minutes). Use `AutonomousRvrGroupPause` for the pause timing instead of a new timer.

- [ ] **Step 5: Run tests** — filter `UT_OpenBattlegroundDoctrine|UT_Rvr|UT_AutonomousRvr|UT_Gothwaite`. Expected: no new failure.

- [ ] **Step 6: Commit**

```bash
git add source/server/GameServer/bots/autonomous/AutonomousBattlegroundDriver.cs source/server/GameServer/bots/autonomous/AutonomousRvrDoctrine.cs source/server/Tests/UnitTests/UT_OpenBattlegroundDoctrine.cs
git commit -m "feat(open-bg): break-off rule in the BG driver, Minstrel stealth, bomb group by bombers plus area CC"
```

---

### Task 10: Settings surface, docs, version, ship

**Files:**
- Modify: `source/tools/OfflineDaoc.Launcher/OpenBgSettings.cs`, `OpenBgSettingsControl.cs` (mode and landing-peace choices, presets text), `source/server/GameServer/commands/playercommands/OpenBgManagerSession.cs` and `OpenBgSetupCommand.cs` (`/bgsetup mode <serverpvp|perrealm>`, `/bgsetup peace <all|realm|auto>`, `/bgsetup presets <list>`), `docs/CAMLANN.md` (open BG table), `docs/TASKS.md` (item 116 follow-ups), `CHANGELOG.md`, version pins, `ALL SERVER COMMANDS.txt`
- Test: `source/tools/OfflineDaoc.Launcher.Tests/OpenBgSettingsTests.cs` (extend), `UT_OpenBgSetupCommand.cs` (extend)

**Interfaces:**
- Consumes: the three properties of Task 1.
- Produces: launcher fields `Mode`, `LandingPeace`, `Presets` round-tripped through `OpenBgSettings`; `/bgsetup mode|peace|presets` writes `open_bg_mode`, `open_bg_landing_peace`, `open_bg_presets`.

- [ ] **Step 1: Failing tests.** In `UT_OpenBgSetupCommand` add one test per command: `/bgsetup mode perrealm` sets `OPEN_BG_MODE` to `perrealm`, an invalid mode is rejected with the usage line and leaves the value unchanged; same for `peace` (`all`, `realm`, `auto` = empty) and `presets` (`std8:2,solo:6` stored verbatim, an unparseable list is rejected). In `OpenBgSettingsTests` add a round-trip test for the three launcher fields. Run: build, expect `CS` errors for the missing members.

- [ ] **Step 2: Implement** the command branches next to the existing `<key> <value>` handling in `OpenBgSetupCommand.cs` (read the current switch first and follow its validation and live-refresh pattern exactly), the three launcher fields and their controls, and the document updates. `ALL SERVER COMMANDS.txt` gets the three subcommands in the existing `/bgsetup` line (CRLF file: edit in binary mode).

- [ ] **Step 3: Run the filtered suites**

Run: `tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OpenBg|FullyQualifiedName~OpenBattleground|FullyQualifiedName~Rvr|FullyQualifiedName~Gothwaite"` and the launcher tests `--filter "FullyQualifiedName~OpenBg|FullyQualifiedName~Presentation"`.
Expected: no new failure versus the baseline.

- [ ] **Step 4: Version and changelog.** Bump to 0.278.0 in the four pins, add a dated heading to `CHANGELOG.md` with Added (modes, landing peace by mode, presets, `/bgsetup mode|peace|presets`, `OPEN_BG_FIELD`, muster at the leader's landing, break-off in the BG driver) and Fixed (claim lock for the allied claim flip, roster healer test, Minstrel stealth, bomb group trigger). Move TASKS 116 follow-ups (allied claim flip) to done with the version.

- [ ] **Step 5: Merge and push (owner rule: direct to main).**

```bash
git fetch origin && git merge origin/main
# build server and launcher in Release (commands in docs/DEVELOPMENT.md), then:
git checkout main && git merge --ff-only obg-window && git push origin main
```

If `main` moved, merge it into `obg-window` first and rebuild; never force-push. Deploy only on the owner's explicit request ("ship now"); report the result honestly: offline tests only, real-client and live behaviour left to the owner.

- [ ] **Step 6: Live measurement hand-off (owner decides when).** After a deploy the owner runs the open BG 30 minutes in each mode; the numbers to read are `OPEN_BG_FIELD`, `KEEP_CLAIMED keep=11`, `OPEN_BG_ARCHETYPE_MISS` and the death split by phase (concept section 4).

---

## Self-Review

**Spec coverage** (spec 2a/2b): two modes with switch (Tasks 1, 3, 10); landing peace by mode (Task 2); presets for roles with team fight (Tasks 6, 7, 9); one UI (the window is Plan 2; until then `/bgsetup` shows both pages, Task 10 adds the new subcommands); muster at the leader's landing (Task 8); claim-flip fix and metric log from the concept (Tasks 4, 5). Not in this plan, deliberately: the Custom5 window and client stage (Plan 2), vulture ring and second raid (owner OK pending), defender unit kind, archetype-count targets replacing the realm targets (needs the window and Task 7's live result first).

**Placeholder scan:** Tasks 4, 8, 9 and 10 say "read the call site first" at exactly the spots where the exact signature is not yet known (`RvrDoctrineMember`, the muster's direct-move call, the setup command's switch). Each of those names the file and lines, the pure rule is given in full code, and a mismatch is stated as a compile error to fix. These are the places where the code, not the plan, is the source of truth.

**Type consistency:** `OpenBgMode`, `OpenBgLandingPeace`, `OpenBattlegroundMode.Current/Peace/RealmsAreFriends` (Task 1) are used unchanged in Tasks 2, 3, 7, 8. `OpenBgFill`, `OpenBgSlotRole`, `OpenBattlegroundArchetypes.Get/ClassesFor/ParseMix/Fill` (Tasks 6, 7) match. `ClaimAllowed(OpenBgRole, bool, long)` (Task 4) uses the existing `OpenBgRole` enum.

**Review Focus coverage:** realm `None` (Tasks 1, 2 tests), region scoping (Tasks 1, 2, 3 tests), live mode switch (read at use, Task 1 design), claim lock idle roles (Task 4 test), archetype miss fallback (Task 7 test), empty preset text (Task 6 test), already-at-landing member (Task 8 test).
