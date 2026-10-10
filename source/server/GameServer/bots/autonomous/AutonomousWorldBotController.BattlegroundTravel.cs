using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Scripts;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private OFTeleporter _battlegroundPorter;
    private GameMerchant _battlegroundMerchant;
    private Vector3? _battlegroundWaitingPoint;
    private long _nextBattlegroundPorterSearch;

    /// <summary>
    /// An assigned participant walks to the nearest porter, buys the free
    /// battlegrounds medallion from a real merchant (or equips one it already
    /// holds), equips it in the Mythical slot as a player does, then waits at
    /// the porter for the cast that moves it into the campaign. Returns false
    /// when the ordinary RvR plan should decide this turn.
    /// </summary>
    private bool TryBattlegroundTravel(GameBot bot)
    {
        if (bot.IsPlayerLedGroup || bot.IsTemporaryGroupHelper || !bot.IsAlive || bot.InCombat || bot.IsAttacking ||
            bot.IsStunned || bot.IsMezzed || (bot.Brain as BotBrain)?.HasAggro == true ||
            GameRelic.IsPlayerCarryingRelic(bot) || !AutonomousBattlegroundParticipation.IsFrontierRegion(bot.CurrentRegionID))
            return false;
        long now = GameLoop.GameLoopTime;
        if (now >= _nextBattlegroundPorterSearch || _battlegroundPorter is not { ObjectState: GameObject.eObjectState.Active } ||
            _battlegroundPorter.CurrentRegion != bot.CurrentRegion)
        {
            _nextBattlegroundPorterSearch = now + 30_000;
            // Only a porter with a medallion merchant in reach is a legal source for this bot.
            OFTeleporter selected = AutonomousBattlegroundParticipation.SourcePorters(bot.CurrentRegionID)
                .OrderBy(bot.GetDistanceTo).FirstOrDefault();
            if (selected != _battlegroundPorter)
            {
                _battlegroundPorter = selected;
                _battlegroundMerchant = null;
                _battlegroundWaitingPoint = null;
            }
        }
        OFTeleporter porter = _battlegroundPorter;
        if (porter == null) return false;

        // Walking to, buying from or equipping the medallion is this turn's whole job.
        if (!AutonomousBattlegroundParticipation.HasBattlegroundMedallion(bot))
            return AcquireBattlegroundMedallion(bot, porter);

        if (!_battlegroundWaitingPoint.HasValue && AutonomousFrontierTransport.TryWaitingPoint(bot, porter, out Vector3 waiting))
            _battlegroundWaitingPoint = waiting;
        if (!_battlegroundWaitingPoint.HasValue)
        {
            SetRvrStatus(bot, "Finding teleporter waiting space", "Battleground campaign",
                "Waiting for a reachable spot clear of the teleporter", porter.Name);
            return true;
        }
        Vector3 point = _battlegroundWaitingPoint.Value;
        if (Vector3.Distance(new(bot.X, bot.Y, bot.Z), point) > 40)
            IssuePath(bot, point);
        else
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        // The porter's own cast cycle is the departure; waking it is what a passing bot does.
        porter.OnAutonomousBotNearby();
        SetRvrStatus(bot, "Boarding battleground teleporter", "Battleground campaign",
            $"At {porter.Name}; waiting for the battleground departure", porter.Name);
        return true;
    }

    /// <summary>
    /// Buys the free medallion from the real merchant beside the porter, as a
    /// player does, or equips a copy already bought. True while this turn did
    /// something toward owning the medallion; false when no legal source exists.
    /// </summary>
    private bool AcquireBattlegroundMedallion(GameBot bot, OFTeleporter porter)
    {
        const string medallion = AutonomousBattlegroundParticipation.BattlegroundMedallionId;
        // The porter reads only the Mythical slot, so a different necklace there blocks the step.
        if (bot.Inventory.GetItem(eInventorySlot.Mythical) != null) return false;
        DbInventoryItem owned = bot.Inventory.AllItems.FirstOrDefault(item => item.Id_nb == medallion && item.Count > 0 &&
            item.SlotPosition >= (int)eInventorySlot.FirstBackpack && item.SlotPosition <= (int)eInventorySlot.LastBackpack);
        if (owned != null)
        {
            if (!bot.Inventory.MoveItem((eInventorySlot)owned.SlotPosition, eInventorySlot.Mythical, 1)) return false;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }
        if (_battlegroundMerchant?.ObjectState != GameObject.eObjectState.Active ||
            !_battlegroundMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == medallion))
            _battlegroundMerchant = porter.GetNPCsInRadius(AutonomousBattlegroundParticipation.MerchantReach).OfType<GameMerchant>()
                .Where(npc => npc.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == medallion) == true)
                .OrderBy(porter.GetDistanceTo).FirstOrDefault();
        GameMerchant merchant = _battlegroundMerchant;
        if (merchant == null) return false;
        if (!ApproachSupplyMerchant(bot, merchant))
        {
            SetRvrStatus(bot, "Collecting battleground medallion", "Battleground campaign",
                $"Walking to {merchant.Name} for {medallion}", merchant.Name);
            return true;
        }
        DbItemTemplate template = merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().First(item => item.Id_nb == medallion);
        eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
        if (slot == eInventorySlot.Invalid)
        {
            // A full backpack sells one ordinary disposable item at this real merchant;
            // nothing is deleted and no free ticket is granted.
            var trash = AutonomousBotEconomy.FindVendorTrashCandidate(bot);
            if (trash == null) return false;
            AutonomousBotEconomy.TrySellToVendor(bot, merchant, trash, out _);
            SetRvrStatus(bot, "Making room for battleground medallion", "Battleground campaign",
                "At the real merchant; preserving equipped items and protected upgrades", merchant.Name);
            return true;
        }
        if (template.Price < 0 || template.Price > 0 && !AutonomousBotEconomy.TrySpend(bot.DatabaseID, template.Price)) return false;
        var item = GameInventoryItem.Create(template);
        if (item == null || !bot.Inventory.AddItem(slot, item))
        {
            if (template.Price > 0) AutonomousBotEconomy.AddMoney(bot.DatabaseID, template.Price);
            return false;
        }
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        AutonomousBotStatusPersistence.Queue(bot, true);
        if (!bot.Inventory.MoveItem(slot, eInventorySlot.Mythical, 1)) return false;
        return true;
    }
}
