using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.GS.Spells;

namespace DOL.GS
{
    // RvR world (Realm-1): the siege floor of the upstream layer (shadowofze/OfflineDAoC 0.38c,
    // AutonomousWorldBotController.Siege.cs TryRunSiegeJob and its placement and target helpers), copied verbatim for port
    // session S2 (docs/RVR_PORT_MAP.md): rams at the outer gate, then the keep door; door order by the door number (relic
    // keeps' doors 10 and 11); posterns last; two rams, the trebuchet and a catapult per army; synthetic engines set up on
    // the spot (no shopping trips); a heavy ram crewed by the bots standing at it; catapults and trebuchets arc onto their
    // target (no line of sight), ballistas need a clear shot or a target up on a wall. ExecuteRvrRealm calls
    // TryRunSiegeJobRealm; the Camlann world keeps the fork's siege doctrine (Siege.cs: ram riders, kit purchases).
    // Local changes are marked "Fork:": friend and foe through the realm rule (map section 4b), the fork's keep approach
    // navigation (AutonomousKeepApproachNavigation.ForBot) and the shared fork helpers (SupplySiegeItem, ReleaseSiegeJob,
    // ContinueSiegeMove, TrySiegeRepair, SiegeLog), which upstream has in the same shape.
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>Siege crews get their engines on site (owner 2026-10-07: skips long shopping trips); the pre-march
        /// kit purchase is kept behind this switch.</summary>
        public const bool SyntheticSiegeEquipment = true;

