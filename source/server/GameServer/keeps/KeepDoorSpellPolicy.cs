namespace DOL.GS.Keeps
{
    /// <summary>
    /// Owner decision 5 (2026-09-28, docs/TASKS.md item 48): as since patch 1.46,
    /// single-target damage spells can hit a keep door, at reduced effect.
    /// Damage over time, debuffs, crowd control and area spells still do not
    /// affect doors (area spells never collect doors as targets). Keep walls
    /// and other components stay siege-only. Applies to players and bots alike.
    /// </summary>
    public static class KeepDoorSpellPolicy
    {
        /// <summary>Share of a direct-damage spell that reaches a door, after the
        /// door's own level toughness (5 % less per keep level).</summary>
        public const double DoorSpellDamageFactor = 0.5;

        /// <summary>Spell types that may damage a keep door.</summary>
        public static bool AffectsKeepDoor(eSpellType spellType) =>
            spellType is eSpellType.SiegeDirectDamage or eSpellType.SiegeArrow ||
            IsReducedDoorDamageSpell(spellType);

        /// <summary>Caster spells that hit a door at <see cref="DoorSpellDamageFactor"/>.</summary>
        public static bool IsReducedDoorDamageSpell(eSpellType spellType) =>
            spellType is eSpellType.DirectDamage or eSpellType.Bolt;

        /// <summary>Whether a caster should cast this spell at a door it targets: an
        /// affecting type and no area radius (a targeted area spell never hits the door).</summary>
        public static bool WorthCastingAtDoor(eSpellType spellType, int radius) =>
            radius <= 0 && AffectsKeepDoor(spellType);

        /// <summary>Multiplier for damage a door takes from an attack of this spell
        /// type (1 for non-spell and siege attacks).</summary>
        public static double DoorDamageFactor(eSpellType? spellType) =>
            spellType is { } type && IsReducedDoorDamageSpell(type) ? DoorSpellDamageFactor : 1d;
    }
}
