using System;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;

namespace DOL.GS.Keeps
{
    // Stationary world defenders, never part of the autonomous population.
    public static class PvpKeepCampaign
    {
        private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(PvpKeepCampaign));
        public const string GarrisonName = "Frontier Wardens";
        public const int GuardRealmPoints = 25;
        public const int CaptureRealmPoints = 1500;
        public static bool Applies(AbstractGameKeep keep) => GameServer.ServerRules is PvPServerRules &&
            keep != null && !keep.IsPortalKeep && (keep.CurrentZone?.IsOF == true ||
                BattlegroundCampaignCatalog.Find((ushort)keep.Region) != null);
        public static bool IsGarrison(Guild guild) => guild?.Name == GarrisonName;

        /// <summary>Owner decision 1a (2026-09-28): a keep the Frontier Wardens
        /// hold stands like a 1.65 unclaimed keep, at door/wood level 1 (door
        /// 50 x 200 = 10,000 HP; guards 52, lord 63 with the 1.6 guard
        /// multiplier). Only a claiming guild raises it (starting_keep_claim_level).</summary>
        public const byte WardenKeepLevel = 1;

        /// <summary>RvR world (upstream 0.38c defaults, port session S2): a captured keep stands at
        /// starting_keep_level 4 (<see cref="AbstractGameKeep.Reset"/>), a claimed one at starting_keep_claim_level 5.
        /// The fork's save carries 1 for both (FrontierKeepBalanceUpdate, Camlann's 1.65 unclaimed keeps).</summary>
        public const byte RvrCaptureLevel = 4, RvrClaimLevel = 5;

        /// <summary>The level a keep stands at after a realm capture: Camlann the Warden level, RvR world upstream's 4.</summary>
        public static byte CaptureLevel(WorldModelKind world) => world == WorldModelKind.Realm ? RvrCaptureLevel : WardenKeepLevel;

        /// <summary>starting_keep_level by world (R6): the RvR world uses upstream 0.38c's default 4 wherever the
        /// upstream code reads the property (a capture's reset, a guild's release, the capture realm points); Camlann
        /// reads the property (1 in the fork's save). The saved property row stays as it is in both worlds.</summary>
        public static byte StartingLevel(WorldModelKind world, int startingLevelProperty) =>
            world == WorldModelKind.Realm ? RvrCaptureLevel : (byte)startingLevelProperty;

        /// <summary><see cref="StartingLevel(WorldModelKind, int)"/> for the running world and its property.</summary>
        public static byte StartingLevel() =>
            StartingLevel(WorldMode.Current, ServerProperties.Properties.STARTING_KEEP_LEVEL);

        /// <summary>The level a guild claim sets: RvR world upstream's 5, Camlann starting_keep_claim_level.</summary>
        public static byte ClaimLevel(WorldModelKind world, int claimLevelProperty) =>
            world == WorldModelKind.Realm ? RvrClaimLevel : (byte)claimLevelProperty;

        /// <summary>The level a Warden-held keep is set to at server start, or null
        /// to leave it: only ordinary claimable keeps (base level 50, no relic
        /// keep) held by the garrison, never a guild-claimed or relic keep.</summary>
        public static byte? WardenStartLevel(bool heldByGarrison, int baseLevel, bool isRelic, int level) =>
            heldByGarrison && baseLevel == 50 && !isRelic && level != WardenKeepLevel ? WardenKeepLevel : null;

        public static void ApplyWardenStartLevel(AbstractGameKeep keep)
        {
            if (!Applies(keep) || !IsGarrison(keep.Guild)) return;
            keep.StopChangeLevelTimer();
            byte? level = WardenStartLevel(true, keep.BaseLevel, keep.IsRelic, keep.Level);
            if (level == null) return;
            byte previous = keep.Level;
            keep.ChangeLevel(level.Value);
            log.Info($"FRONTIER_WARDEN_KEEP_LEVEL keep={keep.KeepID} name={keep.Name} from={previous} to={keep.Level}");
        }

        /// <summary>RvR world (R3a): a portal keep always stands for its realm, as the 1.65
        /// border keeps. The RvR reset writes it; a realm-less row (an older save, a
        /// battleground portal keep) takes its original realm, so its guards and its
        /// hostility agree. Null leaves the keep's realm as it is.</summary>
        public static eRealm? PortalKeepStartRealm(WorldModelKind world, bool portalKeep, eRealm realm, eRealm originalRealm) =>
            world == WorldModelKind.Realm && portalKeep && !PvpCombatant.IsPlayableRealm(realm) &&
            PvpCombatant.IsPlayableRealm(originalRealm) ? originalRealm : null;

