using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Bounded per-keep jobs. Weak references and expiring leases cannot retain removed bots.</summary>
    public static class AutonomousSiegeJobs
    {
        private sealed record Lease(WeakReference<GameBot> Bot, long Until, BotSiegeKind Kind);
        private static readonly object Gate = new();
        private static readonly Dictionary<(ushort Region, string Keep, string Side, int Slot), Lease> Jobs = new();
        /// <summary>Camlann: one siege-job pool per guild, not per realm. RvR world (upstream 0.38c, port session S2): one
        /// pool per realm, whatever the bots' guilds (the realm guild names of R4a are cosmetic).</summary>
        private static string SideOf(GameBot bot) =>
            !WorldMode.IsRvr && bot.Guild?.GuildID is { } guild ? guild : "realm-" + (int)bot.Realm;
        public static int Slots(int present) => Slots(WorldMode.Current, present);

        /// <summary>
        /// Camlann (fork): eight arrived troops bring two jobs. RvR world (upstream 0.38c, run 2026-10-07 on Caer
        /// Erasleigh: 17 attackers got two jobs and a single ram wore the gate down 3% a minute): a small force still
        /// brings one ram; an army brings both rams and the trebuchet.
        /// </summary>
        public static int Slots(WorldModelKind world, int present) => world == WorldModelKind.Realm
            ? present < 4 ? 0 : present < 8 ? 1 : present < 24 ? 3 : present < 64 ? 5 : 6
            : present < 8 ? 0 : present < 24 ? 2 : present < 64 ? 4 : 6;

        /// <summary>The engine of a slot. Camlann (fork): rams or ballistas, then catapult and trebuchet, then ballistas.
        /// RvR world (upstream 0.38c): attackers two rams, the trebuchet (triple damage against doors), a catapult,
        /// ballistas; defenders ballistas against siege, catapults on the attackers, never a trebuchet, whose 2,000
        /// minimum range has no spot inside the keep.</summary>
        public static BotSiegeKind SlotKind(WorldModelKind world, int index, bool attacking) => world == WorldModelKind.Realm
            ? attacking
                ? index < 2 ? BotSiegeKind.Ram : index == 2 ? BotSiegeKind.Trebuchet : index == 3 ? BotSiegeKind.Catapult : BotSiegeKind.Ballista
                : index < 2 ? BotSiegeKind.Ballista : index < 4 ? BotSiegeKind.Catapult : BotSiegeKind.Ballista
            : index < 2 ? (attacking ? BotSiegeKind.Ram : BotSiegeKind.Ballista) :
                index < 4 ? (index == 2 ? BotSiegeKind.Catapult : BotSiegeKind.Trebuchet) : BotSiegeKind.Ballista;

        /// <summary>Slot search order (upstream): slots matching a kit the bot already carries come first.</summary>
        public static int[] SlotOrder(WorldModelKind world, int count, bool attacking, BotSiegeKind? carried) =>
            Enumerable.Range(0, count).OrderBy(index => carried.HasValue && SlotKind(world, index, attacking) == carried.Value ? 0 : 1)
                .ThenBy(index => index).ToArray();
        public static bool CanOperate(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Armsman or eCharacterClass.Mercenary or eCharacterClass.Paladin or eCharacterClass.Reaver or eCharacterClass.Scout or
            eCharacterClass.Warrior or eCharacterClass.Berserker or eCharacterClass.Savage or eCharacterClass.Thane or
            eCharacterClass.Hero or eCharacterClass.Champion or eCharacterClass.Blademaster or eCharacterClass.Ranger or eCharacterClass.Valewalker;
        public static bool Eligible(GameBot bot) => bot is { IsAutonomousWorldBot: true, IsPlayerLedGroup: false, IsAlive: true } &&
            bot.CharacterClass != null && CanOperate((eCharacterClass)bot.CharacterClass.ID);

        public static bool TryAcquire(GameBot bot, string keep, int present, bool attacking, bool enemyEngines,
            out BotSiegeKind kind, out int slot, ushort? destinationRegion = null, BotSiegeKind? carried = null)
        {
            kind = default; slot = -1;
            if (!Eligible(bot)) return false;
            ushort region = destinationRegion ?? bot.CurrentRegionID;
            lock (Gate)
            {
                long now = GameLoop.GameLoopTime;
                foreach (var key in Jobs.Where(p => p.Value.Until < now || !p.Value.Bot.TryGetTarget(out GameBot b) || !b.IsAlive ||
                    b.ObjectState != GameObject.eObjectState.Active).Select(p => p.Key).ToArray()) Jobs.Remove(key);
                var existing = Jobs.FirstOrDefault(p => p.Key.Keep == keep && p.Key.Side == SideOf(bot) &&
                    p.Key.Region == region && p.Value.Bot.TryGetTarget(out GameBot b) && b == bot);
                if (existing.Value != null)
                {
                    Jobs[existing.Key] = existing.Value with { Until = now + 120_000 };
                    bot.TempProperties.SetProperty("SiegeJobUntil", now + 120_000);
                    kind = existing.Value.Kind; slot = existing.Key.Slot; return true;
                }
                WorldModelKind world = WorldMode.Current;
                foreach (int index in SlotOrder(world, Math.Min(Slots(world, present), attacking ? 6 : 4), attacking, carried))
                {
                    var key = (region, keep, SideOf(bot), index);
                    if (Jobs.ContainsKey(key)) continue;
                    kind = SlotKind(world, index, attacking);
                    if (kind == BotSiegeKind.Ballista && !enemyEngines) continue;
                    Jobs[key] = new(new(bot), now + 120_000, kind);
                    bot.TempProperties.SetProperty("SiegeJobUntil", now + 120_000);
                    slot = index; return true;
                }
                return false;
            }
        }
        public static void Release(GameBot bot)
        {
            bot?.TempProperties.SetProperty("SiegeJobUntil", 0L);
            lock (Gate) foreach (var key in Jobs.Where(p => p.Value.Bot.TryGetTarget(out GameBot b) && b == bot).Select(p => p.Key).ToArray()) Jobs.Remove(key);
        }
        public static bool HasRamAssignment(GameBot bot, string keep, ushort region)
        {
            lock (Gate) return Jobs.Any(pair => pair.Key.Keep == keep && pair.Key.Region == region &&
                pair.Key.Side == SideOf(bot) && pair.Value.Kind == BotSiegeKind.Ram &&
                pair.Value.Until > GameLoop.GameLoopTime && pair.Value.Bot.TryGetTarget(out var owner) && owner == bot);
        }
        public static void Refresh(GameBot bot)
        {
            lock (Gate) foreach (var key in Jobs.Where(p => p.Value.Bot.TryGetTarget(out GameBot b) && b == bot).Select(p=>p.Key).ToArray())
                Jobs[key] = Jobs[key] with { Until = GameLoop.GameLoopTime + 120_000 };
            bot.TempProperties.SetProperty("SiegeJobUntil", GameLoop.GameLoopTime + 120_000);
        }
    }
}
