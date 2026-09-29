using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DOL.GS;

/// <summary>What a support bot does this turn in an autonomous RvR fight.</summary>
public enum RvrSupportAction
{
    /// <summary>Today's support behaviour: heals, cures, CC sweep, buffs.</summary>
    HealOnly,
    /// <summary>Damage (or the pet) on the caller's target while the group is healthy.</summary>
    Offense,
    /// <summary>Mez/stun/root the attackers of a group mate before healing.</summary>
    Control,
}

/// <summary>How a spec plays its support job (principle P1: roles are fluid by spec).</summary>
public enum RvrSupportStyle
{
    /// <summary>Heal/buff specs: no change from today.</summary>
    HealFirst,
    /// <summary>Smite Cleric, nature (pet) Druid: offense only behind a second healer.</summary>
    Offensive,
    /// <summary>Pac Healer, cave (Subterranean) Shaman: CC before heals.</summary>
    ControlFirst,
    /// <summary>Staff Friar, battle Warden: fight in the melee line while healthy (already today's behaviour).</summary>
    MeleeHybrid,
}

/// <summary>
/// Wave 4 of livelier RvR bots (principles P1 and P8): spec-aware support for
/// autonomous RvR world bots. The strongest attested gate for a healer doing
/// anything but healing is a second healer in the group ("In a zerg fight you
/// can get away with smiting if you have another cleric", D7668). Cave Shaman
/// and pac Healer put CC before heals ("Healing is nice, but CC is better",
/// D5086). The HP and power numbers are priors: no source gives one.
/// Companions and player-led groups never reach this policy.
/// </summary>
public static class AutonomousRvrSupportOffense
{
    /// <summary>[prior] Lowest living group member must be at least this healthy for offense (per bot and fight 70-80).</summary>
    public const int OffenseGroupMinHealthPercent = 75;
    /// <summary>[prior] Own power needed for offense.</summary>
    public const int OffenseMinPowerPercent = 50;
    /// <summary>[prior] Below this, heals win over CC for the control specs.</summary>
    public const int ControlHealsWinBelowPercent = 40;
    /// <summary>Imperfection: share of fights in which an offensive spec stays on heals anyway.</summary>
    public const double StaysOnHealsChance = 0.30;
    /// <summary>A support bot without a turn for this long starts a new fight (new roll).</summary>
    public const int FightGapMilliseconds = 20_000;

    public static RvrSupportStyle StyleOf(eCharacterClass characterClass, eSpecType spec) => (characterClass, spec) switch
    {
        (eCharacterClass.Cleric, eSpecType.SmiteCleric) => RvrSupportStyle.Offensive,
        (eCharacterClass.Druid, eSpecType.NatureDruid) => RvrSupportStyle.Offensive,
        (eCharacterClass.Healer, eSpecType.PacHealer) => RvrSupportStyle.ControlFirst,
        (eCharacterClass.Shaman, eSpecType.SubtShaman) => RvrSupportStyle.ControlFirst,
        (eCharacterClass.Friar, eSpecType.StaffFriar) => RvrSupportStyle.MeleeHybrid,
        (eCharacterClass.Warden, eSpecType.BattleWarden) => RvrSupportStyle.MeleeHybrid,
        _ => RvrSupportStyle.HealFirst,
    };

    /// <summary>
    /// Classes that can cover heals for a smiting partner: heal specs only.
    /// Smiters, CC-first specs (cave Shaman, pac Healer) and melee hybrids
    /// (staff Friar, battle Warden) are busy with their own job; Paladin and
    /// Bard never count.
    /// </summary>
    public static bool CoversHeals(eCharacterClass characterClass, eSpecType spec) =>
        characterClass is eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer or
            eCharacterClass.Shaman or eCharacterClass.Friar or eCharacterClass.Warden &&
        StyleOf(characterClass, spec) == RvrSupportStyle.HealFirst;

    /// <summary>
    /// Decides this turn's job. <paramref name="roll"/> is drawn once per fight
    /// in [0, 1); below <see cref="StaysOnHealsChance"/> the bot stays on heals
    /// for the whole fight.
    /// </summary>
    public static RvrSupportAction Decide(RvrSupportStyle style, bool secondHealerAlive, int groupMinHealthPercent,
        int ownPowerPercent, bool anyoneNeedsCure, bool callerHasTarget, double roll,
        bool mateAttackedByControllable = false, int offenseGroupMinHealthPercent = OffenseGroupMinHealthPercent)
    {
        if (style == RvrSupportStyle.HealFirst || groupMinHealthPercent < ControlHealsWinBelowPercent ||
            anyoneNeedsCure)
            return RvrSupportAction.HealOnly;

        if (style == RvrSupportStyle.ControlFirst && mateAttackedByControllable)
            return RvrSupportAction.Control;

        if (style == RvrSupportStyle.MeleeHybrid)
            return groupMinHealthPercent >= OffenseGroupMinHealthPercent
                ? RvrSupportAction.Offense
                : RvrSupportAction.HealOnly;

        return secondHealerAlive && groupMinHealthPercent >= offenseGroupMinHealthPercent &&
            ownPowerPercent >= OffenseMinPowerPercent && callerHasTarget && roll >= StaysOnHealsChance
            ? RvrSupportAction.Offense
            : RvrSupportAction.HealOnly;
    }