        /// <summary>R3b temple rule: a relic temple always stands for its original realm. A row of another realm (a
        /// temple captured under 0.337.0) or of none takes it back at start. Null leaves the keep as it is.
        /// Port session S4 (owner decision 2026-10-25 22:20 #1): the RvR world follows upstream 0.38c, where a relic keep
        /// is taken like any keep and keeps its captor, so this reset no longer runs there; the temple rule belongs to
        /// Camlann (<see cref="RvrRelicRules.TempleRulesApply"/>), whose relic keeps follow the guild rules and are not
        /// reset by realm, so the reset is dormant.</summary>
        public static eRealm? RelicKeepStartRealm(WorldModelKind world, bool relicKeep, eRealm realm, eRealm originalRealm) =>
            world == WorldModelKind.Realm && RvrRelicRules.TempleRulesApply(world) && relicKeep && realm != originalRealm &&
            PvpCombatant.IsPlayableRealm(originalRealm) ? originalRealm : null;

        public static void Initialize(AbstractGameKeep keep)
        {
            // A portal keep stands for its realm (R3a). The R3b relic temple reset is dormant since port session S4
            // (RelicKeepStartRealm): the RvR world's relic keeps keep their captor, as upstream 0.38c.
            if (keep?.DBKeep != null && (PortalKeepStartRealm(WorldMode.Current, keep.IsPortalKeep, keep.Realm, keep.OriginalRealm) ??
                RelicKeepStartRealm(WorldMode.Current, keep.IsRelic, keep.Realm, keep.OriginalRealm)) is eRealm portalRealm)
            {
                keep.Realm = portalRealm;
                keep.SaveIntoDatabase();
                RefreshGuardsForRealm(keep);
                log.Info($"RVR_PORTAL_KEEP_REALM keep={keep.KeepID} name={keep.Name} realm={portalRealm}");
            }
            if (!Applies(keep)) return;
            // RvR world (upstream 0.38c, port session S1): a frontier keep stands for its original realm at its original
            // level. A save made before then still has it neutral with the Frontier Wardens; it moves once, here.
            if (keep.DBKeep != null && FrontierKeepStart(WorldMode.Current, keep.KeepID, keep.Realm, keep.OriginalRealm,
                    keep.Guild == null || IsGarrison(keep.Guild), keep.IsPortalKeep, keep.IsRelic) is byte startLevel)
            {
                bool fromWardens = IsGarrison(keep.Guild);
                if (fromWardens)
                {
                    keep.Guild.ClaimedKeeps.Remove(keep);
                    keep.Guild = null;
                }
                keep.Realm = keep.OriginalRealm;
                keep.StopChangeLevelTimer();
                keep.ChangeLevel(startLevel);
                keep.SaveIntoDatabase();
                foreach (GameKeepGuard guard in keep.Guards.Values) guard.ChangeGuild();
                RefreshGuardsForRealm(keep);
                log.Info($"RVR_KEEP_ORIGINAL_START keep={keep.KeepID} name={keep.Name} realm={keep.Realm} level={keep.Level} fromWardens={fromWardens}");
            }
            // A claim whose guild is not loaded (yet) must not be handed to the
            // garrison and saved over: that silently erased every bot claim
            // after a restart (2026-10-02). The crew reconcile rebinds it.
            if (keep.Guild == null && !string.IsNullOrEmpty(keep.DBKeep.ClaimedGuildName) &&
                keep.DBKeep.ClaimedGuildName != GarrisonName)
            {
                log.Warn($"KEEP_OWNER_UNRESOLVED keep={keep.KeepID} name={keep.Name} guild=\"{keep.DBKeep.ClaimedGuildName}\"");
                ApplyWardenStartLevel(keep);
                EnsureClaimPoint(keep);
                return;
            }
            if (keep.Guild == null && NeedsGarrison(WorldMode.Current, keep.DBKeep.LordDefeated, keep.Realm))
            {
                keep.Guild = GuildMgr.GetGuildByName(GarrisonName) ?? GuildMgr.CreateGuild(eRealm.None, GarrisonName);
                if (keep.Guild == null) throw new InvalidOperationException("Cannot initialize the frontier garrison guild.");
                if (!keep.Guild.ClaimedKeeps.Contains(keep)) keep.Guild.ClaimedKeeps.Add(keep);
                // RvR world: the Wardens are no realm, so their guards show and fight as neutrals.
                bool realmChanged = WorldMode.IsRvr && keep.Realm != eRealm.None;
                if (realmChanged) keep.Realm = eRealm.None;
                keep.SaveIntoDatabase();
                foreach (GameKeepGuard guard in keep.Guards.Values) guard.ChangeGuild();
                if (realmChanged) RefreshGuardsForRealm(keep);
            }
            else if (WorldMode.IsRvr && IsGarrison(keep.Guild) && keep.Realm != eRealm.None)
            {
                keep.Realm = eRealm.None;
                keep.SaveIntoDatabase();
                RefreshGuardsForRealm(keep);
            }
            else if (WorldMode.IsRvr && keep.Guild == null && !keep.DBKeep.LordDefeated && !keep.IsRelic &&
                PvpCombatant.IsPlayableRealm(keep.Realm))
            {
                // An unclaimed realm keep always waits at the steward for a claim of its realm.
                keep.DBKeep.LordDefeated = true;
                keep.SaveIntoDatabase();
            }
            ApplyWardenStartLevel(keep);
            EnsureClaimPoint(keep);
        }

