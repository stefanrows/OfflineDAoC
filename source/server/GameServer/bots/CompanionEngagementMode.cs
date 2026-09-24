using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    public enum eCompanionEngagementMode { Aggressive, Defensive, Passive }

    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        public const int RecallDistance = 2100;
        public const int RegroupDistance = 650;

        private sealed class Mode
        {
            public eCompanionEngagementMode GroupOrder;
            public bool HasGroupOrder;
            public GameLiving Pull;
        }

        private sealed class ReturnState { public bool Returning; }

        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        private static readonly ConditionalWeakTable<GameBot, ReturnState> Returns = new();

        public static void Set(GamePlayer player, bool defensive) =>
            Set(player, defensive ? eCompanionEngagementMode.Defensive : eCompanionEngagementMode.Aggressive);

        public static void Set(GamePlayer player, eCompanionEngagementMode order)
        {
            if (player == null) return;
            Mode mode = Modes.GetOrCreateValue(player);
            mode.GroupOrder = order;
            mode.HasGroupOrder = true;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
        }

        public static void ClearGroupOrder(GamePlayer player)
        {
            if (player != null && Modes.TryGetValue(player, out Mode mode))
            {
                mode.HasGroupOrder = false;
                mode.Pull = null;
                CompanionPvpEngagement.Reset(player);
            }
        }

        /// <summary>The order that overrides every companion's saved stance, if one is set.</summary>
        public static bool TryGetGroupOrder(GamePlayer player, out eCompanionEngagementMode order)
        {
            order = eCompanionEngagementMode.Aggressive;
            if (player == null || !Modes.TryGetValue(player, out Mode mode) || !mode.HasGroupOrder)
                return false;
            order = mode.GroupOrder;
            return true;
        }

        private static GameBot Companion(GameLiving actor)
        {
            // Include companion pets, never human pets or autonomous bots.
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot)
                {
                    GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
                    return (bot.IsTemporaryGroupHelper || bot.IsPersistentPlayerCompanion) &&
                        !bot.IsAutonomousWorldBot && leader != null && bot.Group != null &&
                        bot.Group == leader.Group ? bot : null;
                }
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }

        public static eCompanionEngagementMode Effective(GameLiving actor)
        {
            GameBot bot = Companion(actor);
            if (bot == null) return eCompanionEngagementMode.Aggressive;
            GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
            if (Modes.TryGetValue(leader, out Mode mode) && mode.HasGroupOrder) return mode.GroupOrder;
            return bot.PlayerCompanionRecord?.EngagementPreference switch
            {
                "defensive" => eCompanionEngagementMode.Defensive,
                "passive" => eCompanionEngagementMode.Passive,
                _ => eCompanionEngagementMode.Aggressive
            };
        }

        public static bool ShouldRegroup(GameLiving actor)
        {
            GameBot bot = Companion(actor);
            if (bot == null) return false;
            GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
            if (!bot.IsAlive || leader?.IsAlive != true || bot.CurrentRegionID != leader.CurrentRegionID) return false;
            ReturnState state = Returns.GetOrCreateValue(bot);
            if (Effective(bot) == eCompanionEngagementMode.Passive) return true;
            if (bot.IsWithinRadius(leader, RegroupDistance)) state.Returning = false;
            else if (!bot.IsWithinRadius(leader, RecallDistance)) state.Returning = true;
            return state.Returning;
        }

        public static GamePlayer DefensiveLeader(GameLiving actor)
        {
            GameBot bot = Companion(actor);
            return bot != null && Effective(bot) == eCompanionEngagementMode.Defensive
                ? bot.PlayerGroupLeader ?? bot.Owner : null;
        }

        public static bool Allows(GameLiving actor, GameLiving target)
        {
            GameBot bot = Companion(actor);
            if (bot == null) return true;
            if (Effective(actor) == eCompanionEngagementMode.Passive) return false;
            if (ShouldRegroup(actor)) return false;
            GamePlayer owner = bot.PlayerGroupLeader ?? bot.Owner;
            if (target != null && (target.CurrentRegionID != owner.CurrentRegionID ||
                !owner.IsWithinRadius(target, RecallDistance))) return false;
            GamePlayer leader = DefensiveLeader(actor);
            return leader == null || target != null && target.CurrentRegionID == leader.CurrentRegionID &&
                (leader.IsWithinRadius(target, DefensiveRadius) || CompanionPvpEngagement.Defending(actor, target));
        }

        public static void RememberPull(GamePlayer player, GameLiving target)
        {
            if (player != null && target != null) Modes.GetOrCreateValue(player).Pull = target;
        }

        public static GameLiving NearbyPull(GameBot bot)
        {
            GamePlayer player = DefensiveLeader(bot);
            if (player == null || !Modes.TryGetValue(player, out Mode mode)) return null;
            GameLiving target = mode.Pull;
            if (target?.IsAlive != true || target.ObjectState != GameObject.eObjectState.Active ||
                target.CurrentRegionID != player.CurrentRegionID)
            {
                mode.Pull = null;
                return null;
            }
            return Allows(bot, target) ? target : null;
        }
    }
}
