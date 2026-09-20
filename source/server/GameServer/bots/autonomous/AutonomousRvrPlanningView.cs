using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        // One planning view per NPC phase, not one global population regrouping
        // per warband. No combat target, inventory, purchase, or route is cached.
        private sealed record RvrPlanningView(GameBot[] Candidates, Dictionary<ushort, GameBot[]> Population)
        {
            public (int Allies, int Enemies) Count(ushort region, eRealm realm)
            {
                if (!Population.TryGetValue(region, out GameBot[] actors)) return default;
                int allies = actors.Count(candidate => candidate.Realm == realm);
                return (allies, actors.Length - allies);
            }

            public (int Allies, int Enemies) CountForActor(ushort region, GameBot actor)
            {
                if (!Population.TryGetValue(region, out GameBot[] actors)) return default;
                int allies = actors.Count(candidate => PvpCombatant.AreAllied(actor, candidate));
                return (allies, actors.Length - allies);
            }
        }
        private static readonly Lock RvrPlanningLock = new();
        private static RvrPlanningView _rvrPlanningView;

        public static void PrepareRvrPlanningTick() => Volatile.Write(ref _rvrPlanningView, null);

        private static RvrPlanningView GetRvrPlanningView()
        {
            RvrPlanningView view = Volatile.Read(ref _rvrPlanningView);
            if (view != null) return view;
            lock (RvrPlanningLock)
            {
                if (_rvrPlanningView != null) return _rvrPlanningView;
                var population = new Dictionary<ushort, List<GameBot>>();
                var candidates = new List<GameBot>();
                foreach (GameBot actor in AutonomousBotRegistry.Snapshot())
                {
                    if (!actor.IsAlive) continue;
                    if (!population.TryGetValue(actor.CurrentRegionID, out List<GameBot> actors))
                        population[actor.CurrentRegionID] = actors = new List<GameBot>();
                    actors.Add(actor);
                    if (actor.Realm != eRealm.None && AutonomousObjectiveAssignments.Is(actor, eAutonomousObjectiveKind.RvR) && IsInFrontier(actor))
                        candidates.Add(actor);
                }
                view = new(candidates.ToArray(), population.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
                Volatile.Write(ref _rvrPlanningView, view);
                return view;
            }
        }
    }
}
