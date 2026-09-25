using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DOL.GS.ServerProperties;
using DOL.Logging;
using ECS.Debug;

namespace DOL.GS
{
    public static class GameLoop
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string THREAD_NAME = "GameLoop";

        private static Thread _gameLoopThread;
        private static GameLoopThreadPool _threadPool;
        private static GameLoopTickPacer _tickPacer;
        private static bool _running;
        private static List<TickStep> _tickSequence;
        private static int _logicalTimeRemainder;
        private static double _tickBudgetMilliseconds;

        public static double TickDuration { get; private set; }
        public static long GameLoopTime { get; private set; }
        public static double TickBudgetMilliseconds => Volatile.Read(ref _tickBudgetMilliseconds);
        public static double SelectedTickBudgetMilliseconds => TickDuration / Math.Max(1, OfflineWorldSpeedControl.SelectedMultiplier);
        public static string ActiveService { get; set; }

        public static bool Init()
        {
            if (Interlocked.CompareExchange(ref _running, true, false))
                return false;

            TickDuration = 1000.0 / Properties.GAME_LOOP_TICK_RATE;
            GameLoopTime = 0;
            _logicalTimeRemainder = 0;
            _tickBudgetMilliseconds = TickDuration;

            _gameLoopThread = new(new ThreadStart(Run))
            {
                Name = THREAD_NAME,
                IsBackground = true
            };
            _tickPacer = new(TickDuration);
            _tickPacer.Start();
            _gameLoopThread.Start();
            return true;
        }

        public static void Exit()
        {
            if (!Interlocked.CompareExchange(ref _running, false, true))
                return;

            if (Thread.CurrentThread != _gameLoopThread && _gameLoopThread.IsAlive)
                _gameLoopThread.Join();

            _tickPacer.Stop();
            _threadPool.Dispose();
            AutonomousWorldBotController.StopCampPlanning();
        }

        public static List<(int, double)> GetAverageTps()
        {
            return _tickPacer.Stats.GetAverageTicks();
        }

        public static void NotifySpeedChanged()
        {
            _tickPacer?.PaceChanged();
        }

        public static int CalculateLogicalTickDeltaMilliseconds(int logicalTicksPerSecond, ref int remainder)
        {
            if (logicalTicksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalTicksPerSecond));
            if (remainder < 0 || remainder >= logicalTicksPerSecond)
                throw new ArgumentOutOfRangeException(nameof(remainder));