        /// <summary>
        /// RvR world (port session S1, docs/RVR_PORT_MAP.md): the level a neutral frontier keep starts at when it takes
        /// its original realm (<see cref="OfflineDaoc.Configuration.RvrKeepStart"/>), or null to leave the keep as it is.
        /// Only one of the 21 frontier keeps, realm-less, held by the Frontier Wardens or nobody, with a realm of origin.
        /// Portal keeps, relic keeps and battleground keeps never; Camlann never.
        /// </summary>
        public static byte? FrontierKeepStart(WorldModelKind world, int keepId, eRealm realm, eRealm originalRealm,
            bool unclaimedOrWardens, bool portalKeep, bool relicKeep) =>
            world == WorldModelKind.Realm && !portalKeep && !relicKeep && unclaimedOrWardens && realm == eRealm.None &&
            PvpCombatant.IsPlayableRealm(originalRealm) ? OfflineDaoc.Configuration.RvrKeepStart.StartLevel(keepId) : null;

        /// <summary>Whether an unheld keep goes to the Frontier Wardens at start. Camlann:
        /// every unheld keep whose lord stands. RvR world (R3a, owner decision 12): only a
        /// realm-less one; a keep of a realm (portal, relic, or captured and not yet
        /// claimed) stands for that realm without a garrison.</summary>
        public static bool NeedsGarrison(WorldModelKind world, bool lordDefeated, eRealm keepRealm) =>
            !lordDefeated && (world != WorldModelKind.Realm || !PvpCombatant.IsPlayableRealm(keepRealm));

        /// <summary>Guards already in the world take a realm the startup changed (they
        /// normally spawn after the keeps and read it then).</summary>
        private static void RefreshGuardsForRealm(AbstractGameKeep keep)
        {
            foreach (GameKeepGuard guard in keep.Guards.Values)
                if (guard.ObjectState == GameObject.eObjectState.Active && guard.Realm != keep.Realm)
                    guard.RefreshTemplate();
        }

        /// <summary>Whether the keep's combat blocks a claim. Camlann: a defeated lord opens
        /// the steward even while siege damage refreshes the combat timer. RvR world (R3a):
        /// LordDefeated means "captured, awaiting a claim" with a standing lord, so a claim
        /// waits until the keep is out of combat, as the upstream rule.</summary>
        public static bool ClaimBlockedByCombat(WorldModelKind world, bool inCombat, bool campaign, bool lordDefeated) =>
            inCombat && (world == WorldModelKind.Realm || !(campaign && lordDefeated));

        public static bool ClaimBlockedByCombat(AbstractGameKeep keep) => keep != null &&
            ClaimBlockedByCombat(WorldMode.Current, keep.InCombat, Applies(keep), keep.DBKeep?.LordDefeated == true);

        /// <summary>Whether a lord of the keep stands in the world.</summary>
        public static bool LordStands(AbstractGameKeep keep) => keep != null &&
            keep.Guards.Values.OfType<GuardLord>().Any(lord => lord.IsAlive && lord.ObjectState == GameObject.eObjectState.Active);

