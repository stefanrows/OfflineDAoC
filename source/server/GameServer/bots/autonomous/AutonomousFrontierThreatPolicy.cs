using System;
using System.Collections.Generic;

namespace DOL.GS;

public sealed class AutonomousFrontierThreatPolicy
{
    public const int VisibilityBudget = 8;
    public const int CandidateBudget = 24;
    private long _nextScan;
    private int _cursor;
    private int _secondaryCursor;
    private bool _secondaryPending;

    public IReadOnlyList<T> VisiblePriority<T>(IReadOnlyList<T> primary, IReadOnlyList<T> secondary, Func<T, bool> visible) =>
        VisiblePriority(primary, secondary, _ => true, visible);

    // Bound expensive legality/fight/area checks as well as LOS. Defenders
    // finish the combatant sweep before engines; blocked or ineligible crowds
    // cannot restart the sweep and permanently conceal later attackers.
    public IReadOnlyList<T> VisiblePriority<T>(IReadOnlyList<T> primary, IReadOnlyList<T> secondary,
        Func<T, bool> eligible, Func<T, bool> visible)
    {
        var result = new List<T>(VisibilityBudget);
        int attempts = 0;
        int rays = 0;
        if (!_secondaryPending && primary.Count > 0)
        {
            int index = _cursor % primary.Count;
            while (index < primary.Count && attempts < CandidateBudget && rays < VisibilityBudget)
            {
                T candidate = primary[index++];
                attempts++;
                if (!eligible(candidate)) continue;
                rays++;
                if (visible(candidate)) result.Add(candidate);
            }
            if (result.Count > 0) { _cursor = 0; return result; }
            if (index < primary.Count) { _cursor = index; return result; }
            _cursor = 0;
            if (attempts == CandidateBudget || rays == VisibilityBudget)
            {
                _secondaryPending = true;
                return result;
            }
        }
        else if (primary.Count == 0)
            _cursor = 0;
        _secondaryPending = false;
        if (secondary.Count == 0) { _secondaryCursor = 0; return result; }
        int start = _secondaryCursor % secondary.Count;
        int checkedSecondary = 0;
        while (checkedSecondary < secondary.Count && attempts < CandidateBudget && rays < VisibilityBudget)
        {
            T candidate = secondary[(start + checkedSecondary++) % secondary.Count];
            attempts++;
            if (!eligible(candidate)) continue;
            rays++;
            if (visible(candidate)) result.Add(candidate);
        }
        _secondaryCursor = result.Count > 0 ? 0 : (start + checkedSecondary) % secondary.Count;
        return result;
    }

    public bool Due(long now, long key)
    {
        if (now < _nextScan) return false;
        _nextScan = now + 1000 + (int)(unchecked((ulong)key) % 250);
        return true;
    }

    public IReadOnlyList<T> Visible<T>(IReadOnlyList<T> nearest, Func<T, bool> visible) =>
        Visible(nearest, _ => true, visible);

    // Rotate after a blocked/ineligible window rather than evaluating every
    // nearby actor's full fight policy on every scan of a crowded keep.
    public IReadOnlyList<T> Visible<T>(IReadOnlyList<T> nearest, Func<T, bool> eligible, Func<T, bool> visible)
    {
        var result = new List<T>(VisibilityBudget);
        if (nearest.Count == 0) { _cursor = 0; return result; }
        int start = _cursor % nearest.Count;
        int attempts = 0;
        int rays = 0;
        while (attempts < Math.Min(CandidateBudget, nearest.Count) && rays < VisibilityBudget)
        {
            T candidate = nearest[(start + attempts++) % nearest.Count];
            if (!eligible(candidate)) continue;
            rays++;
            if (visible(candidate)) result.Add(candidate);
        }
        _cursor = result.Count > 0 ? 0 : (start + attempts) % nearest.Count;
        return result;
    }
}
