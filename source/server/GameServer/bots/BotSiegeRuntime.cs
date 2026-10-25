using System;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    public enum BotSiegeKind { Ram, Catapult, Trebuchet, Ballista }

    /// <summary>Bot-only native siege adapter. Real equipment purchases; ammunition-free artillery by design.</summary>
    public static class BotSiegeRuntime
    {
        public const string RepairKit = "offline_siege_field_repair_kit";
        // Inventory packets encode generic stack counts as one unsigned byte.
        public const int RepairStackLimit = byte.MaxValue;
        public const int RepairRestockTarget = 5;
        public static bool IsSupply(string id) => id == RepairKit ||
            id is "deploy_siege_ram" or "deploy_siege_ram2" or "deploy_siege_ram3" or
                "deploy_siege_catapult" or "deploy_siege_catapult2" or "deploy_siege_catapult3" or
                "deploy_siege_trebuchet" or "deploy_siege_trebuchet2" or "deploy_siege_trebuchet3" or
                "deploy_siege_ballista" or "deploy_siege_ballista2" or "deploy_siege_ballista3";
        public static string Kit(eRealm realm, BotSiegeKind kind) => "deploy_siege_" + kind.ToString().ToLowerInvariant() +
            (realm == eRealm.Midgard ? "2" : realm == eRealm.Hibernia ? "3" : "");
        public static BotSiegeKind Kind(GameSiegeWeapon w) => w is GameSiegeRam ? BotSiegeKind.Ram :
            w is GameSiegeTrebuchet ? BotSiegeKind.Trebuchet : w is GameSiegeBallista ? BotSiegeKind.Ballista : BotSiegeKind.Catapult;

        [GameServerStartedEvent]
        public static void SupplyCatalog(DOLEvent e, object sender, EventArgs args)
        {
            // Explicitly requested bot repair alternative; no ammo or crafting-skill grants.
            DbItemTemplate repair = GameServer.Database.FindObjectByKey<DbItemTemplate>(RepairKit);
            if (repair == null)
            {
                repair = new DbItemTemplate { Id_nb=RepairKit,Name="field siege repair kit",Level=1,Model=520,
                    Price=50_000,PackSize=1,MaxCount=RepairStackLimit,Weight=10,IsPickable=true,IsDropable=true,IsTradable=true };
                GameServer.Database.AddObject(repair);
            }
            else if (repair.MaxCount != RepairStackLimit)
            { repair.MaxCount=RepairStackLimit; GameServer.Database.SaveObject(repair); }
            AddSupplyToMerchants(repair);
        }

        private static void AddSupplyToMerchants(DbItemTemplate template)
        {
            foreach (GameMerchant merchant in WorldMgr.GetAllRegions().Where(r => r != null).SelectMany(r => r.Merchants))
                if (merchant.TradeItems != null && new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Any(realm => AutonomousSiegePolicy.IsSiegeMerchant(realm, merchant.TradeItems.ItemsListID)) &&
                    !merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(t => t.Id_nb == template.Id_nb))
                    merchant.TradeItems.AddTradeItem(0, eMerchantWindowSlot.FirstEmptyInPage, template);
        }

        public static DbInventoryItem Item(GameBot bot, string id) => id == null ? null : bot?.Inventory?.AllItems.FirstOrDefault(i =>
            i.Count > 0 && i.Id_nb == id && i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack);

        public static int RepairRestockAmount(int held, long money, long unitPrice) => unitPrice<=0 ? 0 :
            (int)Math.Min(Math.Max(0,RepairRestockTarget-held),Math.Max(0,money)/unitPrice);

        // Opportunistic only: this never assigns a destination or interrupts a fight.
        public static int TryRestockRepairKits(GameBot bot, GameMerchant merchant)
        {
            if (bot?.IsAutonomousWorldBot!=true || bot.Inventory==null || bot.InCombat || bot.IsAttacking ||
                merchant?.TradeItems==null || merchant.CurrentRegion!=bot.CurrentRegion ||
                !merchant.IsWithinRadius(bot,GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE) ||
                merchant.Realm!=eRealm.None && merchant.Realm!=bot.Realm) return 0;
            DbItemTemplate template=merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().FirstOrDefault(t=>t.Id_nb==RepairKit && t.Price>0);
            if (template==null) return 0;
            lock (bot.Inventory)
            {
                var stacks=bot.Inventory.AllItems.Where(i=>i.Id_nb==RepairKit && i.Count>0 &&
                    i.SlotPosition >= (int)eInventorySlot.FirstBackpack && i.SlotPosition <= (int)eInventorySlot.LastBackpack).ToArray();
                int count=RepairRestockAmount(stacks.Sum(i=>i.Count),AutonomousBotEconomy.GetMoney(bot.DatabaseID),template.Price);
                if (count==0) return 0;
                DbInventoryItem stack=stacks.FirstOrDefault(i=>i.MaxCount-i.Count>=count);
                var slot=bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
                if (stack==null && slot==eInventorySlot.Invalid) return 0;
                DbInventoryItem item=stack==null ? GameInventoryItem.Create(template) : null;
                if (stack==null && item==null) return 0;
                if (item!=null) item.Count=count;
                long cost=checked(template.Price*count);
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID,cost)) return 0;
                bool added=stack!=null ? bot.Inventory.AddCountToStack(stack,count) : bot.Inventory.AddItem(slot,item);
                if (!added) { AutonomousBotEconomy.AddMoney(bot.DatabaseID,cost); return 0; }
                AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot,true);
                return count;
            }
        }

        public static bool Consume(GameBot bot, string id, int count)
        {
            if (bot?.Inventory == null || count <= 0) return false;
            lock (bot.Inventory)
            {
                DbInventoryItem item = Item(bot, id);
                if (item == null || item.Count < count) return false;
                if (!bot.Inventory.RemoveCountFromStack(item, count)) return false;
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                return true;
            }
        }

        public static bool LegalEnemy(GameLiving owner, GameLiving target) => owner?.IsAlive == true && target?.IsAlive == true &&
            target.ObjectState == GameObject.eObjectState.Active && owner.CurrentRegion == target.CurrentRegion &&
            (PvpCombatant.IsPlayerShaped(target) || target is GameSiegeWeapon || target is GameKeepGuard || target is GameKeepDoor) &&
            GameServer.ServerRules.IsAllowedToAttack(owner, target, true);

        public static bool CanDamage(GameSiegeWeapon weapon, GameLiving target)
        {
            if (weapon?.Owner is not GameBot bot || !weapon.IsAlive || weapon.ObjectState != GameObject.eObjectState.Active ||
                weapon.IsMoving || !weapon.CanUse() || !LegalEnemy(bot, target) || BotPvpCrowdControl.Protected(bot, target)) return false;
            int range = weapon.GetDistanceTo(target);
            if (weapon is GameSiegeRam)
                return target is GameKeepDoor { State: eDoorState.Closed } && range <= weapon.attackComponent.AttackRange;
            if (range < weapon.MinAttackRange || range > weapon.MaxAttackRange) return false;
            // RvR world (upstream 0.38c, port session S2): catapults and trebuchets lob onto a ground target in an arc, as
            // players used them: no line of sight. A ballista fires directly: it needs a clear shot, or a target standing up
            // on a wall. Camlann keeps the line-of-sight rule for every engine.
            if (WorldMode.IsRvr)
            {
                if (weapon is GameSiegeCatapult) return true;
                return target.Z - weapon.Z > 150 || Visible(weapon, target);
            }
            return Visible(weapon, target);
        }

        /// <summary>Highest reach of a ranged bot at a target above it (bow and spell range).</summary>
        public const int ShootUpRange = 1_500;

        /// <summary>
        /// Whether a bot may take this target: <see cref="Visible"/> (the navmesh ray, which is a walkability test), or for
        /// a ranged bot a target up on a wall it stands below (owner report 2026-10-17: defenders on the battlements were
        /// never attacked, the ray stops at the wall's foot).
        /// </summary>
        public static bool CanEngage(GameBot bot, GameLiving target) =>
            Visible(bot, target) || AutonomousRvrDefense.IsRangedDefender(bot) && ShootsUpAt(bot, target);

        /// <summary>
        /// A target 120-1,000 above the source within <see cref="ShootUpRange"/>, and the ground from the source to the
        /// foot of the wall under the target (250 short of it, on the source's side) is open.
        /// </summary>
        public static bool ShootsUpAt(GameLiving source, GameLiving target)
        {
            if (source?.CurrentZone == null || target == null) return false;
            int rise = target.Z - source.Z;
            if (rise < 120 || rise > 1_000 || !source.IsWithinRadius(target, ShootUpRange)) return false;
            double dx = source.X - target.X, dy = source.Y - target.Y;
            double flat = Math.Sqrt(dx * dx + dy * dy);
            if (flat < 1) return false;
            double back = Math.Min(250, flat);
            var foot = new Point3D((int)Math.Round(target.X + dx / flat * back), (int)Math.Round(target.Y + dy / flat * back), source.Z);
            var nav = PathfindingProvider.Instance;
            return nav.IsAvailable && nav.HasNavmesh(source.CurrentZone) &&
                nav.HasLineOfSight(source.CurrentZone, new(source.X, source.Y, source.Z + 48), new(foot.X, foot.Y, foot.Z + 48), nav.DefaultFilters);
        }

        public static bool Visible(GameLiving source, GameLiving target)
        {
            var nav = PathfindingProvider.Instance;
            return source?.CurrentZone != null && target != null && nav.IsAvailable && nav.HasNavmesh(source.CurrentZone) &&
                nav.HasLineOfSight(source.CurrentZone, new(source.X, source.Y, source.Z + 48),
                    new(target.X, target.Y, target.Z + 48), nav.DefaultFilters);
        }

        public static bool CanRepair(GameBot bot, GameSiegeWeapon w) => bot?.IsAlive == true && w?.IsAlive == true &&
            w.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == w.CurrentRegion && w.MayUseSiegeWeapon(bot) &&
            w.Owner == bot && !bot.InCombat && !w.InCombat && !w.IsMoving && !bot.IsMoving && !bot.IsCasting && !bot.IsIncapacitated && w.TimesRepaired <= 3 &&
            bot.IsWithinRadius(w, WorldMgr.INTERACT_DISTANCE) && Item(bot, RepairKit) != null;

        public static bool HoldingPosition(GameBot bot) => AutonomousSiegeOwnership.All(bot).Any(w =>
            bot.IsWithinRadius(w, w.SIEGE_WEAPON_CONTROLE_DISTANCE));
        public static bool Assigned(GameBot bot) => bot?.TempProperties.GetProperty<long>("SiegeJobUntil",0) > GameLoop.GameLoopTime;
    }
}
