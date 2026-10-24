using System;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Watches a living for the things that end a held effect: moving,
    /// attacking or starting a cast.
    ///
    /// Several Master Level holds (Lookout, Blanket of Camouflage, Battlewarder,
    /// Focusing Winds) were written against GamePlayerEvent.Moving,
    /// AttackFinished and CastStarting. Nothing in this server raises those
    /// events, so the holds never ended. The state behind them is still
    /// readable, so the watch samples it on a short beat instead of waiting:
    ///
    ///   moving    IsMoving, a drift of more than <see cref="StepDistance"/>
    ///             from the point where the watch was armed (this catches a
    ///             short walk that started and stopped between two samples),
    ///             or a region change
    ///   attacking attack mode on, or LastAttackTick moving past its start value
    ///   casting   a spell being cast, other than the one that raised the effect
    ///   standing  (optional) the living is no longer sitting
    ///
    /// A watched living that dies or leaves the world reports "gone".
    /// The watch stops itself before it reports, so the callback runs once.
    /// Ported from the gaheris OpenDAoC overlay (scripts/core/MovementWatch.cs).
    /// </summary>
    public sealed class MovementWatch
    {
        public const string Moving = "moving";
        public const string Attacking = "attacking";
        public const string Casting = "casting";
        public const string Standing = "standing";
        public const string Gone = "gone";

        /// <summary>Drift from the armed point that counts as a step rather than a wobble.</summary>
        public const int StepDistance = 32;

        /// <summary>Sample interval in milliseconds.</summary>
        public const int BeatInterval = 400;

        private readonly GameLiving _who;
        private readonly Action<string> _broke;
        private readonly Func<bool> _stillWanted;
        private readonly bool _onMove;
        private readonly bool _onAttack;
        private readonly bool _onCast;
        private readonly ISpellHandler _ownCast;
        private readonly bool _onStand;

        private ECSGameTimer _timer;
        private Point3D _stood;
        private ushort _region;
        private long _lastAttack;
        private bool _armed;

        /// <param name="who">The living to watch.</param>
        /// <param name="broke">Called once, with the reason, when something breaks the hold.</param>
        /// <param name="stillWanted">
        /// Optional. Return false when the effect has ended some other way; the
        /// watch then stops without calling <paramref name="broke"/>.
        /// </param>
        /// <param name="ownCast">
        /// Optional. The spell handler that raised the effect; its own cast is
        /// still finishing when the watch starts and does not count as casting.
        /// </param>
        /// <param name="onStand">Also break when the living stops sitting.</param>
        public MovementWatch(GameLiving who, Action<string> broke,
                             Func<bool> stillWanted = null,
                             bool onMove = true, bool onAttack = false, bool onCast = false,
                             ISpellHandler ownCast = null, bool onStand = false)
        {
            _onStand = onStand;
            _who = who;
            _broke = broke;
            _stillWanted = stillWanted;
            _onMove = onMove;
            _onAttack = onAttack;
            _onCast = onCast;
            _ownCast = ownCast;
        }

        public GameLiving Watched => _who;
        public bool IsRunning => _armed;

        /// <summary>Records the starting point and starts sampling.</summary>
        public void Start()
        {
            if (_armed || _who == null)
                return;

            Arm();
            _timer = new ECSGameTimer(_who, OnBeat);
            _timer.Start(BeatInterval);
        }

        /// <summary>
        /// Records where the living stands and its last attack, without a timer.
        /// <see cref="Start"/> calls this; tests and callers that sample on their
        /// own beat call it directly and then <see cref="Check"/>.
        /// </summary>
        public void Arm()
        {
            if (_who == null)
                return;

            _stood = new Point3D(_who.X, _who.Y, _who.Z);
            _region = _who.CurrentRegionID;
            _lastAttack = Math.Max(_who.LastAttackTickPvE, _who.LastAttackTickPvP);
            _armed = true;
        }

        public void Stop()
        {
            _armed = false;
            ECSGameTimer timer = _timer;
            _timer = null;
            timer?.Stop();
        }

        /// <summary>
        /// One sample. Returns true while the watch keeps running; reports the
        /// reason and returns false once something has broken the hold.
        /// </summary>
        public bool Check()
        {
            if (!_armed)
                return false;

            if (_stillWanted != null && !_stillWanted())
            {
                Stop();
                return false;
            }

            string why = BreakReason();

            if (why == null)
                return true;

            Stop();

            try
            {
                _broke?.Invoke(why);
            }
            catch (Exception)
            {
                // A cancel that throws is not worth taking the timer service down for.
            }

            return false;
        }

        /// <summary>The reason the hold would break now, or null.</summary>
        public string BreakReason()
        {
            if (_who == null || !_who.IsAlive || _who.ObjectState != GameObject.eObjectState.Active)
                return Gone;

            if (_onStand && !_who.IsSitting)
                return Standing;

            if (_onMove && (_who.IsMoving || _who.CurrentRegionID != _region ||
                            (_stood != null && _stood.GetDistanceTo(_who) > StepDistance)))
                return Moving;

            if (_onAttack && (_who.IsAttacking ||
                              Math.Max(_who.LastAttackTickPvE, _who.LastAttackTickPvP) > _lastAttack))
                return Attacking;

            if (_onCast && _who.IsCasting && !IsOwnCast())
                return Casting;

            return null;
        }

        private bool IsOwnCast()
        {
            return _ownCast != null && ReferenceEquals(_who.castingComponent?.SpellHandler, _ownCast);
        }

        private int OnBeat(ECSGameTimer timer)
        {
            return Check() ? BeatInterval : 0;
        }
    }
}
