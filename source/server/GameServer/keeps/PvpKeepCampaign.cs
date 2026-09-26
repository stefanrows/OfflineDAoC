using System;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS.Keeps
{
    // Stationary world defenders, never part of the autonomous population.
    public static class PvpKeepCampaign
    {
        public const string GarrisonName = "Frontier Wardens";
        public const int GuardRealmPoints = 25;
        public const int CaptureRealmPoints = 1500;
        public static bool Applies(AbstractGameKeep keep) => GameServer.ServerRules is PvPServerRules &&
            keep != null && !keep.IsPortalKeep && keep.CurrentZone?.IsOF == true;
        public static bool IsGarrison(Guild guild) => guild?.Name == GarrisonName;

        public static void Initialize(AbstractGameKeep keep)
        {
            if (!Applies(keep)) return;
            if (keep.Guild == null && !keep.DBKeep.LordDefeated)
            {
                keep.Guild = GuildMgr.GetGuildByName(GarrisonName) ?? GuildMgr.CreateGuild(eRealm.None, GarrisonName);
                if (keep.Guild == null) throw new InvalidOperationException("Cannot initialize the frontier garrison guild.");
                if (!keep.Guild.ClaimedKeeps.Contains(keep)) keep.Guild.ClaimedKeeps.Add(keep);
                keep.SaveIntoDatabase();
                foreach (GameKeepGuard guard in keep.Guards.Values) guard.ChangeGuild();
            }
            EnsureClaimPoint(keep);
        }

        public static void EnsureClaimPoint(AbstractGameKeep keep)
        {
            if (!Applies(keep) || !keep.DBKeep.LordDefeated || keep.IsRelic || keep.ClaimPoint != null) return;
            GuardLord lord = keep.Guards.Values.OfType<GuardLord>().FirstOrDefault();
            if (lord == null) return;
            keep.ClaimPoint = new KeepClaimPoint(keep, lord);
            if (!keep.ClaimPoint.AddToWorld()) keep.ClaimPoint = null;
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
            return TryClaim(player);
        }
        public bool TryClaim(GameLiving player)
        {
            if (!player.IsAlive || !player.IsWithinRadius(this, WorldMgr.INTERACT_DISTANCE) || !Keep.CheckForClaim(player)) return false;
            Keep.Claim(player);
            return Keep.Guild == PvpCombatant.GuildOf(player);
        }
    }
}