    /// <summary>
    /// The Healer area stun: player-led groups as before; autonomous bots only
    /// in an RvR group whose doctrine is the bomb group.
    /// </summary>
    public static bool AllowsHealerAreaStun(bool isHealerClass, bool playerLedGroup, RvrDoctrineKind? autonomousRvrDoctrine) =>
        isHealerClass && (playerLedGroup || autonomousRvrDoctrine == RvrDoctrineKind.BombGroup);

    /// <summary>Classes named in the counter line, in this order.</summary>
    public static readonly eCharacterClass[] CountedClasses =
    [
        eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Shaman,
        eCharacterClass.Friar, eCharacterClass.Druid, eCharacterClass.Warden,
    ];
}

/// <summary>
/// Server-wide counters for <see cref="AutonomousRvrSupportOffense"/>, logged
/// once per five-minute window (not per bot). Counts are episodes: a bot adds
/// one each time it enters an action, not one per AI tick.
/// </summary>
public sealed class RvrSupportCounters
{
    public const int WindowMilliseconds = 300_000;

    private readonly object _sync = new();
    private readonly Dictionary<eCharacterClass, int[]> _byClass = [];
    private int _offense;
    private int _control;
    private int _healOnly;
    private int _areaStuns;
    private long _windowStart = -1;

    public void Record(eCharacterClass characterClass, RvrSupportAction action)
    {
        lock (_sync)
        {
            switch (action)
            {
                case RvrSupportAction.Offense: _offense++; break;
                case RvrSupportAction.Control: _control++; break;
                default: _healOnly++; break;
            }
            if (action == RvrSupportAction.HealOnly || Array.IndexOf(AutonomousRvrSupportOffense.CountedClasses, characterClass) < 0)
                return;
            if (!_byClass.TryGetValue(characterClass, out int[] counts))
                _byClass[characterClass] = counts = new int[2];
            counts[action == RvrSupportAction.Offense ? 0 : 1]++;
        }
    }

    /// <summary>Cheap pre-check so the per-turn caller rarely takes the lock.</summary>
    public bool Due(long now)
    {
        long start = System.Threading.Volatile.Read(ref _windowStart);
        return start < 0 || now - start >= WindowMilliseconds;
    }

    public void RecordAreaStun()
    {
        lock (_sync) _areaStuns++;
    }

    /// <summary>
    /// Returns the log lines when the window has ended, and starts a new one.
    /// No lines when nothing happened. The first call only opens the window.
    /// </summary>
    public IReadOnlyList<string> Flush(long now)
    {
        lock (_sync)
        {
            if (_windowStart < 0)
            {
                _windowStart = now;
                return [];
            }
            if (now - _windowStart < WindowMilliseconds)
                return [];
            // The window is closed by the next support turn after five
            // minutes, so the real length is logged, never less than 300 s.
            long seconds = (now - _windowStart) / 1000;
            List<string> lines = [];
            if (_offense + _control + _healOnly > 0)
            {
                StringBuilder byClass = new();
                foreach (eCharacterClass characterClass in AutonomousRvrSupportOffense.CountedClasses)
                {
                    if (!_byClass.TryGetValue(characterClass, out int[] counts) || counts[0] + counts[1] == 0)
                        continue;
                    if (byClass.Length > 0) byClass.Append(',');
                    byClass.Append(characterClass).Append(':').Append(counts[0]).Append('/').Append(counts[1]);
                }
                lines.Add($"RVR_SUPPORT_OFFENSE window_s={seconds} offense={_offense} " +
                    $"control={_control} heal_only={_healOnly} by_class={(byClass.Length > 0 ? byClass : "none")}");
            }
            if (_areaStuns > 0)
                lines.Add($"RVR_HEALER_AREA_STUN window_s={seconds} casts={_areaStuns}");
            _offense = _control = _healOnly = _areaStuns = 0;
            _byClass.Clear();
            _windowStart = now;
            return lines;
        }
    }
}