        private bool PrepareSiegeResponseSuppliesRealm(GameBot bot, long now)
        {
            // Reserve against the destination, not the origin region: responders
            // from several towns must share one bounded equipment assignment pool.
            if (IsInFrontier(bot) || !_rvrSharedEvent || _rvrDestination == null ||
                _siegePreSupplyKeep == _rvrDestination.Id || now < _siegeNextAttempt ||
                !AutonomousSiegeJobs.Eligible(bot) ||
                !AutonomousRvrEventLayer.IsTargetActive(_rvrDestination.Id, now) || SyntheticSiegeEquipment) return false;
            var keep = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId)
                .FirstOrDefault(k => _rvrDestination.Id == $"rvr-keep-{k.KeepID}" && AutonomousRvrKeepPolicy.IsSiegeObjective(k));
            if (keep == null) return false;
            if (_siegeJobKeep != null && _siegeJobKeep != _rvrDestination.Id) ReleaseSiegeJob(bot);
            bool attackingKeep = AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm); // Fork: keep rule (map 4b)
            if (!AutonomousSiegeJobs.TryAcquire(bot, _rvrDestination.Id, 64, attackingKeep, !attackingKeep,
                out var kind, out var slot, _rvrDestination.RegionId, CarriedSiegeKind(bot)))
            { _siegeNextAttempt = now + 10_000; return false; }
            _siegeJobKeep = _siegePreSupplyKeep = _rvrDestination.Id;
            _siegeKind = kind; _siegeSlot = slot;
            SiegeLog(bot, "response_equipment", null, $"kind={kind} slot={slot}; other responders proceed independently");
            string kit = BotSiegeRuntime.Kit(bot.Realm, kind);
            // Already equipped operators proceed immediately. Repairs are topped
            // up opportunistically, never a prerequisite for marching to battle.
            return BotSiegeRuntime.Item(bot, kit) == null && SupplySiegeItem(bot, kit, kind == BotSiegeKind.Ram ? 2 : 1);
        }

        private bool TryRunSiegeJobRealm(GameBot bot)
        {
            long now = GameLoop.GameLoopTime;
            if (_rvrIntent is not (AutonomousRvrEventLayer.Intent.AssaultKeep or AutonomousRvrEventLayer.Intent.AssaultRelicKeep or AutonomousRvrEventLayer.Intent.DefendEvent))
            { if (_siegeJobKeep!=null) ReleaseSiegeJob(bot); return false; }
            if (_siegeSupplyItem != null)
            {
                if (_siegeJobKeep != _rvrDestination?.Id) { ReleaseSiegeJob(bot); return false; }
                AutonomousSiegeJobs.Refresh(bot);
                return SupplySiegeItem(bot,_siegeSupplyItem,_siegeSupplyCount);
            }
            if (PrepareSiegeResponseSuppliesRealm(bot, now)) return true;
            if (_rvrDestination?.Id?.StartsWith("rvr-keep-", StringComparison.Ordinal) != true || !AutonomousSiegeJobs.Eligible(bot)) return false;
            if (_siegeJobKeep==_rvrDestination.Id && (bot.CurrentRegionID!=_rvrDestination.RegionId ||
                bot.GetDistanceTo(new Point3D(_rvrDestination.X,_rvrDestination.Y,_rvrDestination.Z))>4800))
            { AutonomousSiegeJobs.Refresh(bot); return TravelRvrObjective(bot,_rvrDestination); }
            if (bot.CurrentRegionID!=_rvrDestination.RegionId) return false;
            var keep = GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).FirstOrDefault(k =>
                _rvrDestination.Id == $"rvr-keep-{k.KeepID}" && AutonomousRvrKeepPolicy.IsSiegeObjective(k));
            if (keep == null || bot.GetDistanceTo(new Point3D(keep.X, keep.Y, keep.Z)) > 6000) return false;
            bool attacking = AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm); // Fork: keep rule (map 4b)
            GameKeepDoor door = attacking ? SiegeDoor(bot, keep) : null;
            if (_siegeJobKeep != _rvrDestination.Id)
            {
                ReleaseSiegeJob(bot);
                _siegeJobKeep = _rvrDestination.Id;
            }
            if (now < _siegeNextAttempt) return false;
            if (_siegeMoveTo.HasValue && _siegeWeapon != null)
                return ContinueSiegeMove(bot,now);
            var nearby = bot.GetNPCsInRadius(6000).ToArray();
            var engines = nearby.OfType<GameSiegeWeapon>().Where(w => w.IsAlive && w.ObjectState == GameObject.eObjectState.Active).ToArray();
            bool enemyEngines = engines.Any(w => BotSiegeRuntime.LegalEnemy(bot, w));
            // Fork: allies by the realm rule (map 4b), so companions, pets' owners and realm-less helpers count right.
            int present = nearby.OfType<GameBot>().Count(b => b.IsAlive && PvpCombatant.AreAllied(bot, b)) +
                bot.GetPlayersInRadius(6000).Count(p => p.IsAlive && PvpCombatant.AreAllied(bot, p));
            if (!AutonomousSiegeJobs.TryAcquire(bot, _siegeJobKeep, present, attacking && door!=null, enemyEngines, out _siegeKind, out _siegeSlot,
                    carried: CarriedSiegeKind(bot) ?? AbandonedSiegeKind(bot, engines)))
            { _siegeNextAttempt = now + 10_000; return false; }
            if (now>=_siegeNextTopup)
            {
                _siegeNextTopup=now+30_000;
                var merchant=nearby.OfType<GameMerchant>().Where(m=>m.IsWithinRadius(bot,GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE))
                    .OrderBy(bot.GetDistanceTo).FirstOrDefault();
                int bought=BotSiegeRuntime.TryRestockRepairKits(bot,merchant);
                if (bought>0) SiegeLog(bot,"repair_restock",null,$"count={bought} target={BotSiegeRuntime.RepairRestockTarget}");
            }

            GameLiving target = SelectSiegeTarget(bot, keep, door, _siegeKind, nearby, engines);
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
            if (bot.GetNPCsInRadius(450).Any(n => n.IsAlive && n.TargetObject == bot && n.IsAttacking && SiegeOperatorThreat(bot, n)) ||
                bot.GetPlayersInRadius(450).Any(p => p.IsAttacking && p.TargetObject == bot && SiegeOperatorThreat(bot, p))) return false;

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
                    Vector3? move = ChooseSiegePositionRealm(bot,target,_siegeKind,engines.Where(w=>w!=owned).ToArray(),keep,out _);
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
            // Goal 1 (owner 2026-10-07): siege crews use a kit they carry, otherwise synthetic siege equipment set up on
            // the spot; no more shopping trips (runs to run 18: hundreds of supply timeouts, not one engine deployed).
            bool synthetic = BotSiegeRuntime.Item(bot, kitId) == null;
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
                    _siegePosition = ChooseSiegePositionRealm(bot, target, _siegeKind, engines, keep, out string blocked);
                    if (_siegePosition == null)
                    {
                        // Run 13 (2026-10-16): Dun Dagda's ram never found a spot (unconnected=34 of 35, 159 times) after the
                        // outer gate fell; which door, the gate's state and the bot's spot show where the approach breaks.
                        string doorInfo = target is GameKeepDoor kd
                            ? $" door={kd.DoorId} index={SiegeDoorNumber(kd)} at={kd.X},{kd.Y},{kd.Z} gate={string.Join("/", keep.Doors.Values.Where(d => SiegeDoorNumber(d) == 1).Select(d => $"{d.DoorId}:{(d.IsAlive ? "alive" : "broken")}:{d.State}"))}"
                            : "";
                        SiegeLog(bot, "placement_blocked", null, $"kind={_siegeKind} target={target.Name}{doorInfo} from={(int)bot.X},{(int)bot.Y},{(int)bot.Z} {blocked}");
                        _siegeNextAttempt = now + 15_000; return false;
                    }
                }
                if (Vector3.Distance(new(bot.X, bot.Y, bot.Z), _siegePosition.Value) > (_siegeKind == BotSiegeKind.Ballista ? 55 : 90))
                { IssuePath(bot, _siegePosition.Value, preciseArrival:true); SiegeStatus(bot, "Moving to verified siege position", target); return true; }
                bot.StopMovingOnPath(); bot.StopMoving();
                lock (AutonomousSiegeOwnership.ChangeGate)
                {
                    // Recheck the shared cap immediately before consuming any kit.
                    if (target.GetNPCsInRadius(_siegeKind == BotSiegeKind.Ram ? (ushort)650 : (ushort)6000)
                        .OfType<GameSiegeWeapon>().Count(w => w.IsAlive && FriendlyEngine(bot, w) && w.Health > w.DecayedHp &&
                            (BotSiegeRuntime.Kind(w) == _siegeKind || _siegeKind is BotSiegeKind.Catapult or BotSiegeKind.Trebuchet && w is GameSiegeCatapult)) >= 2) return false;
                    DbInventoryItem kit = synthetic ? null : BotSiegeRuntime.Item(bot, kitId);
                    SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
                    Spell spell = kit == null || line == null ? null : SkillBase.FindSpell(kit.SpellID, line);
                    if (!synthetic && (spell == null || !ValidSiegeSpell(_siegeKind, spell.SpellType))) { _siegeNextAttempt = now + 30_000; return false; }
                    owned = CreateSiegeWeapon(WorldModelKind.Realm, _siegeKind);
                    // On the verified spot itself (the operator stops up to 90 short of it).
                    owned.CurrentRegion = bot.CurrentRegion;
                    owned.X = (int)_siegePosition.Value.X; owned.Y = (int)_siegePosition.Value.Y; owned.Z = (int)_siegePosition.Value.Z;
                    owned.Realm = bot.Realm; owned.ItemId = kit?.Id_nb;
                    // A new engine reports position (0,0) until it is in the world: add it, then check its range.
                    if (!owned.AddToWorld()) { SiegeLog(bot, "spawn_failed", null, "could not be added to the world"); return false; }
                    owned.Heading = owned.GetHeading(target);
                    if (!InSiegeRange(owned, target))
                    { SiegeLog(bot, "spawn_failed", null, $"out of range ({(int)owned.GetDistanceTo(target)})"); owned.Delete(); _siegePosition = null; return false; }
                    if (!owned.TryTakeControl(bot) || !synthetic && !BotSiegeRuntime.Consume(bot, kitId, 1))
                    { owned.Delete(); SiegeLog(bot, "spawn_failed", null, "operator could not take control"); return false; }
                }
                _siegeWeapon = owned; _siegeLastProgress = now; _siegeObservedHits = 0;
                SiegeLog(bot, "deployed", owned, synthetic ? "synthetic=true" : $"kit={kitId}");
            }
            if (!bot.IsWithinRadius(owned, Math.Max(32, owned.SIEGE_WEAPON_CONTROLE_DISTANCE - 20)))
            { IssuePath(bot, new(owned.X, owned.Y, owned.Z), preciseArrival:true); return true; }
            bot.StopMovingOnPath(); bot.StopMoving(); bot.TargetObject = target;
            if (owned.TargetObject != target && !owned.SiegeWeaponTimer.IsAlive)
            { owned.TargetObject = target; owned.CurrentState &= ~GameSiegeWeapon.eState.Aimed; }
            if (!owned.SiegeWeaponTimer.IsAlive)
            {
                (owned as GameSiegeRam)?.UpdateRamStatus(); // crew at the ram shortens the reload
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

        private static BotSiegeKind? CarriedSiegeKind(GameBot bot)
        {
            foreach (BotSiegeKind kind in new[] { BotSiegeKind.Ram, BotSiegeKind.Trebuchet, BotSiegeKind.Catapult, BotSiegeKind.Ballista })
                if (BotSiegeRuntime.Item(bot, BotSiegeRuntime.Kit(bot.Realm, kind)) != null) return kind;
            return null;
        }

        /// <summary>An attacker standing beside the operator at its own height (melee reach), not a wall archer.</summary>
        public static bool IsSiegeOperatorThreat(float horizontalDistance, float heightDifference) =>
            horizontalDistance <= 300 && Math.Abs(heightDifference) <= 160;

        private static bool SiegeOperatorThreat(GameBot bot, GameLiving attacker) =>
            IsSiegeOperatorThreat(Vector2.Distance(new(bot.X, bot.Y), new(attacker.X, attacker.Y)), attacker.Z - bot.Z);

        /// <summary>
        /// A spot for this engine, following player siege practice (SiegePlacement): rams straight out from the door's
        /// outer face inside their reach; catapults and trebuchets on a firing ring around their ground target (no line
        /// of sight, the shot arcs), attackers on the far side from the keep and defenders inside; ballistas need a
        /// clear shot unless the target stands up on a wall. Every spot must be reachable without passing a closed
        /// enemy door. Run 18 and earlier: a navmesh line-of-sight ray to the door's centre (inside the excluded door
        /// polygon) and to wall tops failed every time, so no bot ever deployed an engine.
        /// </summary>
        internal static Vector3? ChooseSiegePositionRealm(GameBot bot, GameLiving target, BotSiegeKind kind, GameSiegeWeapon[] engines,
            AbstractGameKeep keep, out string reason)
        {
            reason = "no navmesh";
            var nav = AutonomousKeepApproachNavigation.ForBot(PathfindingProvider.Instance,bot); // Fork: friendly doors by the keep rule
            if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return null;
            Vector2 centre = new(keep.X, keep.Y);
            Vector3 at = new(target.X, target.Y, target.Z);
            var (min, max) = SiegePlacement.Range(kind);
            var candidates = kind == BotSiegeKind.Ram
                ? SiegePlacement.RamCandidates(at, target.Heading, centre, InnerDoorApproach(keep, target))
                : SiegePlacement.ArtilleryCandidates(at, centre, new(bot.X, bot.Y), min, max, AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm));
            int tried = 0, floorless = 0, band = 0, crowded = 0, blind = 0, uneven = 0, unconnected = 0;
            foreach (Vector3 raw in candidates.Take(kind == BotSiegeKind.Ram ? 35 : 80))
            {
                tried++;
                Vector3? floor = nav.GetClosestPoint(bot.CurrentZone, raw, 48, 48, kind==BotSiegeKind.Ram ? 256 : 1024, nav.DefaultFilters);
                if (!floor.HasValue) { floorless++; continue; }
                float distance = Vector3.Distance(floor.Value, at);
                if (kind == BotSiegeKind.Ram ? distance < min || distance > max : distance < min + 50 || distance > max - 100) { band++; continue; }
                // Only working engines take up a spot: wrecks left to their native decay blocked all 35 ram spots at Dun
                // Dagda's Relic Gate after the outer gate fell (run 23).
                if (engines.Any(w => w.Health > w.DecayedHp && Vector3.Distance(floor.Value, new(w.X, w.Y, w.Z)) < (kind == BotSiegeKind.Ram ? 120 : 400))) { crowded++; continue; }
                if (kind == BotSiegeKind.Ballista && target.Z - floor.Value.Z <= 150 &&
                    !nav.HasLineOfSight(bot.CurrentZone, floor.Value + new Vector3(0,0,48), at + new Vector3(0,0,48), nav.DefaultFilters)) { blind++; continue; }
                if (kind != BotSiegeKind.Ram && !LevelGround(nav, bot.CurrentZone, floor.Value)) { uneven++; continue; }
                if (!AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, new(bot.X, bot.Y, bot.Z), floor.Value)) { unconnected++; continue; }
                reason = null;
                return floor;
            }
            reason = $"tried={tried} floorless={floorless} out_of_reach={band} crowded={crowded} blind={blind} uneven={uneven} unconnected={unconnected}";
            return null;
        }

        // Artillery needs a small level pad (rams sit on the gate approach, often a ramp or bridge).
        private static bool LevelGround(IPathfindingMgr nav, Zone zone, Vector3 floor)
        {
            foreach (var d in new[] { new Vector3(40,0,0),new Vector3(-40,0,0),new Vector3(0,40,0),new Vector3(0,-40,0) })
            {
                Vector3? edge = nav.GetClosestPoint(zone, floor + d, 16,16,48,nav.DefaultFilters);
                if (!edge.HasValue || Math.Abs(edge.Value.Z-floor.Z)>56) return false;
            }
            return true;
        }

        /// <summary>The door a ram works on: main gates before posterns, the outer gate before the inner keep door.
        /// Owner 2026-10-07: at Caer Sursbrooke a raid ram set up on the postern instead of the keep door. The postern is
        /// a pair of door halves, only one flagged IsPostern; the unflagged half sat farther from the keep's centre than
        /// the keep door. A door beside a flagged postern counts as one, and doors then go in keep order (gate, keep door)
        /// before distance. Door data is left as it is.</summary>
        internal static GameKeepDoor SiegeDoor(GameBot bot, AbstractGameKeep keep) =>
            keep.Doors.Values.Where(d=>d.IsAlive && d.IsAttackableDoor && d.State==eDoorState.Closed)
                .OrderBy(d=>PosternLike(keep, d) ? 1 : 0)
                .ThenBy(SiegeDoorNumber)
                .ThenByDescending(d=>Vector2.DistanceSquared(new(d.X,d.Y),new(keep.X,keep.Y)))
                .ThenBy(bot.GetDistanceTo).FirstOrDefault();

        /// <summary>For a door behind the outer gate (the keep door), the gate's position: attackers reach it from that
        /// side. Null for the gate itself and for anything that is not a keep door.</summary>
        internal static Vector2? InnerDoorApproach(AbstractGameKeep keep, GameObject door) =>
            door is GameKeepDoor inner && SiegeDoorNumber(inner) != 1 &&
            keep.Doors.Values.FirstOrDefault(d => SiegeDoorNumber(d) == 1) is { } gate ? new Vector2(gate.X, gate.Y) : null;

        /// <summary>
        /// The door's number within its keep (the id's last two digits). GameKeepDoor.DoorIndex keeps only the last digit,
        /// so a relic keep's doors 10 and 11 read 0 and 1: run 14 (2026-10-16) Midgard's rams went for Dun Dagda's door 10
        /// ("index 0", by the keep centre) while the gate still stood, and found no reachable spot 159 times.
        /// </summary>
        public static int SiegeDoorNumber(GameKeepDoor door) => door.DoorId % 100;

        /// <summary>A postern, or the other half of one (within 200 of a flagged postern door).</summary>
        internal static bool PosternLike(AbstractGameKeep keep, GameKeepDoor door) =>
            door.IsPostern || keep.Doors.Values.Any(p => p != door && p.IsPostern && p.IsWithinRadius(door, 200));

        /// <summary>
        /// What each engine shoots at: rams the door; trebuchets the same door (triple damage against doors);
        /// catapults the thickest knot of defenders (double damage against people, 150 splash), those fighting our ram
        /// crews first, wall archers included; ballistas enemy siege first, then a target in view or up on a wall.
        /// </summary>
        internal static GameLiving SelectSiegeTarget(GameBot bot, AbstractGameKeep keep, GameKeepDoor door, BotSiegeKind kind,
            GameNPC[] nearby, GameSiegeWeapon[] engines)
        {
            if (kind == BotSiegeKind.Ram) return door;
            if (kind == BotSiegeKind.Trebuchet && door != null) return door;
            var people = nearby.OfType<GameLiving>().Concat(bot.GetPlayersInRadius(5000)).Where(t =>
                    (BotPvpCrowdControl.PlayerLike(t) || t is GameKeepGuard guard && guard.Component?.Keep==keep && !guard.IsPortalKeepGuard && (door==null || guard is not GuardLord)) &&
                    BotSiegeRuntime.LegalEnemy(bot, t) && !BotPvpCrowdControl.Protected(bot, t))
                .OrderBy(bot.GetDistanceTo).Take(32).ToArray();
            if (kind == BotSiegeKind.Ballista)
                return engines.Where(w => BotSiegeRuntime.LegalEnemy(bot, w)).OrderBy(w => w is GameSiegeRam ? 0 : 1).ThenBy(bot.GetDistanceTo).FirstOrDefault()
                    ?? people.FirstOrDefault(t => t.Z - bot.Z > 150 || BotSiegeRuntime.Visible(bot, t));
            var rams = engines.Where(w => w is GameSiegeRam && FriendlyEngine(bot, w) && w.IsAlive).ToArray();
            return people.OrderByDescending(t => rams.Any(r => r.IsWithinRadius(t, 700)) ? 1 : 0)
                .ThenByDescending(t => people.Count(n => n.IsWithinRadius(t, 150)))
                .FirstOrDefault() ?? door;
        }

        /// <summary>A working friendly engine nobody is operating (a player's dropped ram included): its kind, so the bot
        /// takes that job and hops in.</summary>
        internal static BotSiegeKind? AbandonedSiegeKind(GameBot bot, GameSiegeWeapon[] engines) =>
            engines.Where(w => FriendlyEngine(bot, w) && w.Owner == null && w.IsAlive && w.Health > w.DecayedHp && bot.IsWithinRadius(w, 1500))
                .OrderBy(bot.GetDistanceTo).Select(w => (BotSiegeKind?)BotSiegeRuntime.Kind(w)).FirstOrDefault();
    }
}
