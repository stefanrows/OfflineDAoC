using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.GS.Spells;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>An engine on our side: run by an ally, or unowned and of our realm (abandoned own engines).</summary>
        private static bool FriendlyEngine(GameBot bot, GameSiegeWeapon engine) =>
            engine.Owner is GameLiving owner ? owner == bot || PvpCombatant.AreAllied(bot, owner) : engine.Realm == bot.Realm;

        private string _siegeJobKeep;
        private BotSiegeKind _siegeKind;
        private int _siegeSlot;
        private long _siegeNextAttempt, _siegeNextPosition, _siegeLastHitLog, _siegeLastProgress, _siegeRepairUntil;
        private long _siegeObservedHits;
        private long _siegeRepairCombatTick;
        private Vector3? _siegePosition;
        private GameLiving _siegePositionTarget;
        private GameSiegeWeapon _siegeWeapon, _siegeRepair;
        private string _siegeSupplyItem;
        private int _siegeSupplyCount;
        private long _siegeSupplyStarted, _siegeNextTopup;
        private long _siegeNoTargetSince;
        private Vector3? _siegeMoveTo;
        private long _siegeMoveStarted, _siegeNextMove, _siegeMoveRepath;
        private string _siegePreSupplyKeep;
        private GameNPC[] _siegeScanNpcs;
        private GamePlayer[] _siegeScanPlayers;
        private long _siegeScanUntil, _siegeRideCheck;
        /// <summary>The ram this caster is walking to; keeps the walk going every think.</summary>
        private GameSiegeRam _siegeRideTarget;
        /// <summary>The operator's 6,000-unit scans are reused for this long.</summary>
        private const long SiegeScanReuseMilliseconds = 2_000;

        /// <summary>
        /// Siege work for a force committed to an automatic keep assault (task 48,
        /// siege slice 1): casters ride their group's ram, operators buy a ram at
        /// the hub before the march and place and operate it within 6,000 units
        /// of the keep. Everything else leaves the turn to FindRvrTarget.
        /// </summary>
        private bool TryRunSiegeWork(GameBot bot, bool permitSupply = true, bool permitExecution = true)
        {
            using var profile = BotThinkProfiler.Measure(BotThinkPhase.SiegeJob);
            long now = GameLoop.GameLoopTime;
            if (_rvrIntent != AutonomousRvrEventLayer.Intent.AssaultKeep || !_rvrSharedEvent ||
                _rvrDestination?.Id?.StartsWith("rvr-keep-", StringComparison.Ordinal) != true)
            {
                if (bot.CompanionRam is { } previousRam) StopRiding(bot, previousRam, "no keep assault");
                _siegeRideTarget = null;
                if (_siegeJobKeep != null) ReleaseSiegeJob(bot);
                return false;
            }
            if (!permitSupply && _groupDirective?.IsDynamic == true && _groupDirective.Leader != bot &&
                (_groupDirective.Leader?.IsAlive != true || _groupDirective.Leader.CurrentRegionID != bot.CurrentRegionID ||
                 bot.GetDistanceTo(_groupDirective.Leader) > AutonomousRvrSiegeMuster.RejoinRadius))
                permitExecution = false;
            // A completed purchase is not a completed supply trip until the
            // operator rejoins the rally. Otherwise a six-member quorum could
            // march while its only equipped operator is still at the merchant.
            if (_siegeSupplyItem == null &&
                bot.TempProperties.GetProperty<string>(SiegeSupplyKeepProperty) == _rvrDestination.Id &&
                (_groupDirective?.IsDynamic != true || _groupDirective.Leader == bot ||
                 _groupDirective.Leader is { IsAlive: true } supplyLeader &&
                 supplyLeader.CurrentRegionID == bot.CurrentRegionID &&
                 bot.GetDistanceTo(supplyLeader) <= AutonomousRvrSiegeMuster.PresentRadius))
                bot.TempProperties.RemoveProperty(SiegeSupplyKeepProperty);
            // A reserved operator remains part of the marching formation. A
            // kit purchase is allowed while gathering, but must not send a
            // supplied operator down the keep road ahead of its leader.
            if (_siegeJobKeep == _rvrDestination.Id) AutonomousSiegeJobs.Refresh(bot);
            if (!permitExecution)
            {
                if (!permitSupply) return false;
                if (_siegeSupplyItem != null) return SupplySiegeItem(bot, _siegeSupplyItem, _siegeSupplyCount);
                return PrepareSiegeResponseSupplies(bot, now, allowFrontierSupply: true);
            }
            if (TryRideSiegeRam(bot, now)) return true;
            if (!AutonomousSiegeJobs.Eligible(bot)) return false;
            bool inRegion = bot.CurrentRegionID == _rvrDestination.RegionId;
            double distance = inRegion ? Distance(bot.X, bot.Y, _rvrDestination.X, _rvrDestination.Y) : double.PositiveInfinity;
            // A 2003 group bought its ram at the border keep, not at the gate.
            bool canBuyBeforeMarch = _siegePreSupplyKeep != _rvrDestination.Id &&
                (!IsInFrontier(bot) || PvpCombatant.IsSafeBorderHub(bot.CurrentRegionID, bot.X, bot.Y));
            if (!AutonomousSiegeDoctrine.ShouldRunSiegeJob(_rvrIntent, _rvrSharedEvent, inRegion, distance,
                    _siegeSupplyItem != null, canBuyBeforeMarch)) return false;
            return TryRunSiegeJob(bot, permitSupply);
        }

        /// <summary>Melee classes, and pure casters with no free seat on a ram of
        /// their group near them, work the gate (owner decision 5, 2026-09-28).</summary>
        private static bool CanWorkGate(GameBot bot)
        {
            if (bot?.CharacterClass == null) return false;
            var characterClass = (eCharacterClass)bot.CharacterClass.ID;
            if (AutonomousSiegeDoctrine.CanMeleeDoor(characterClass)) return true;
            if (!AutonomousSiegeDoctrine.CanNukeDoor(characterClass)) return false;
            bool freeSeat = bot.Group != null && bot.Group.GetMembersInTheGroup().OfType<GameBot>().Where(member => member != bot)
                .SelectMany(AutonomousSiegeOwnership.All).OfType<GameSiegeRam>()
                .Any(ram => ram.IsAlive && ram.CurrentRegion == bot.CurrentRegion &&
                    bot.IsWithinRadius(ram, AutonomousSiegeDoctrine.RamBoardSearchRadius) &&
                    ram.TargetObject is GameKeepDoor { IsAlive: true, State: eDoorState.Closed } &&
                    ram.PassengerCount < ram.MAX_PASSENGERS);
            return AutonomousSiegeDoctrine.CanDamageDoor(characterClass, freeSeat);
        }

        private bool TryRideSiegeRam(GameBot bot, long now)
        {
            GameSiegeRam riding = bot.CompanionRam;
            if (riding != null && riding.CompanionRiderSlot(bot) < 0) { bot.CompanionRam = null; riding = null; }
            GameSiegeRam ram = riding;
            if (ram == null)
            {
                if (!AutonomousSiegeDoctrine.RidesRam(bot) || bot.Group == null || bot.InCombat) { _siegeRideTarget = null; return false; }
                // A ram already chosen stays the goal on every think while it is
                // valid; only a fresh search is throttled.
                bool remembered = _siegeRideTarget is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } known &&
                    known.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(known, AutonomousSiegeDoctrine.RamBoardSearchRadius);
                if (!AutonomousSiegeDoctrine.ShouldSearchForRam(remembered, now, _siegeRideCheck)) return false;
                ram = remembered ? _siegeRideTarget : bot.Group.GetMembersInTheGroup().OfType<GameBot>().Where(member => member != bot)
                    .SelectMany(AutonomousSiegeOwnership.All).OfType<GameSiegeRam>()
                    .Where(r => r.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(r, AutonomousSiegeDoctrine.RamBoardSearchRadius))
                    .OrderBy(bot.GetDistanceTo).FirstOrDefault();
                _siegeRideTarget = ram;
                if (ram == null) { _siegeRideCheck = now + 5_000; return false; }
            }
            GameKeepDoor gate = ram.TargetObject as GameKeepDoor;
            bool onGate = ram.IsAlive && ram.ObjectState == GameObject.eObjectState.Active &&
                gate is { IsAlive: true, State: eDoorState.Closed };
            bool sameGroup = ram.Owner is GameBot operatorBot && operatorBot != bot && operatorBot.IsAlive &&
                operatorBot.Group != null && operatorBot.Group == bot.Group;
            bool attacked = bot.GetNPCsInRadius(450).Any(n => n.IsAlive && n.TargetObject == bot && n.IsAttacking) ||
                bot.GetPlayersInRadius(450).Any(p => p.IsAttacking && p.TargetObject == bot);
            if (!AutonomousSiegeDoctrine.ShouldRide(AutonomousSiegeDoctrine.RidesRam(bot), sameGroup, onGate,
                    riding != null, ram.PassengerCount, ram.MAX_PASSENGERS, attacked))
            {
                if (riding != null) StopRiding(bot, riding, attacked ? "attacked in melee" : "gate down or operator gone");
                _siegeRideTarget = null; _siegeRideCheck = now + 5_000;
                return false;
            }
            bot.TempProperties.SetProperty("SiegeJobUntil", now + 15_000);
            if (riding != null)
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                SiegeStatus(bot, "Riding the siege ram", gate);
                return true;
            }
            if (!bot.IsWithinRadius(ram, AutonomousSiegeDoctrine.RamBoardRadius))
            {
                if (!IssuePath(bot, new(ram.X, ram.Y, ram.Z), preciseArrival: true))
                {
                    // No route to this ram: give it up and wait for a fresh search.
                    _siegeRideTarget = null; _siegeRideCheck = now + 5_000;
                    bot.TempProperties.SetProperty("SiegeJobUntil", 0L);
                    return false;
                }
                SiegeStatus(bot, "Moving to ride the siege ram", gate);
                return true;
            }
            _siegeRideTarget = null;
            if (!ram.BoardWorldBot(bot)) { bot.TempProperties.SetProperty("SiegeJobUntil", 0L); _siegeRideCheck = now + 5_000; return false; }
            if (Log.IsInfoEnabled) Log.Info($"RVR_SIEGE action=ride bot={bot.Name} keep={_rvrDestination?.Id} engine={ram.ObjectID} " +
                $"operator={(ram.Owner as GameBot)?.Name} riders={ram.PassengerCount}/{ram.MAX_PASSENGERS}");
            SiegeStatus(bot, "Riding the siege ram", gate);
            return true;
        }

        private void StopRiding(GameBot bot, GameSiegeRam ram, string reason)
        {
            ram.DismountCompanion(bot);
            bot.TempProperties.SetProperty("SiegeJobUntil", 0L);
            if (Log.IsInfoEnabled) Log.Info($"RVR_SIEGE action=dismount bot={bot.Name} keep={_rvrDestination?.Id} engine={ram.ObjectID} reason=\"{reason}\"");
        }

        public const string SiegeSupplyKeepProperty = "SiegeSupplyKeep";

        /// <summary>Started purchase trips finish before departure. Keep the
        /// native melee and allied-engine alternatives: guild-scoped job slots
        /// need not assign a personal ram to every supporting warband.</summary>
        public static bool SiegeSuppliesReadyForMarch(GameBot leader, System.Collections.Generic.IEnumerable<GameBot> members, string keepId)
        {
            return leader?.IsAlive == true && members != null && !members.Any(member => member?.IsAlive == true &&
                member.TempProperties.GetProperty<string>(SiegeSupplyKeepProperty) == keepId);
        }

        public static long SiegeSupplyBudget(bool playerResponse) => playerResponse ? 180_000L : 600_000L;

        private bool PrepareSiegeResponseSupplies(GameBot bot, long now, bool allowFrontierSupply = false)
        {
            // Reserve against the destination, not the origin region: responders
            // from several towns must share one bounded equipment assignment pool.
            // A force already roaming the frontier still needs real equipment.
            // Its designated operators may buy while the force gathers; after
            // departure a missing kit cannot send members away from the march.
            if (!allowFrontierSupply && IsInFrontier(bot) && !PvpCombatant.IsSafeBorderHub(bot.CurrentRegionID, bot.X, bot.Y) ||
                !_rvrSharedEvent || _rvrDestination == null ||
                _siegePreSupplyKeep == _rvrDestination.Id || now < _siegeNextAttempt ||
                !AutonomousSiegeJobs.Eligible(bot) ||
                !AutonomousRvrEventLayer.IsTargetActive(_rvrDestination.Id, now)) return false;
            var keep = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId)
                .FirstOrDefault(k => _rvrDestination.Id == $"rvr-keep-{k.KeepID}" && AutonomousRvrKeepPolicy.IsSiegeObjective(k));
            if (keep == null) return false;
            if (_siegeJobKeep != null && _siegeJobKeep != _rvrDestination.Id) ReleaseSiegeJob(bot);
            // Equipment for the force actually marching (eight: two rams), not a
            // notional army: a full warband brought one or two rams in 2003.
            if (!AutonomousSiegeJobs.TryAcquire(bot, _rvrDestination.Id, Math.Max(1, (int)(bot.Group?.MemberCount ?? 1)),
                keep.Guild == null || keep.Guild != bot.Guild, false,
                out var kind, out var slot, _rvrDestination.RegionId))
            { _siegeNextAttempt = now + 10_000; return false; }
            _siegeJobKeep = _siegePreSupplyKeep = _rvrDestination.Id;
            _siegeKind = kind; _siegeSlot = slot;
            SiegeLog(bot, "response_equipment", null, $"kind={kind} slot={slot}; other responders proceed independently");
            string kit = BotSiegeRuntime.Kit(bot.Realm, kind);
            // Already equipped operators proceed immediately. Repairs are topped
            // up opportunistically, never a prerequisite for marching to battle.
            return BotSiegeRuntime.Item(bot, kit) == null && SupplySiegeItem(bot, kit, 1);
        }

        private static bool CanSupplySiege(GameBot bot)
        {
            if (BotSiegeRuntime.Item(bot,BotSiegeRuntime.Kit(bot.Realm,BotSiegeKind.Ram))!=null) return true;
            var merchant=FindReachableSiegeMerchant(bot);
            long price=merchant?.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>()
                .Where(t=>t.Price>0 && AutonomousSiegePolicy.IsRamKit(bot.Realm,t.Id_nb)).Select(t=>t.Price).DefaultIfEmpty(0).Min() ?? 0;
            bool slot=bot.Inventory!=null && bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack)!=eInventorySlot.Invalid;
            return AutonomousSiegePolicy.CanSupplyRam(false,AutonomousBotEconomy.GetMoney(bot.DatabaseID),price,slot);
        }

        private bool TryRunSiegeJob(GameBot bot, bool permitSupply = true)
        {
            long now = GameLoop.GameLoopTime;
            if (_rvrIntent is not (AutonomousRvrEventLayer.Intent.AssaultKeep or AutonomousRvrEventLayer.Intent.AssaultRelicKeep or AutonomousRvrEventLayer.Intent.DefendEvent))
            { if (_siegeJobKeep!=null) ReleaseSiegeJob(bot); return false; }
            if (_siegeSupplyItem != null)
            {
                if (!permitSupply) return false;
                if (_siegeJobKeep != _rvrDestination?.Id) { ReleaseSiegeJob(bot); return false; }
                AutonomousSiegeJobs.Refresh(bot);
                return SupplySiegeItem(bot,_siegeSupplyItem,_siegeSupplyCount);
            }
            if (permitSupply && PrepareSiegeResponseSupplies(bot, now)) return true;
            if (_rvrDestination?.Id?.StartsWith("rvr-keep-", StringComparison.Ordinal) != true || !AutonomousSiegeJobs.Eligible(bot)) return false;
            if (_siegeJobKeep==_rvrDestination.Id && (bot.CurrentRegionID!=_rvrDestination.RegionId ||
                bot.GetDistanceTo(new Point3D(_rvrDestination.X,_rvrDestination.Y,_rvrDestination.Z))>4800))
            { AutonomousSiegeJobs.Refresh(bot); return TravelRvrObjective(bot,_rvrDestination); }
            if (bot.CurrentRegionID!=_rvrDestination.RegionId) return false;
            var keep = GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).FirstOrDefault(k =>
                _rvrDestination.Id == $"rvr-keep-{k.KeepID}" && AutonomousRvrKeepPolicy.IsSiegeObjective(k));
            if (keep == null || bot.GetDistanceTo(new Point3D(keep.X, keep.Y, keep.Z)) > 6000) return false;
            bool attacking = keep.Guild == null || keep.Guild != bot.Guild;
            GameKeepDoor door = attacking ? keep.Doors.Values.Where(d=>d.IsAlive && d.IsAttackableDoor && d.State==eDoorState.Closed)
                .OrderByDescending(d=>Vector2.DistanceSquared(new(d.X,d.Y),new(keep.X,keep.Y)))
                .ThenBy(bot.GetDistanceTo).FirstOrDefault() : null;
            if (_siegeJobKeep != _rvrDestination.Id)
            {
                ReleaseSiegeJob(bot);
                _siegeJobKeep = _rvrDestination.Id;
            }
            if (now < _siegeNextAttempt) return false;
            if (_siegeMoveTo.HasValue && _siegeWeapon != null)
                return ContinueSiegeMove(bot,now);
            if (_siegeScanNpcs == null || now >= _siegeScanUntil)
            {
                // Bounded local scans, reused for two seconds: an operator
                // thinks every turn, the siege picture does not change that fast.
                _siegeScanNpcs = bot.GetNPCsInRadius(6000).ToArray();
                _siegeScanPlayers = bot.GetPlayersInRadius(6000).ToArray();
                _siegeScanUntil = now + SiegeScanReuseMilliseconds;
            }
            var nearby = _siegeScanNpcs;
            var engines = nearby.OfType<GameSiegeWeapon>().Where(w => w.IsAlive && w.ObjectState == GameObject.eObjectState.Active).ToArray();
            bool enemyEngines = engines.Any(w => BotSiegeRuntime.LegalEnemy(bot, w));
            // Camlann: our side is our guild and its allies, from any realm.
            int present = nearby.OfType<GameBot>().Count(b => b.IsAlive && b.ObjectState == GameObject.eObjectState.Active &&
                    (b == bot || PvpCombatant.AreAllied(bot, b))) + (nearby.Contains(bot) ? 0 : 1) +
                _siegeScanPlayers.Count(p => p.IsAlive && PvpCombatant.AreAllied(bot, p));
            if (!AutonomousSiegeJobs.TryAcquire(bot, _siegeJobKeep, present, attacking && door!=null, enemyEngines, out _siegeKind, out _siegeSlot))
            { _siegeNextAttempt = now + 10_000; return false; }
            if (now>=_siegeNextTopup)
            {
                _siegeNextTopup=now+30_000;
                var merchant=nearby.OfType<GameMerchant>().Where(m=>m.IsWithinRadius(bot,GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
                    .OrderBy(bot.GetDistanceTo).FirstOrDefault();
                int bought=BotSiegeRuntime.TryRestockRepairKits(bot,merchant);
                if (bought>0) SiegeLog(bot,"repair_restock",null,$"count={bought} target={BotSiegeRuntime.RepairRestockTarget}");
            }

            GameLiving target = _siegeKind == BotSiegeKind.Ram ? door : _siegeKind == BotSiegeKind.Ballista ?
                engines.Where(w => BotSiegeRuntime.LegalEnemy(bot, w)).OrderBy(w => w is GameSiegeRam ? 0 : 1).ThenBy(bot.GetDistanceTo).FirstOrDefault() :
                nearby.OfType<GameLiving>().Concat(bot.GetPlayersInRadius(5000)).Where(t =>
                    (BotPvpCrowdControl.PlayerLike(t) || t is GameKeepGuard guard && guard.Component?.Keep==keep && !guard.IsPortalKeepGuard && (door==null || guard is not GuardLord)) &&
                    BotSiegeRuntime.LegalEnemy(bot, t) && !BotPvpCrowdControl.Protected(bot, t) && BotSiegeRuntime.Visible(bot, t))
                    .OrderBy(bot.GetDistanceTo).Take(24)
                    .OrderByDescending(t => nearby.Count(n => n.Realm == t.Realm && n.IsAlive && n.IsWithinRadius(t, 150)))
                    .FirstOrDefault();
            // Keep firing at an already valid target; changing cluster scores
            // must not continually restart aiming or move an effective engine.
            var currentWeapon = AutonomousSiegeOwnership.All(bot).FirstOrDefault();
            if (_siegeKind != BotSiegeKind.Ram && currentWeapon != null && BotSiegeRuntime.Kind(currentWeapon)==_siegeKind &&
                currentWeapon.TargetObject is GameLiving retained && BotSiegeRuntime.CanDamage(currentWeapon,retained) && InSiegeRange(currentWeapon,retained))
                target=retained;
            if (target == null)
            {
                if (_siegeNoTargetSince==0) _siegeNoTargetSince=now;
                // Keep the existing engine in place for brief gaps. Never continually
                // move a working engine after each target dies.
                if (now-_siegeNoTargetSince<60_000 && AutonomousSiegeOwnership.All(bot).FirstOrDefault() is { } waiting)
                { bot.StopMovingOnPath(); bot.StopMoving(); SiegeStatus(bot,"Holding siege position; no valid target",waiting); return true; }
                ReleaseSiegeJob(bot); _siegeNextAttempt=now+30_000; return false;
            }
            _siegeNoTargetSince=0;

            // Prioritize immediate personal defense without chasing distant enemies away from an engine.
            if (bot.GetNPCsInRadius(450).Any(n => n.IsAlive && n.TargetObject == bot && n.IsAttacking) ||
                bot.GetPlayersInRadius(450).Any(p => p.IsAttacking && p.TargetObject == bot)) return false;

            GameSiegeWeapon owned = AutonomousSiegeOwnership.All(bot).FirstOrDefault();
            if (owned != null && (owned.CurrentRegion != bot.CurrentRegion || BotSiegeRuntime.Kind(owned) != _siegeKind)) { owned.ReleaseControl(); owned = null; }
            if (_siegeWeapon != null && !_siegeWeapon.IsAlive) SiegeLog(bot, "destroyed", _siegeWeapon, "replacement requested");
            _siegeWeapon = owned;
            if (owned == null)
            {
                GameSiegeWeapon abandoned = engines.Where(w => FriendlyEngine(bot, w) && w.Owner == null &&
                    BotSiegeRuntime.Kind(w) == _siegeKind && (w.Health > w.DecayedHp || w.TimesRepaired<=3 && BotSiegeRuntime.Item(bot,BotSiegeRuntime.RepairKit)!=null) && InSiegeRange(w, target))
                    .OrderBy(bot.GetDistanceTo).FirstOrDefault();
                if (abandoned != null)
                {
                    if (!bot.IsWithinRadius(abandoned, Math.Max(32, abandoned.SIEGE_WEAPON_CONTROLE_DISTANCE - 20)))
                    { IssuePath(bot, new(abandoned.X, abandoned.Y, abandoned.Z), preciseArrival:true); SiegeStatus(bot, "Reclaiming abandoned siege equipment", target); return true; }
                    bot.StopMovingOnPath(); bot.StopMoving();
                    if (abandoned.TryTakeControl(bot)) { owned = _siegeWeapon = abandoned; _siegeLastProgress = now; SiegeLog(bot, "reclaimed", owned, "native control acquired"); }
                }
            }
            if (owned != null && !InSiegeRange(owned, target))
            {
                // Preserve a legal current target instead of chasing a newly selected one.
                if (owned.TargetObject is GameLiving current && BotSiegeRuntime.LegalEnemy(bot,current) && InSiegeRange(owned,current)) target=current;
                else if (owned.EnableToMove && now >= _siegeNextMove && !owned.InCombat && !bot.InCombat)
                {
                    Vector3? move = ChooseSiegePosition(bot,target,_siegeKind,engines.Where(w=>w!=owned).ToArray());
                    var nav=PathfindingProvider.Instance;
                    if (move.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav,owned.CurrentZone,new(owned.X,owned.Y,owned.Z),move.Value))
                    {
                        owned.SiegeWeaponTimer.Stop(); owned.CurrentState=GameSiegeWeapon.eState.Inactive;
                        _siegeMoveTo=move; _siegeMoveStarted=now; _siegeNextMove=now+120_000; _siegeMoveRepath=0;
                        SiegeLog(bot,"relocating",owned,"no target in firing range; connected route verified");
                        return ContinueSiegeMove(bot,now);
                    }
                }
            }
            if (owned != null && !InSiegeRange(owned,target))
            {
                owned.ReleaseControl(); _siegeWeapon = null; _siegePosition = null;
                _siegeNextAttempt = now + 15_000;
                SiegeLog(bot, "released", owned, "target outside engine range");
                return false;
            }
            if (owned != null && owned.HealthPercent <= 60)
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                if (TrySiegeRepair(bot, owned, now)) return true;
                if (owned.Health <= owned.DecayedHp)
                {
                    owned.ReleaseControl(); owned.TargetObject = null;
                    SiegeLog(bot, "unusable", owned, "no eligible safe repair; replace after cooldown");
                    _siegeWeapon = null; _siegeNextAttempt = now + 15_000; _siegePosition = null; return false;
                }
            }

            string kitId = BotSiegeRuntime.Kit(bot.Realm, _siegeKind);
            if (owned == null && BotSiegeRuntime.Item(bot, kitId) == null)
                return permitSupply && SupplySiegeItem(bot, kitId, 1);
            if (owned == null)
            {
                // Working player engines count too. Broken engines retain their native decay,
                // but do not count as functioning firepower and are never deleted here.
                int active = engines.Count(w => FriendlyEngine(bot, w) && w.Health > w.DecayedHp &&
                    (BotSiegeRuntime.Kind(w) == _siegeKind || _siegeKind is BotSiegeKind.Catapult or BotSiegeKind.Trebuchet && w is GameSiegeCatapult) &&
                    (w is not GameSiegeRam || w.IsWithinRadius(target, 600)));
                if (active >= 2) { _siegeNextAttempt = now + 10_000; return false; }
                if (_siegePositionTarget != target || _siegePosition == null)
                {
                    if (now < _siegeNextPosition) return false;
                    _siegeNextPosition = now + 15_000;
                    _siegePositionTarget = target;
                    _siegePosition = ChooseSiegePosition(bot, target, _siegeKind, engines);
                    if (_siegePosition == null)
                    { SiegeLog(bot, "placement_blocked", null, "no connected firing position"); _siegeNextAttempt = now + 15_000; return false; }
                }
                if (Vector3.Distance(new(bot.X, bot.Y, bot.Z), _siegePosition.Value) > 30)
                { IssuePath(bot, _siegePosition.Value, preciseArrival:true); SiegeStatus(bot, "Moving to verified siege position", target); return true; }
                bot.StopMovingOnPath(); bot.StopMoving();
                lock (AutonomousSiegeOwnership.ChangeGate)
                {
                    // Recheck the shared cap immediately before consuming any kit.
                    if (target.GetNPCsInRadius(_siegeKind == BotSiegeKind.Ram ? (ushort)650 : (ushort)6000)
                        .OfType<GameSiegeWeapon>().Count(w => w.IsAlive && FriendlyEngine(bot, w) && w.Health > w.DecayedHp &&
                            (BotSiegeRuntime.Kind(w) == _siegeKind || _siegeKind is BotSiegeKind.Catapult or BotSiegeKind.Trebuchet && w is GameSiegeCatapult)) >= 2) return false;
                    DbInventoryItem kit = BotSiegeRuntime.Item(bot, kitId);
                    SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
                    Spell spell = kit == null || line == null ? null : SkillBase.FindSpell(kit.SpellID, line);
                    if (spell == null || !ValidSiegeSpell(_siegeKind, spell.SpellType)) { _siegeNextAttempt = now + 30_000; return false; }
                    owned = CreateSiegeWeapon(_siegeKind);
                    owned.CurrentRegion = bot.CurrentRegion; owned.X = bot.X; owned.Y = bot.Y; owned.Z = bot.Z;
                    owned.Heading = bot.Heading; owned.Realm = bot.Realm; owned.ItemId = kit.Id_nb;
                    if (!InSiegeRange(owned, target) || !owned.AddToWorld()) return false;
                    if (!owned.TryTakeControl(bot) || !BotSiegeRuntime.Consume(bot, kitId, 1)) { owned.Delete(); return false; }
                }
                _siegeWeapon = owned; _siegeLastProgress = now; _siegeObservedHits = 0;
                SiegeLog(bot, "deployed", owned, $"kit={kitId}");
            }
            if (!bot.IsWithinRadius(owned, Math.Max(32, owned.SIEGE_WEAPON_CONTROLE_DISTANCE - 20)))
            { IssuePath(bot, new(owned.X, owned.Y, owned.Z), preciseArrival:true); return true; }
            bot.StopMovingOnPath(); bot.StopMoving(); bot.TargetObject = target;
            if (owned.TargetObject != target && !owned.SiegeWeaponTimer.IsAlive)
            { owned.TargetObject = target; owned.CurrentState &= ~GameSiegeWeapon.eState.Aimed; }
            if (!owned.SiegeWeaponTimer.IsAlive)
            {
                if ((owned.CurrentState & GameSiegeWeapon.eState.Armed) == 0) owned.Arm();
                else if ((owned.CurrentState & GameSiegeWeapon.eState.Aimed) == 0) owned.Aim();
                else owned.Fire();
            }
            if (owned.ConfirmedHits != _siegeObservedHits)
            {
                _siegeObservedHits = owned.ConfirmedHits; _siegeLastProgress = now;
                AutonomousStuckWatchdog.MarkProgress(bot,eAutonomousProgressKind.SiegeParticipation);
                if (now >= _siegeLastHitLog) { SiegeLog(bot, "hit", owned, $"hits={owned.ConfirmedHits} targetHp={target.HealthPercent}"); _siegeLastHitLog = now + 60_000; }
            }
            if (now - _siegeLastProgress > 90_000)
            { SiegeLog(bot, "no_progress", owned, "90s without damage; release and retry"); owned.ReleaseControl(); _siegeNextAttempt = now + 30_000; return false; }
            SiegeStatus(bot, $"Siege {owned.CurrentState}: {owned.ShotsFired} shots / {owned.ConfirmedHits} confirmed hits", target);
            return true;
        }

        private static bool ValidSiegeSpell(BotSiegeKind kind, eSpellType type) => (kind, type) is
            (BotSiegeKind.Ram, eSpellType.SummonSiegeRam) or (BotSiegeKind.Catapult, eSpellType.SummonSiegeCatapult) or
            (BotSiegeKind.Trebuchet, eSpellType.SummonSiegeTrebuchet) or (BotSiegeKind.Ballista, eSpellType.SummonSiegeBallista);
        public static GameSiegeWeapon CreateSiegeWeapon(BotSiegeKind kind) => kind switch
        {
            BotSiegeKind.Ram => new GameSiegeRam { Level = 1, Model = 2600, Name = "light siege ram" },
            BotSiegeKind.Catapult => new GameSiegeCatapult { Level = 3 },
            BotSiegeKind.Trebuchet => new GameSiegeTrebuchet { Level = 3 },
            _ => new GameSiegeBallista { Level = 3 }
        };
        public static bool InSiegeRange(GameSiegeWeapon w, GameLiving t) => t != null &&
            (w is GameSiegeRam ? w.GetDistanceTo(t) >= 200 && w.GetDistanceTo(t) <= w.attackComponent.AttackRange - 25 :
                w.GetDistanceTo(t) >= w.MinAttackRange + 50 && w.GetDistanceTo(t) <= w.MaxAttackRange - 100);

        private Vector3? ChooseSiegePosition(GameBot bot, GameLiving target, BotSiegeKind kind, GameSiegeWeapon[] engines)
        {
            // Attackers cannot place a ram/artillery piece by taking a path
            // through the very closed enemy gate they are supposed to breach.
            var nav = AutonomousKeepApproachNavigation.ForBot(PathfindingProvider.Instance,bot);
            if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return null;
            float angle = MathF.Atan2(bot.Y - target.Y, bot.X - target.X);
            float radius = kind == BotSiegeKind.Ram ? 310 : kind == BotSiegeKind.Trebuchet ? 2600 : 1700;
            for (int i = 0; i < 13; i++)
            {
                float offset = i==0 ? 0 : ((i-1) / 2 + 1) * 0.22f * (i % 2 == 0 ? -1 : 1);
                // The gate may stand uphill from the operator. Project near
                // the gate's elevation, then require a full connected route;
                // projecting exclusively at the bot's Z rejects valid ramps.
                Vector3 raw = new(target.X + MathF.Cos(angle + offset) * radius, target.Y + MathF.Sin(angle + offset) * radius, target.Z);
                Vector3? floor = nav.GetClosestPoint(bot.CurrentZone, raw, 48, 48, kind==BotSiegeKind.Ram ? 256 : 1024, nav.DefaultFilters);
                if (!floor.HasValue || engines.Any(w => Vector3.Distance(floor.Value, new(w.X, w.Y, w.Z)) < (kind == BotSiegeKind.Ram ? 210 : 510)) ||
                    !AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, new(bot.X, bot.Y, bot.Z), floor.Value) ||
                    !nav.HasLineOfSight(bot.CurrentZone, floor.Value + new Vector3(0,0,48), new(target.X,target.Y,target.Z+48), nav.DefaultFilters)) continue;
                bool clear = true;
                foreach (var d in new[] { new Vector3(40,0,0),new Vector3(-40,0,0),new Vector3(0,40,0),new Vector3(0,-40,0) })
                {
                    Vector3? edge = nav.GetClosestPoint(bot.CurrentZone, floor.Value + d, 16,16,48,nav.DefaultFilters);
                    if (!edge.HasValue || Math.Abs(edge.Value.Z-floor.Value.Z)>24 || !AutonomousZoneItinerary.HasCompleteCorridor(nav,bot.CurrentZone,floor.Value,edge.Value)) { clear=false; break; }
                }
                if (clear) return floor;
            }
            return null;
        }

        private bool SupplySiegeItem(GameBot bot, string id, int quantity)
        {
            // Never leave a siege just to fetch repair supplies.
            if (id==BotSiegeRuntime.RepairKit) return false;
            long now=GameLoop.GameLoopTime;
            if (_siegeSupplyItem!=id) _siegeSupplyStarted=now;
            bool urgent = AutonomousRvrEventLayer.IsPlayerDefenseResponse(_rvrDestination?.Id, now);
            if (now-_siegeSupplyStarted>SiegeSupplyBudget(urgent))
            { SiegeLog(bot,"supply_timeout",null,"finite purchase trip expired; rejoin warband and retain normal gate-combat alternatives"); ReleaseSiegeJob(bot); _siegeNextAttempt=now+120_000; return false; }
            _siegeSupplyItem=id; _siegeSupplyCount=quantity;
            bot.TempProperties.SetProperty(SiegeSupplyKeepProperty, _rvrDestination?.Id);
            GameMerchant merchant = FindReachableSiegeMerchant(bot);
            if (merchant == null) { SiegeLog(bot,"supply_unavailable",null,"no reachable siege merchant"); ReleaseSiegeJob(bot); _siegeNextAttempt = GameLoop.GameLoopTime + 60_000; return false; }
            DbItemTemplate template = merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().FirstOrDefault(t => t.Id_nb == id && t.Price > 0);
            if (template == null || AutonomousBotEconomy.GetMoney(bot.DatabaseID) < template.Price * quantity)
            { SiegeLog(bot,"supply_unavailable",null,"kit catalog missing or saved copper insufficient"); ReleaseSiegeJob(bot); _siegeNextAttempt = GameLoop.GameLoopTime + 60_000; return false; }
            if (bot.CurrentRegionID != merchant.CurrentRegionID)
            {
                CampDestination previous = _camp;
                try { _camp = new($"siege-supply-{merchant.ObjectID}",merchant.Name,merchant.CurrentZone?.Description ?? "siege merchant",merchant.CurrentRegionID,merchant.X,merchant.Y,merchant.Z,1,false,false); return TravelAcrossRegions(bot); }
                finally { _camp=previous; }
            }
            if (!ApproachSupplyMerchant(bot, merchant)) { SiegeStatus(bot,"Buying siege supplies",merchant); return true; }
            lock (bot.Inventory)
            {
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack,eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid) { SiegeLog(bot,"supply_unavailable",null,"backpack has no free slot"); ReleaseSiegeJob(bot); _siegeNextAttempt=GameLoop.GameLoopTime+60_000; return false; }
                long price=checked(template.Price*quantity);
                DbInventoryItem item=GameInventoryItem.Create(template);
                if (item==null) { ReleaseSiegeJob(bot); _siegeNextAttempt=GameLoop.GameLoopTime+60_000; return false; }
                item.Count=quantity;
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID,price)) return false;
                if (!bot.Inventory.AddItem(slot,item)) { AutonomousBotEconomy.AddMoney(bot.DatabaseID,price); return false; }
                AutonomousBotEconomy.MarkInventoryChanged(bot); bot.MarkAutonomousStateDirty(); AutonomousBotStatusPersistence.Queue(bot,true);
                SiegeLog(bot,"purchase",null,$"item={id} count={quantity} copper={price}");
                _siegeSupplyItem=null;
                // Keep the trip marker until the operator reaches the rally.
            }
            int repairs=BotSiegeRuntime.TryRestockRepairKits(bot,merchant);
            if (repairs>0) SiegeLog(bot,"repair_restock",null,$"count={repairs} target={BotSiegeRuntime.RepairRestockTarget}");
            return true;
        }

        private bool TrySiegeRepair(GameBot bot, GameSiegeWeapon weapon, long now)
        {
            if (_siegeRepair != weapon) { _siegeRepair=weapon; _siegeRepairUntil=0; }
            if (!BotSiegeRuntime.CanRepair(bot,weapon))
            {
                _siegeRepairUntil=0;
                // Our last siege shot sets the ordinary combat timer. Pause
                // firing so it can clear before the timed repair begins.
                // Incoming attacks do not qualify for this safe wait.
                if (weapon.Owner==bot && weapon.IsAlive && weapon.TimesRepaired<=3 && !weapon.InCombat &&
                    !bot.IsCasting && !bot.IsIncapacitated && now-bot.LastAttackedByEnemyTick>10_000 &&
                    BotSiegeRuntime.Item(bot,BotSiegeRuntime.RepairKit)!=null)
                { weapon.SiegeWeaponTimer.Stop(); SiegeStatus(bot,"Pausing fire before safe repair",weapon); return true; }
                return false;
            }
            long combatTick=Math.Max(Math.Max(bot.LastAttackTick,bot.LastAttackedByEnemyTick),Math.Max(weapon.LastAttackTick,weapon.LastAttackedByEnemyTick));
            if (_siegeRepairUntil==0 || _siegeRepairCombatTick!=combatTick)
            { _siegeRepairUntil=now+20_000; _siegeRepairCombatTick=combatTick; weapon.SiegeWeaponTimer.Stop(); }
            if (now<_siegeRepairUntil) { SiegeStatus(bot,"Repairing siege equipment",weapon); return true; }
            lock (AutonomousSiegeOwnership.ChangeGate)
            {
                if (BotSiegeRuntime.CanRepair(bot,weapon) && weapon.Repair(weapon.MaxHealth*15/100,
                    ()=>BotSiegeRuntime.Consume(bot,BotSiegeRuntime.RepairKit,1)))
                { _siegeLastProgress=now; SiegeLog(bot,"repaired",weapon,$"health={weapon.HealthPercent}"); }
            }
            _siegeRepairUntil=0; return true;
        }
        private void ReleaseSiegeJob(GameBot bot)
        {
            bot.TempProperties.RemoveProperty(SiegeSupplyKeepProperty);
            AutonomousSiegeJobs.Release(bot);
            foreach(var w in AutonomousSiegeOwnership.All(bot)) w.ReleaseControl();
            _siegeJobKeep=null; _siegeWeapon=null; _siegePosition=null; _siegeRepair=null; _siegeRepairUntil=0;
            _siegeSupplyItem=null; _siegeNoTargetSince=0;
            _siegeMoveTo=null; _siegeScanNpcs=null; _siegeScanPlayers=null;
        }
        private bool ContinueSiegeMove(GameBot bot,long now)
        {
            var weapon=_siegeWeapon;
            AutonomousSiegeJobs.Refresh(bot);
            if (weapon?.IsAlive!=true || weapon.Owner!=bot || bot.InCombat || weapon.InCombat || now-_siegeMoveStarted>180_000)
            {
                weapon?.StopMovingOnPath(); weapon?.StopMoving(); _siegeMoveTo=null; _siegeNextAttempt=now+15_000;
                SiegeLog(bot,"relocation_stopped",weapon,"combat, control loss or bounded timeout"); return false;
            }
            if (Vector3.Distance(new(weapon.X,weapon.Y,weapon.Z),_siegeMoveTo.Value)<40)
            { weapon.StopMovingOnPath(); weapon.StopMoving(); _siegeMoveTo=null; _siegeLastProgress=now; return true; }
            // Move in short native path segments while the operator stays within
            // control range. No teleports and no through-wall direct WalkTo.
            if (!bot.IsWithinRadius(weapon,30)) { weapon.StopMovingOnPath(); weapon.StopMoving(); }
            else if (now>=_siegeMoveRepath)
            { weapon.PathTo(_siegeMoveTo.Value,20); _siegeMoveRepath=now+3000; }
            if (!bot.IsWithinRadius(weapon,15))
                bot.PathTo(new Vector3(weapon.X,weapon.Y,weapon.Z),bot.MaxSpeed);
            SiegeStatus(bot,"Relocating siege equipment along checked route",weapon); return true;
        }
        private void SiegeStatus(GameBot bot,string status,GameLiving target) => SetRvrStatus(bot,status,target.Name,"Real siege equipment, finite inventory and native combat timers");
        private void SiegeLog(GameBot bot,string action,GameSiegeWeapon weapon,string detail)
        { if(Log.IsInfoEnabled) Log.Info($"RVR_SIEGE action={action} bot={bot.Name} keep={_siegeJobKeep} engine={weapon?.ObjectID} {detail}"); }
    }
}
