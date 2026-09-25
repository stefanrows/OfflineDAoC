using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    public sealed class GameLoopTickPacer
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private const bool DYNAMIC_BUSY_WAIT_THRESHOLD = true;

        private double _tickDuration;
        private bool _running;
        private Thread _busyWaitThresholdThread;
        private int _busyWaitThreshold;
        private readonly AutoResetEvent _paceChanged = new(false);

        private double _totalElapsedTime;
        private Stopwatch _stopwatch;

        public GameLoopTickPacerStats Stats { get; private set; }

        public GameLoopTickPacer(double tickDuration)
        {
            if (tickDuration <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickDuration), "Tick duration must be a positive value.");

            _tickDuration = tickDuration;
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;

            if (DYNAMIC_BUSY_WAIT_THRESHOLD && Environment.ProcessorCount > 1)
            {
                _busyWaitThresholdThread = new(new ThreadStart(UpdateBusyWaitThreshold))
                {
                    Name = $"{GameLoop.THREAD_NAME}_BusyWaitThreshold",
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true
                };
                _busyWaitThresholdThread.Start();
            }

            Stats = new([60000, 30000, 10000], 1000.0 / _tickDuration * OfflineWorldSpeedControl.MaximumMultiplier);
            _stopwatch = Stopwatch.StartNew();
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;
            _paceChanged.Set();

            if (_busyWaitThresholdThread != null && _busyWaitThresholdThread != Thread.CurrentThread && _busyWaitThresholdThread.IsAlive)
                _busyWaitThresholdThread.Interrupt(); // This thread sleeps for a long time, let's not wait for it to finish.

            _busyWaitThresholdThread = null;
        }

        public void PaceChanged()
        {
            _paceChanged.Set();
        }

        public void WaitForNextTick(Func<int> getEffectiveMultiplier)
        {
            while (true)
            {
                double multiplier = Math.Clamp(getEffectiveMultiplier(), 1, OfflineWorldSpeedControl.MaximumMultiplier);
                double targetDuration = GetTargetTickDuration(_tickDuration, (int) multiplier);
                double remaining = targetDuration - _stopwatch.Elapsed.TotalMilliseconds;

                if (remaining <= 0)
                    break;

                int busyWaitThreshold = Volatile.Read(ref _busyWaitThreshold);
                if (busyWaitThreshold == 0 && remaining >= 1)
                {
                    int sleepFor = (int) Math.Max(1, Math.Floor(remaining));
                    _paceChanged.WaitOne(sleepFor);
                }
                else if (remaining >= busyWaitThreshold && busyWaitThreshold > 0)
                {
                    int sleepFor = (int) Math.Max(1, Math.Floor(remaining - busyWaitThreshold));
                    _paceChanged.WaitOne(sleepFor);
                }
                else
                {
                    if (_paceChanged.WaitOne(0))
                        continue;

                    // Any small number will do here. Technically, this could be 0.
                    // If the game loop appears to overshoot the tick duration for no reason, this can be reduced even further.
                    Thread.SpinWait(10);
                }
            }

            double elapsedTime = _stopwatch.Elapsed.TotalMilliseconds;
            _totalElapsedTime += elapsedTime;
            _stopwatch.Restart();

            Stats.RecordTick(_totalElapsedTime);
        }

        public static double GetTargetTickDuration(double logicalTickDuration, int multiplier)
        {
            if (logicalTickDuration <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalTickDuration));
            if (multiplier < 1 || multiplier > OfflineWorldSpeedControl.MaximumMultiplier)
                throw new ArgumentOutOfRangeException(nameof(multiplier));

            return logicalTickDuration / multiplier;
        }

        private void UpdateBusyWaitThreshold()
        {
            int maxIteration = 10;
            int sleepFor = 1;
            int pauseFor = 10000;
            Stopwatch stopwatch = new();
            stopwatch.Start();

            try
            {
                while (Volatile.Read(ref _running))
                {
                    double start;
                    double overSleptFor;
                    double highest = 0;

                    for (int i = 0; i < maxIteration; i++)
                    {
                        start = stopwatch.Elapsed.TotalMilliseconds;
                        Thread.Sleep(sleepFor);
                        overSleptFor = stopwatch.Elapsed.TotalMilliseconds - start - sleepFor;

                        if (highest < overSleptFor)
                            highest = overSleptFor;
                    }

                    _busyWaitThreshold = (int) Math.Max(0, Math.Ceiling(highest));
                    Thread.Sleep(pauseFor);
                }
            }
            catch (ThreadInterruptedException)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"Thread \"{Thread.CurrentThread.Name}\" was interrupted");
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"Critical error encountered in {nameof(GameLoopTickPacer)}: {e}");
            }

            if (log.IsInfoEnabled)
                log.Info($"Thread \"{Thread.CurrentThread.Name}\" is stopping");
        }
    }
}
