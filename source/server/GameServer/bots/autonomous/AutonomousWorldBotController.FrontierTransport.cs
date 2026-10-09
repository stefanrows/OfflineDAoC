using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Scripts;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private OFTeleporter _frontierPorter;
    private GameMerchant _medallionMerchant;
    private long _nextPorterSearch;
    private Vector3? _frontierWaitingPoint;
    private long _nextWaitingPointSearch;

    private bool TryReturnFromForeignFrontier(GameBot bot)
    {
        // PvE/services cannot walk through another realm's border gates to get
        // home. Use the same real porter/ticket boarding used by outbound RvR.
        ushort home = bot.Realm switch { eRealm.Albion => 1, eRealm.Midgard => 100, eRealm.Hibernia => 200, _ => 0 };
        if (home != 0 && bot.CurrentRegionID == home &&
            AutonomousObjectiveAssignments.CompletePostRvrReturn(bot.PersistentRecord))
        {
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot);
        }
        bool activePveGroup = _groupDirective?.IsDynamic == true &&
            _groupDirective.ObjectiveKind == eAutonomousObjectiveKind.GroupPve;
        if (AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(
                bot.PersistentRecord, activePveGroup))
            return false;
        if (home == 0 || bot.CurrentRegionID == home || bot.CurrentRegionID is not (1 or 100 or 200) ||
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) == eAutonomousObjectiveKind.RvR ||
            GameRelic.IsPlayerCarryingRelic(bot)) return false;
        var passage = AutonomousFrontierTransport.Destination(bot.Realm, home);
        return passage != null && TryFrontierTransport(bot, new("return-home", passage.Location.Name, "frontier",
            home, passage.Location.X, passage.Location.Y, passage.Location.Z, bot.Level, false, true));
    }

    private static void ClearUnrelatedFrontierRequest(GameBot bot, AutonomousRvrEventLayer.Plan plan)
    {
        if (SiegeSupplyRegion(bot) != 0) return;
        var pending = bot.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey);
        if (pending == null) return;
        bool rejoiningLeader = bot.Group?.LivingLeader is GameBot leader && leader != bot &&
            pending.TargetRegion == leader.CurrentRegionID;
        if (bot.CurrentRegionID == plan.RegionId ||
            pending.Passage.Region != plan.RegionId && pending.TargetRegion != plan.RegionId && !rejoiningLeader)
            bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
    }

    private bool TryFrontierTransport(GameBot bot, CampDestination destination)
    {
        using var profile = BotThinkProfiler.Measure(BotThinkPhase.RvrFrontierTransport);
        if (bot.CurrentRegionID == destination.RegionId || bot.CurrentRegionID is not (1 or 100 or 200) ||
            destination.RegionId is not (1 or 100 or 200) || GameRelic.IsPlayerCarryingRelic(bot))
        {
            // No crossing wanted any more: drop the pending passage. Warband
            // members follow a leader's pending passage, so a stale one sent
            // them across while the leader stayed, then back to the leader,
            // all day long (bug 76).
            bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            return false;
        }
        // A failed porter lets the RvR crossing search fall back to the
        // dungeon road for a minute, so no force is stranded (last resort).
        bool Unavailable()
        {
            bot.TempProperties.SetProperty(AutonomousFrontierTransport.PorterUnavailableKey, GameLoop.GameLoopTime + 60_000);
            return false;
        }
        if (AutonomousFrontierTransport.Destination(bot.Realm,destination.RegionId) == null) return Unavailable();
        // A full backpack must not cancel every member's approach before the
        // owner can reach the real merchant and sell ordinary vendor trash.
        var leaderRequest = SiegeSupplyRegion(bot) == 0 &&
            bot.Group?.LivingLeader is GameBot leader && leader != bot && SiegeSupplyRegion(leader) == 0 &&
            leader.CurrentRegion == bot.CurrentRegion
            ? leader.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey) : null;
        bool sharedPorterChanged = leaderRequest?.Porter is { ObjectState: GameObject.eObjectState.Active } &&
            leaderRequest.Porter.CurrentRegion == bot.CurrentRegion && leaderRequest.Porter != _frontierPorter;
        if (sharedPorterChanged || GameLoop.GameLoopTime >= _nextPorterSearch ||
            _frontierPorter?.ObjectState != GameObject.eObjectState.Active || _frontierPorter.CurrentRegion != bot.CurrentRegion)
        {
            if (!sharedPorterChanged && GameLoop.GameLoopTime < _nextPorterSearch) return Unavailable();
            _nextPorterSearch = GameLoop.GameLoopTime + 30_000;
            var selected = AutonomousFrontierTransport.WarbandPorter(bot);
            if (selected != _frontierPorter)
            {
                _frontierPorter = selected;
                _medallionMerchant = null;
                _frontierWaitingPoint = null;
                _nextWaitingPointSearch = 0;
            }
        }
        if (_frontierPorter == null) return Unavailable();
        // Albion/Midgard portal keeps in a foreign frontier sell only the home
        // medallion: port home first, then onward (two hops).
        // A warband takes the passage of its leader's realm so it boards and lands
        // together (bug 75); a member falls back to its own only when this porter
        // cannot serve the leader's.
        bool Sells(string medallion) => AutonomousFrontierTransport.PorterSells(_frontierPorter, medallion);
        bool Holds(string medallion) => AutonomousFrontierTransport.Ticket(bot, new(0, medallion, null)) != null;
        // Use the actual porter's native landing. A shared leader request owns
        // intermediate home hops as well as direct siege passages.
        bool rvrTransport = AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR);
        var sharedRequest = rvrTransport
            ? AutonomousFrontierTransport.SharedRequest(bot, _frontierPorter, destination.RegionId) : null;
        eRealm passageRealm = rvrTransport ? _frontierPorter.Realm : AutonomousFrontierTransport.PassageRealm(bot);
        var passage = sharedRequest?.Passage ?? AutonomousFrontierTransport.ChoosePassage(
            passageRealm, bot.CurrentRegionID, destination.RegionId, Sells, Holds);
        if (passage == null) return Unavailable();
        bool returningToPve = passage.Medallion == "home_necklace" &&
            AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) != eAutonomousObjectiveKind.RvR;
        var party=AutonomousFrontierTransport.BoardingParty(bot, passage);
        if(party.Any(GameRelic.IsPlayerCarryingRelic))
        {
            foreach(var member in party) member.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            return Unavailable();
        }
        // Declare the transport intent before approaching its merchant. A
        // returning PvE bot may need its own portal-keep door to BUY the ticket.
        // Boarding still requires the real purchased medallion and all safety checks.
        bot.TempProperties.SetProperty(AutonomousFrontierTransport.RequestKey,new AutonomousFrontierTransport.Request(
            _frontierPorter, passage, AutonomousFrontierTransport.TransportForceId(bot),
            sharedRequest?.TargetRegion is > 0 ? sharedRequest.TargetRegion : destination.RegionId));
        if (AutonomousFrontierTransport.Ticket(bot,passage) == null)
        {
            if (_medallionMerchant?.ObjectState != GameObject.eObjectState.Active ||
                !_medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Any(item=>item.Id_nb==passage.Medallion))
                _medallionMerchant = _frontierPorter.GetNPCsInRadius(3000).OfType<GameMerchant>()
                    .Where(npc => npc.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item=>item.Id_nb==passage.Medallion)==true)
                    .OrderBy(_frontierPorter.GetDistanceTo).FirstOrDefault();
            if (_medallionMerchant == null) return Unavailable();
            if (!ApproachSupplyMerchant(bot, _medallionMerchant))
            {
                SetRvrStatus(bot,"Collecting frontier medallion",destination.MonsterName,$"Walking to {_medallionMerchant.Name} for {passage.Medallion}");
                return true;
            }
            var template = _medallionMerchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().First(item=>item.Id_nb==passage.Medallion);
            var slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid)
            {
                // A full backpack after RvR must not force a walk through enemy
                // gates. Sell one ordinary disposable item at this real merchant;
                // never delete an item or grant a free ticket/teleport.
                var trash = AutonomousBotEconomy.FindVendorTrashCandidate(bot);
                // Nothing it may sell: never loop here (56 bots did, bug 76);
                // the legal dungeon road remains.
                if (trash == null)
                    return Unavailable();
                AutonomousBotEconomy.TrySellToVendor(bot, _medallionMerchant, trash, out _);
                SetRvrStatus(bot, "Making room for frontier medallion", destination.MonsterName,
                    "At the real merchant; preserving equipped items and protected upgrades");
                return true;
            }
            if (slot==eInventorySlot.Invalid || template.Price<0 || template.Price>0 && !AutonomousBotEconomy.TrySpend(bot.DatabaseID,template.Price))
                return Unavailable(); // Existing legal dungeon itinerary remains available.
            var ticket = GameInventoryItem.Create(template);
            if (ticket == null || !bot.Inventory.AddItem(slot,ticket))
            {
                if(template.Price>0) AutonomousBotEconomy.AddMoney(bot.DatabaseID,template.Price);
                return Unavailable();
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            AutonomousBotStatusPersistence.Queue(bot,true);
        }
        // Warbands, siege responders included, muster up to a minute and then
        // board together; defenders receive a bounded priority slice.
        if (!_frontierWaitingPoint.HasValue && GameLoop.GameLoopTime >= _nextWaitingPointSearch)
        {
            _nextWaitingPointSearch = GameLoop.GameLoopTime + 10_000;
            if (AutonomousFrontierTransport.TryWaitingPoint(bot, _frontierPorter, out var waiting))
                _frontierWaitingPoint = waiting;
        }
        if (!_frontierWaitingPoint.HasValue)
        {
            SetRvrStatus(bot,"Finding teleporter waiting space",destination.MonsterName,"Waiting for a reachable spot clear of the teleporter");
            return true;
        }
        // Solos only for their own release hold; warbands for the regroup window.
        bool regrouping = passage.Medallion != "home_necklace" &&
            AutonomousFrontierTransport.RecentRelease(bot, GameLoop.GameLoopTime) is long released &&
            (party.Length > 1 || GameLoop.GameLoopTime - released < AutonomousFrontierTransport.ReleaseHoldMilliseconds);
        if (Vector3.Distance(new(bot.X,bot.Y,bot.Z),_frontierWaitingPoint.Value) > 40)
            IssuePath(bot,_frontierWaitingPoint.Value);
        else
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            // Freshly released: sit and recover while the warband regroups.
            if (regrouping && !BotRestRecovery.BlocksRest(bot) &&
                !AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0))
                bot.BeginRecoveryRest();
        }
        AutonomousFrontierTransport.WakeBoardingPorter(bot,
            bot.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey));
        if (regrouping)
        {
            SetRvrStatus(bot,"Regrouping at the teleporter",destination.MonsterName,
                $"At {_frontierPorter.Name}; released recently: recovering and waiting for the warband before porting to {passage.Location.Name}");
            return true;
        }
        SetRvrStatus(bot,"Boarding frontier teleporter",destination.MonsterName,
            AutonomousFrontierTransport.HasDefenderPriority(bot, passage)
                ? $"At {_frontierPorter.Name}; priority defense departure to {passage.Location.Name}"
                : AutonomousFrontierTransport.HasCommittedSiegePassage(bot, passage)
                    ? $"At {_frontierPorter.Name}; boarding with the warband (up to a minute for stragglers) for the siege at {passage.Location.Name}"
                    : $"At {_frontierPorter.Name}; waiting for the warband and the native departure to {passage.Location.Name}");
        return true;
    }
}
