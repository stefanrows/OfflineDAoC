using System;
using System.Linq;

namespace DOL.GS.Keeps
{
    /// <summary>
    /// A single dynamic relic mount point belonging to a claimed keep.
    /// </summary>
    public sealed class GameKeepRelicPad : GameRelicPad
    {
        public AbstractGameKeep Keep { get; }

        public Guild Guild => Keep?.Guild;

        public GameKeepRelicPad(AbstractGameKeep keep)
        {
            Keep = keep ?? throw new ArgumentNullException(nameof(keep));
            Name = $"{keep.Name} relic mount";
            GameKeepGuard lord = keep.Guards.Values.OfType<GuardLord>().FirstOrDefault();
            CurrentRegionID = lord?.CurrentRegionID ?? keep.Region;
            X = lord?.X ?? keep.X;
            Y = lord?.Y ?? keep.Y;
            Z = lord == null ? keep.Z + 80 : lord.Z + 40;
            Heading = lord?.Heading ?? keep.Heading;
        }

        public override eRealm Realm
        {
            get => Keep?.Realm ?? eRealm.None;
            set { }
        }

        public override eRelicType PadType => eRelicType.Invalid;

        public override bool AcceptsRelicType(eRelicType type) => type is eRelicType.Strength or eRelicType.Magic;

        public override bool CanReceiveRelic(GameLiving player, GameRelic relic)
        {
            if (Keep?.Guild == null || Keep.IsPortalKeep || !AcceptsRelicType(relic?.RelicType ?? eRelicType.Invalid))
                return false;

            Guild playerGuild = ServerRules.PvpCombatant.GuildOf(player);

            if (playerGuild != Keep.Guild)
                return false;

            DateTime claimedAt = Keep.ClaimedAt;
            return claimedAt == DateTime.MinValue ||
                WorldSimulationClock.UtcNow - claimedAt >= TimeSpan.FromSeconds(ServerProperties.Properties.RELIC_KEEP_CLAIM_DELAY);
        }
    }
}
