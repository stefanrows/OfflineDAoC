namespace DOL.GS
{
    /// <summary>
    /// Dexterity shortens every cast (GameLiving.CalculateCastingTime), so a
    /// buffer first gives itself its own dexterity and dexterity/quickness
    /// buffs and then buffs everyone else faster.
    /// </summary>
    public static class BotCastSpeedSelfBuff
    {
        public static bool IsCastSpeedBuff(Spell spell) =>
            spell != null && !spell.IsHarmful && !spell.IsPulsing &&
            spell.SpellType is eSpellType.DexterityBuff or eSpellType.DexterityQuicknessBuff &&
            spell.Target is eSpellTarget.SELF or eSpellTarget.GROUP or eSpellTarget.REALM;
    }
}