        /// <summary>The "lord down, nobody claimed yet" state the open BG director plays on.
        /// Camlann: the saved LordDefeated flag (the lord stays down until a claim). RvR world:
        /// the flag only means "awaiting a claim"; the lord must really be down.</summary>
        public static bool LordDownUnclaimed(WorldModelKind world, bool claimed, bool lordDefeated, bool lordStands) =>
            !claimed && lordDefeated && (world != WorldModelKind.Realm || !lordStands);

        public static void EnsureClaimPoint(AbstractGameKeep keep)
        {
            if (!Applies(keep) || !keep.DBKeep.LordDefeated || keep.IsRelic || keep.ClaimPoint != null) return;
            lock (keep.Guards)
            {
                if (keep.ClaimPoint != null) return;
                GuardLord lord = keep.Guards.Values.OfType<GuardLord>().FirstOrDefault();
                if (lord == null)
                {
                    log.Warn($"KEEP_CLAIM_STEWARD_MISSING keep={keep.KeepID} reason=no_lord_position");
                    return;
                }
                KeepClaimPoint steward = new KeepClaimPoint(keep, lord);
                if (!steward.AddToWorld())
                {
                    log.Warn($"KEEP_CLAIM_STEWARD_MISSING keep={keep.KeepID} reason=spawn_failed region={steward.CurrentRegionID} x={steward.X} y={steward.Y} z={steward.Z}");
                    return;
                }
                keep.ClaimPoint = steward;
                log.Info($"KEEP_CLAIM_STEWARD_READY keep={keep.KeepID} region={steward.CurrentRegionID} x={steward.X} y={steward.Y} z={steward.Z}");
            }
        }

        /// <summary>Keep saves and level changes run synchronously on the game
        /// tick. A step at or above this many milliseconds is logged so a long
        /// tick (bug 134) can be attributed to its step.</summary>
        public const int SlowKeepStepMilliseconds = 250;

