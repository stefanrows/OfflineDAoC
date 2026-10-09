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

        public static void Initialize(AbstractGameKeep keep)
        {
            if (!Applies(keep)) return;
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
            if (keep.Guild == null && !keep.DBKeep.LordDefeated)
            {
                keep.Guild = GuildMgr.GetGuildByName(GarrisonName) ?? GuildMgr.CreateGuild(eRealm.None, GarrisonName);
                if (keep.Guild == null) throw new InvalidOperationException("Cannot initialize the frontier garrison guild.");
                if (!keep.Guild.ClaimedKeeps.Contains(keep)) keep.Guild.ClaimedKeeps.Add(keep);
                keep.SaveIntoDatabase();
                foreach (GameKeepGuard guard in keep.Guards.Values) guard.ChangeGuild();
            }
            ApplyWardenStartLevel(keep);
            EnsureClaimPoint(keep);
        }

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

        public static void DefeatLord(GuardLord lord)
        {
            AbstractGameKeep keep = lord.Component.Keep;
            if (keep.DBKeep.LordDefeated) return;
            if (keep.Guild != null) keep.Release();
            keep.DBKeep.LordDefeated = true;
            keep.LastAttackedByEnemyTick = 0;
            keep.StartCombatTick = 0;
            foreach (GameKeepGuard guard in keep.Guards.Values)
            {
                guard.attackComponent.StopAttack();
                if (guard.Brain is DOL.AI.Brain.StandardMobBrain brain) brain.ClearAggroList();
            }
            keep.SaveIntoDatabase();
            EnsureClaimPoint(keep);
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