            int accumulatedMilliseconds = remainder + 1000;
            int deltaMilliseconds = accumulatedMilliseconds / logicalTicksPerSecond;
            remainder = accumulatedMilliseconds % logicalTicksPerSecond;
            return deltaMilliseconds;
        }

        public static void ExecuteForEach<T>(List<T> items, int toExclusive, Action<T> action)
        {
            _threadPool.ExecuteForEach(items, toExclusive, action);
        }

        public static T GetObjectForTick<T>() where T : IPooledObject<T>, new()
        {
            return _threadPool != null ? _threadPool.GetObjectForTick<T>() : new();
        }

        public static List<T> GetListForTick<T>() where T : IPooledList<T>
        {
            return _threadPool != null ? _threadPool.GetListForTick<T>() : new();
        }

        private static void Run()
        {
            if (Environment.ProcessorCount == 1)
                _threadPool = new GameLoopThreadPoolSingleThreaded();
            else
                _threadPool = new GameLoopThreadPoolMultiThreaded(Environment.ProcessorCount);

            _threadPool.Init(); // Must be done from the game loop thread.
            BuildTickSequence();

            while (Volatile.Read(ref _running))
            {
                try
                {
                    OfflineWorldSpeedControl.RefreshClientCount(ClientService.Instance.ClientCount);
                    int effectiveMultiplier = OfflineWorldSpeedControl.EffectiveMultiplier;
                    Volatile.Write(ref _tickBudgetMilliseconds, TickDuration / Math.Max(1, effectiveMultiplier));
                    TickServices();
                    AdvanceCompletedTick();
                    OfflineWorldSpeedControl.RecordCompletedLogicalTick();
                    _tickPacer.WaitForNextTick(() => OfflineWorldSpeedControl.EffectiveMultiplier);
                }
                catch (Exception e)
                {
                    if (log.IsFatalEnabled)
                        log.Fatal($"Critical error encountered in {nameof(GameLoop)}: {e}");

                    GameServer.Instance.Stop();
                    break;
                }
            }

            if (log.IsInfoEnabled)
                log.Info($"Thread \"{Thread.CurrentThread.Name}\" is stopping");
        }

        private static void AdvanceCompletedTick()
        {
            int deltaMilliseconds = CalculateLogicalTickDeltaMilliseconds(Properties.GAME_LOOP_TICK_RATE, ref _logicalTimeRemainder);

            WorldSimulationClock.AdvanceMilliseconds(deltaMilliseconds);
            GameLoopTime += deltaMilliseconds;
        }

        private static void TickServices()
        {
            long tickStarted = Stopwatch.GetTimestamp();
            Diagnostics.StartPerfCounter(THREAD_NAME);

            for (int i = 0; i < _tickSequence.Count; i++)
            {
                TickStep tickStep = _tickSequence[i];
                ExecutionContext.Run(tickStep.Context, TickCallback, tickStep.State);
            }

            Diagnostics.StopPerfCounter(THREAD_NAME);
            Diagnostics.Tick();
            GameLoopWorkMetrics.RecordTick(tickStarted);

            static void TickCallback(object state)
            {
                TickState tickState = (TickState) state;
                ActiveService = tickState.ProfileKey;
                long measuredStart = GameLoopWorkMetrics.BeginStage();
                Diagnostics.StartPerfCounter(ActiveService);

                try
                {
                    tickState.TickAction();
                }
                finally
                {
                    Diagnostics.StopPerfCounter(ActiveService);
                    GameLoopWorkMetrics.EndStage(ActiveService, measuredStart);
                    ActiveService = string.Empty;
                }
            }
        }

        private static void BuildTickSequence()
        {
            _tickSequence = new();

            AddStep(GameLoopService.Instance, GameLoopService.Instance.Tick);
            AddStep(TimerService.Instance, TimerService.Instance.Tick);
            AddStep(ClientService.Instance, ClientService.Instance.BeginTick);
            AddStep(NpcService.Instance, NpcService.Instance.Tick);
            AddStep(AttackService.Instance, AttackService.Instance.Tick);
            AddStep(CastingService.Instance, CastingService.Instance.Tick);
            AddStep(EffectService.Instance, EffectService.Instance.Tick);
            AddStep(EffectListService.Instance, EffectListService.Instance.BeginTick);
            AddStep(EffectListService.Instance, EffectListService.Instance.EndTick);
            AddStep(MovementService.Instance, MovementService.Instance.Tick);
            AddStep(CraftingService.Instance, CraftingService.Instance.Tick);
            AddStep(ReaperService.Instance, ReaperService.Instance.Tick);
            AddStep(ZoneService.Instance, ZoneService.Instance.Tick);
            AddStep(ClientService.Instance, ClientService.Instance.EndTick);
            AddStep(DailyQuestService.Instance, DailyQuestService.Instance.Tick);
            AddStep(WeeklyQuestService.Instance, WeeklyQuestService.Instance.Tick);
            AddStep(MonthlyQuestService.Instance, MonthlyQuestService.Instance.Tick);

            GameServiceContext.Current.Value = null;

            static void AddStep(IGameService service, Action action)
            {
                string methodName = action.Method.Name;
                string profileKey = methodName == nameof(IGameService.Tick) ? service.ServiceName : $"{service.ServiceName}.{methodName}";
                GameServiceContext.Current.Value = service;
                _tickSequence.Add(new(new(action, profileKey), ExecutionContext.Capture()));
            }
        }

        private sealed class TickStep
        {
            public readonly TickState State;
            public readonly ExecutionContext Context;

            public TickStep(TickState state, ExecutionContext context)
            {
                State = state;
                Context = context;
            }
        }

        private sealed class TickState
        {
            public readonly Action TickAction;
            public readonly string ProfileKey;

            public TickState(Action tickAction, string profileKey)
            {
                TickAction = tickAction;
                ProfileKey = profileKey;
            }
        }
    }
}