        /// <summary>Logs KEEP_STEP_TIMING when the step that began at
        /// <paramref name="mark"/> (a Stopwatch timestamp) was slow; returns the
        /// current timestamp for the next step.</summary>
        public static long LogSlowKeepStep(AbstractGameKeep keep, string owner, string step, long mark)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(mark, now).TotalMilliseconds;
            if (milliseconds >= SlowKeepStepMilliseconds)
                log.Warn($"KEEP_STEP_TIMING keep={keep?.KeepID} owner={owner} step={step} ms={(int)milliseconds}");
            return now;
        }

        public static void DefeatLord(GuardLord lord)
        {
            AbstractGameKeep keep = lord.Component.Keep;
            if (keep.DBKeep.LordDefeated) return;
            using var batch = KeepSaveBatch.Begin("defeat_lord", keep);
            long mark = System.Diagnostics.Stopwatch.GetTimestamp();
            if (keep.Guild != null) keep.Release();
            mark = LogSlowKeepStep(keep, "defeat_lord", "release", mark);
            keep.DBKeep.LordDefeated = true;
            keep.LastAttackedByEnemyTick = 0;
            keep.StartCombatTick = 0;
            foreach (GameKeepGuard guard in keep.Guards.Values)
            {
                guard.attackComponent.StopAttack();
                if (guard.Brain is DOL.AI.Brain.StandardMobBrain brain) brain.ClearAggroList();
            }
            keep.SaveIntoDatabase();
            mark = LogSlowKeepStep(keep, "defeat_lord", "save", mark);
            EnsureClaimPoint(keep);
            LogSlowKeepStep(keep, "defeat_lord", "steward", mark);
        }

        // ---- RvR world (R3a): keeps change hands by realm --------------------------------------------------------

        /// <summary>The realm a lord kill captures for in the RvR world: the realm the killer fights for (pets,
        /// companions and /spawn helpers count as their owner's, <see cref="PvpCombatant.AllianceRealm"/>), or
        /// None for a realm-less killer (a mob, a neutral encounter bot, no killer).</summary>
        public static eRealm CapturingRealm(GameObject killer)
        {
            GameLiving combatant = PvpCombatant.Resolve(killer as GameLiving);
            eRealm realm = combatant == null ? eRealm.None : PvpCombatant.AllianceRealm(combatant);
            return PvpCombatant.IsPlayableRealm(realm) ? realm : eRealm.None;
        }

        /// <summary>Whether a lord kill for <paramref name="captor"/> changes the owner: a playable realm takes a
        /// Warden, realm-less or other realm's keep. A keep never changes to its own realm.</summary>
        public static bool CaptureChangesOwner(eRealm keepRealm, bool heldByGarrison, eRealm captor) =>
            PvpCombatant.IsPlayableRealm(captor) && (heldByGarrison || keepRealm != captor);

        /// <summary>Whether a lord kill captures the keep for a realm. RvR world (upstream 0.38c, port session S4): every
        /// campaign keep, the relic keeps (skin 99) included, as upstream's Normal rules (AbstractGameKeep.Reset). The R3b
        /// temple rule, under which a relic temple is never captured, applies only where
        /// <see cref="RvrRelicRules.TempleRulesApply"/> says so (Camlann, which does not capture by realm).</summary>
        public static bool RealmCaptureApplies(WorldModelKind world, bool relicKeep) =>
            !relicKeep || !RvrRelicRules.TempleRulesApply(world);

        /// <summary>Only a guild of the keep's realm may claim it in the RvR world (owner decision 13): the claimer
        /// fights for the keep's realm and the guild is of that realm (a realm-less guild, as the RvR rules allow,
        /// counts through its member). A realm-less keep is first captured, never claimed.</summary>
        public static bool RealmMayClaim(eRealm keepRealm, eRealm claimerRealm, eRealm guildRealm) =>
            PvpCombatant.IsPlayableRealm(keepRealm) && claimerRealm == keepRealm &&
            (guildRealm == eRealm.None || guildRealm == keepRealm);

        /// <summary>The claim realm gate for the current world; Camlann claims by guild only.</summary>
        public static bool MayClaimInWorld(AbstractGameKeep keep, GameLiving claimer) =>
            MayClaimInWorld(WorldMode.Current, keep, claimer);

        public static bool MayClaimInWorld(WorldModelKind world, AbstractGameKeep keep, GameLiving claimer)
        {
            if (world != WorldModelKind.Realm) return true;
            if (keep == null || claimer == null) return false;
            return RealmMayClaim(keep.Realm, PvpCombatant.AllianceRealm(claimer),
                PvpCombatant.GuildOf(claimer)?.Realm ?? eRealm.None);
        }

        /// <summary>The refusal for a claimer of the wrong realm, or null when the realm may claim.</summary>
        public static string RealmClaimRefusal(AbstractGameKeep keep, GameLiving claimer)
        {
            if (MayClaimInWorld(keep, claimer)) return null;
            if (keep == null || !PvpCombatant.IsPlayableRealm(keep.Realm))
                return $"Defeat the keep lord to win {keep?.Name ?? "this keep"} for your realm first.";
            return $"{keep.Name} belongs to {GlobalConstants.RealmToName(keep.Realm)}; only a guild of that realm can claim it.";
        }

        /// <summary>The lord of a campaign keep fell in the RvR world (owner decisions 12 and 17, spec 4.1).</summary>
        public static void CaptureForRealm(GuardLord lord, GameObject killer)
        {
            AbstractGameKeep keep = lord?.Component?.Keep;
            if (keep == null) return;
            CaptureForRealm(keep, CapturingRealm(killer), PvpCombatant.Resolve(killer as GameLiving));
        }

        /// <summary>
        /// The keep flips to <paramref name="captor"/> at once, as the upstream Normal rules do
        /// (<see cref="AbstractGameKeep.Reset"/>): the previous claim (a guild or the Wardens) is released, walls
        /// are repaired, doors and banners take the realm, and the guards die and respawn as the realm's guards
        /// (the lord a second later). The keep stands at <see cref="CaptureLevel"/> without a claim (Camlann 1,
        /// RvR world 4 as upstream's starting_keep_level) and the Keep Claim Steward waits
        /// for an optional claim by a guild of that realm (<see cref="AbstractGameKeep.DBKeep"/>.LordDefeated
        /// marks "awaiting a claim" here; the lord still stands so another realm can retake it). The capture is
        /// saved and never returns to the Wardens. Returns false, changing nothing, for a realm-less captor, the
        /// keep's own realm, or a relic keep: the six relic temples never change hands (R3b, as the 1.65
        /// temples), so their pads, gates and temple guards stay their realm's.
        /// </summary>
        public static bool CaptureForRealm(AbstractGameKeep keep, eRealm captor, GameLiving captorLiving)
        {
            if (!RealmCaptureApplies(WorldMode.Current, keep.IsRelic))
            {
                log.Info($"KEEP_REALM_CAPTURE_SKIPPED keep={keep.KeepID} name={keep.Name} realm={keep.Realm} captor={captor} reason=relic_temple");
                return false;
            }
            eRealm previous = keep.Realm;
            byte previousLevel = keep.Level;
            string previousOwner = keep.Guild?.Name ?? string.Empty;
            if (!CaptureChangesOwner(previous, IsGarrison(keep.Guild), captor))
            {
                log.Info($"KEEP_REALM_CAPTURE_SKIPPED keep={keep.KeepID} name={keep.Name} realm={previous} captor={captor}");
                return false;
            }
            using var batch = KeepSaveBatch.Begin("realm_capture", keep);
            long mark = System.Diagnostics.Stopwatch.GetTimestamp();
            keep.DBKeep.LordDefeated = !keep.IsRelic;
            PayCaptureReward(keep, captor, captorLiving);
            keep.Reset(captor);
            mark = LogSlowKeepStep(keep, "realm_capture", "reset", mark);
            byte captureLevel = CaptureLevel(WorldMode.Current);
            // R6: the RvR world's reset already writes level 4 (StartingLevel); a keep that stood at another level
            // still gets the full level change (components, guards, patrols), as before. Camlann unchanged.
            if (!keep.IsRelic && (keep.Level != captureLevel || WorldMode.IsRvr && previousLevel != captureLevel))
            {
                keep.ChangeLevel(captureLevel);
                mark = LogSlowKeepStep(keep, "realm_capture", "change_level", mark);
            }
            batch.Dispose();
            EnsureClaimPoint(keep);
            LogSlowKeepStep(keep, "realm_capture", "steward", mark);
            log.Info($"KEEP_REALM_CAPTURED keep={keep.KeepID} name={keep.Name} from={previous} previousOwner=\"{previousOwner}\" " +
                $"to={captor} by=\"{captorLiving?.Name}\" bot={captorLiving is GameBot} level={keep.Level}");
            return true;
        }

        /// <summary>RvR capture reward (spec 4.1): the living members of the captor's group and battlegroup who
        /// fight for the capturing realm and stand in the keep area get the capture realm points, once per keep
        /// in 30 minutes. Battleground campaign keeps keep their own bracket-scaled objective rewards.</summary>
        private static void PayCaptureReward(AbstractGameKeep keep, eRealm captor, GameLiving captorLiving)
        {
            if (captorLiving == null || BattlegroundCampaignCatalog.Find((ushort)keep.Region) != null) return;
            if (WorldSimulationClock.UtcNow - keep.DBKeep.LastCaptureRewardAt < TimeSpan.FromMinutes(30)) return;
            keep.DBKeep.LastCaptureRewardAt = WorldSimulationClock.UtcNow;
            var members = new System.Collections.Generic.HashSet<GameLiving> { captorLiving };
            if (captorLiving.Group != null)
                foreach (GameLiving member in captorLiving.Group.GetMembersInTheGroup()) members.Add(member);
            BattleGroup battleGroup = PvpCombatant.BattleGroupOf(captorLiving);
            if (battleGroup != null)
                foreach (GamePlayer member in battleGroup.GetPlayerMembers()) members.Add(member);
            foreach (GameLiving member in members)
                if (member.IsAlive && PvpCombatant.AllianceRealm(member) == captor &&
                    member.CurrentRegionID == keep.Region && keep.Area?.IsContaining(member, false) == true)
                    member.GainRealmPoints(CaptureRealmPoints);
        }

        /// <summary>A claimed campaign keep stands whole: every gate and wall is healed and the gates are closed and
        /// announced. The lord's death never goes through AbstractGameKeep.Reset, which is the only other place that
        /// does this, so a raided keep would otherwise keep its broken gates until the slow 30 minute repair ticks.</summary>
        public static void RestoreDefences(AbstractGameKeep keep)
        {
            int components = 0;
            foreach (GameKeepComponent component in keep.KeepComponents)
            {
                if (component.IsRaized) continue;
                component.Repair(component.MaxHealth - component.Health);
                components++;
            }
            int doors = 0;
            foreach (GameKeepDoor door in keep.Doors.Values)
            {
                door.Reset(keep.Realm);
                doors++;
            }
            foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRegion(keep.CurrentRegion))
                player.Out.SendKeepComponentUpdate(keep, false);
            log.Info($"KEEP_DEFENCES_RESTORED keep={keep.KeepID} name={keep.Name} doors={doors} components={components}");
        }

        public static void CompleteClaim(AbstractGameKeep keep, GameLiving claimer)
        {
            keep.DBKeep.LordDefeated = false;
            keep.ClaimPoint?.Delete();
            keep.ClaimPoint = null;
            foreach (GuardLord lord in keep.Guards.Values.OfType<GuardLord>())
            {
                lord.StopRespawn();
                lord.RefreshTemplate();
                lord.Health = lord.MaxHealth;
                if (lord.ObjectState != GameObject.eObjectState.Active) lord.AddToWorld();
            }
            RestoreDefences(keep);
            // RvR world: the capture paid the realm when the lord fell (CaptureForRealm); the claim adds none.
            if (WorldMode.IsRvr) return;
            // Campaign objectives own bracket-scaled capture rewards.
            if (BattlegroundCampaignCatalog.Find((ushort)keep.Region) != null) return;
            bool rewardReady = WorldSimulationClock.UtcNow - keep.DBKeep.LastCaptureRewardAt >= TimeSpan.FromMinutes(30);
            if (!rewardReady) return;
            keep.DBKeep.LastCaptureRewardAt = WorldSimulationClock.UtcNow;
            // Only nearby, living members of the winning group and guild receive
            // this one-time capture reward. Releasing alone cannot unlock it again.
            var members = claimer.Group?.GetMembersInTheGroup() ?? new System.Collections.Generic.List<GameLiving> { claimer };
            foreach (GameLiving member in members)
                if (member.IsAlive && PvpCombatant.GuildOf(member) == keep.Guild &&
                    member.IsWithinRadius(claimer, WorldMgr.MAX_EXPFORKILL_DISTANCE) &&
                    keep.Area?.IsContaining(member, false) == true)
                    member.GainRealmPoints(CaptureRealmPoints);
        }
    }

    public sealed class KeepClaimPoint : GameNPC
    {
        /// <summary>RvR world: distance of the steward in front of the standing lord.</summary>
        public const int StewardLordDistance = 200;

        /// <summary>Where the steward stands: on the lord's spot in Camlann (the lord is down
        /// there), <see cref="StewardLordDistance"/> in front of him in the RvR world.</summary>
        public static (int X, int Y) StewardSpot(WorldModelKind world, int lordX, int lordY, ushort lordHeading)
        {
            if (world != WorldModelKind.Realm) return (lordX, lordY);
            Point2D front = new Point2D(lordX, lordY).GetPointFromHeading(lordHeading, StewardLordDistance);
            return (front.X, front.Y);
        }

        public AbstractGameKeep Keep { get; }
        public KeepClaimPoint(AbstractGameKeep keep, GuardLord lord)
        {
            Keep = keep;
            Name = "Keep Claim Steward";
            GuildName = keep.Name;
            Model = 40;
            Level = 50;
            Health = MaxHealth;
            Realm = eRealm.None;
            Flags = eFlags.PEACE;
            CurrentRegionID = lord.CurrentRegionID;
            X = lord.X; Y = lord.Y; Z = lord.Z; Heading = lord.Heading;
            // RvR world: the lord stands again beside a captured keep's steward, so the
            // steward waits a few steps in front of him instead of inside him.
            (X, Y) = StewardSpot(WorldMode.Current, lord.X, lord.Y, lord.Heading);
        }
        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player)) return false;
            if (!Keep.CheckForClaim(player)) return true;

            string guildName = PvpCombatant.GuildOf(player)?.Name ?? "your guild";
            player.Out.SendCustomDialog(
                $"Do you want to claim {Keep.Name} for {guildName}? Select Yes to claim it.",
                new CustomDialogResponse(ClaimDialogResponse));
            return true;
        }

        private void ClaimDialogResponse(GamePlayer player, byte response)
        {
            if (response != 0x01) return;
            TryClaim(player);
        }

        public bool TryClaim(GameLiving player)
        {
            if (player == null || !player.IsAlive || !player.IsWithinRadius(this, WorldMgr.INTERACT_DISTANCE))
            {
                if (player is GamePlayer human)
                    human.Out.SendMessage("You must be alive and remain beside the steward to claim this keep.",
                        eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }

            if (!Keep.CheckForClaim(player)) return false;
            Keep.Claim(player);
            return Keep.Guild == PvpCombatant.GuildOf(player);
        }
    }
}
