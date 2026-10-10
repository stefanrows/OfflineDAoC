using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Bug 75 switches for automatic keep sieges. The loader in ServerProperties
/// finds these static fields in every assembly and adds their default rows;
/// the initial values below are the same defaults for code that runs before
/// (or without) the property loader, such as unit tests.
/// </summary>
public static class AutonomousSiegeProperties
{
    [ServerProperty("pvp", "siege_column_quorum_march", "Siege column marches once leader, ram carriers and the quorum are together; stragglers follow. False = 0.217.0 rule (no march without stragglers)", true)]
    public static bool SIEGE_COLUMN_QUORUM_MARCH = true;

    [ServerProperty("pvp", "siege_column_quorum", "Members (leader included) a siege column needs together before it marches on (2-8)", 6)]
    public static int SIEGE_COLUMN_QUORUM = 6;

    [ServerProperty("pvp", "siege_army_keep_level_quorum", "After five minutes of gathering, a guild army needs only a keep-level troop floor (level 1-3: 6, 4-6: 12, 7-10: 16, never below 1.5x the sighted defenders)", true)]
    public static bool SIEGE_ARMY_KEEP_LEVEL_QUORUM = true;
}
