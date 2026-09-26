namespace DOL.GS.ServerRules
{
    public static class PvpDangerExperience
    {
        public static double Multiplier(GameLiving recipient, eXPSource source) =>
            GameServer.ServerRules is PvPServerRules && source == eXPSource.NPC &&
            (recipient?.CurrentZone?.IsOF == true || recipient?.CurrentRegionID == 249) ? 1.5 : 1.0;
    }
}
