namespace DOL.GS
{
    /// <summary>
    /// Companions are groupmates, not pets: their hits, resists and blocks
    /// are not mirrored into the owner's own combat chat. The owner sees
    /// them like any groupmate, through the ordinary "attacks ... and hits"
    /// lines. The player's own pets keep their messages.
    /// </summary>
    public static class CompanionCombatChat
    {
        public static bool IsQuiet(GameNPC npc) =>
            npc is GameBot ||
            npc?.Brain is DOL.AI.Brain.IControlledBrain { Owner: GameBot };
    }
}
