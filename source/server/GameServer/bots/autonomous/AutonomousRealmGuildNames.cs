using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Guild names of the RvR world's bot guilds (docs/RVR_BOTS.md, "Guilds and login"). Every realm has its own
/// table per charter, written in the style of 2003 guilds: Arthurian orders and village fellowships in Albion,
/// Norse warbands and mead halls in Midgard, Gaelic clans and fianna in Hibernia, with the odd lowercase joke
/// name. Eight names per charter and realm cover a full pool of fifteen guilds without a numbered suffix.
/// Names use letters and spaces only, as the player guild command requires. The Camlann world keeps its own
/// names (<see cref="AutonomousCrewManager.NameForOrdinal(int, AutonomousGuildCharter)"/>).
/// </summary>
public static class AutonomousRealmGuildNames
{
    private static readonly IReadOnlyDictionary<eRealm, IReadOnlyDictionary<AutonomousGuildCharter, string[]>> Tables =
        new Dictionary<eRealm, IReadOnlyDictionary<AutonomousGuildCharter, string[]>>
        {
            [eRealm.Albion] = new Dictionary<AutonomousGuildCharter, string[]>
            {
                [AutonomousGuildCharter.Hunting] = [
                    "Blackthorn Company", "Knives of Lyonesse", "Pennine Wolves", "Silent Arrows",
                    "Black Moor Rangers", "Sons of Morgana", "The Cutpurse Guild", "Hadrians Watch"],
                [AutonomousGuildCharter.Rvr] = [
                    "Order of the Red Dragon", "Lions of Albion", "Pendragons Fist", "Excaliburs Edge",
                    "Emain Breakers", "The Last Charge", "Iron Legion", "Swords of Camelot"],
                [AutonomousGuildCharter.Keep] = [
                    "Wardens of Benowyc", "Stonewall Brotherhood", "Templars of the Wall", "Hold of Renaris",
                    "Erasleigh Guard", "The Gatewardens", "Shield of Sursbrooke", "Boldiam Watch"],
                [AutonomousGuildCharter.Leveling] = [
                    "Salisbury Fellowship", "Cotswold Company", "Camp Fire Club", "Avalon Marsh Rats",
                    "Black Mountain Explorers", "Llyn Barfog Anglers", "Pennine Pilgrims", "Lethantis Scholars"],
                [AutonomousGuildCharter.Social] = [
                    "The Lions Den", "Ye Olde Alehouse", "Ludlow Lads", "Order of the Mead Cup",
                    "Humberton Regulars", "Late to Camelot", "the bards of prydwen", "Tavern Knights"],
            },
            [eRealm.Midgard] = new Dictionary<AutonomousGuildCharter, string[]>
            {
                [AutonomousGuildCharter.Hunting] = [
                    "Wolves of Jordheim", "Night Ravens", "Hunters of Myrkwood", "Fenrirs Kin",
                    "Silent Spears", "Skadis Chosen", "Shadow Clan", "Ulfhednar"],
                [AutonomousGuildCharter.Rvr] = [
                    "Valhalla Bound", "Odins Fist", "Sons of Thor", "Hammer of Midgard",
                    "Einherjar", "Ragnarok Rising", "Bifrost Breakers", "Storm of Svealand"],
                [AutonomousGuildCharter.Keep] = [
                    "Shieldwall of Bledmeer", "Wardens of Glenlock", "Nottmoor Hold", "Hlidskialf Guard",
                    "Blendrake Watch", "Iron Gate Kin", "Stone Hall Defenders", "Fensalir Bulwark"],
                [AutonomousGuildCharter.Leveling] = [
                    "Mularn Hunters", "Gotar Gatherers", "Fort Atla Fellowship", "Audliten Trackers",
                    "Skona Explorers", "Dellingstad Pathfinders", "Huginfell Trappers", "Vasudheim Wanderers"],
                [AutonomousGuildCharter.Social] = [
                    "Mead Hall Regulars", "Jordheim Ale Club", "Valkyries Tavern", "Kobold Fan Club",
                    "Troll Hugs", "Sons of Mead", "Skald Circle", "the dwarf bar"],
            },
            [eRealm.Hibernia] = new Dictionary<AutonomousGuildCharter, string[]>
            {
                [AutonomousGuildCharter.Hunting] = [
                    "Children of the Mist", "Fianna Shades", "Emerald Knives", "Raven Clan",
                    "Moonfall Rangers", "Sidhe Whispers", "Silent Wolves", "Cruachan Stalkers"],
                [AutonomousGuildCharter.Rvr] = [
                    "Tuatha Warband", "Clan Cuchulain", "Fianna of Erin", "Red Branch Knights",
                    "Wrath of Lugh", "Odins Gate Breakers", "Banshees Wail", "Stormcrows of Breifine"],
                [AutonomousGuildCharter.Keep] = [
                    "Wardens of Crauchon", "Dun Bolg Defenders", "Crimthain Guard", "Ailinne Shieldbearers",
                    "Da Behn Hold", "Scathaig Watch", "Nged Stonewall", "Keepers of the Dun"],
                [AutonomousGuildCharter.Leveling] = [
                    "Lough Derg Fishers", "Connacht Rangers", "Shannon Wanderers", "Silvermine Explorers",
                    "Mag Mell Fellowship", "Tir na mBeo Camp", "Cliffs of Moher Hikers", "Lough Gur Trappers"],
                [AutonomousGuildCharter.Social] = [
                    "Tir na nOg Tavern", "Lurikeen Social Club", "Firbolg Hugs", "Pints at Howth",
                    "Celtic Twilight", "Selkie Song", "Grove of Friends", "the elves of ardee"],
            },
        };

    /// <summary>The names of one realm and charter in preference order; empty for a realm without a table.</summary>
    public static IReadOnlyList<string> For(eRealm realm, AutonomousGuildCharter charter) =>
        Tables.TryGetValue(realm, out var byCharter) && byCharter.TryGetValue(charter, out string[] names)
            ? names
            : [];

    /// <summary>Every name of a realm, all charters.</summary>
    public static IEnumerable<string> All(eRealm realm) =>
        Tables.TryGetValue(realm, out var byCharter) ? byCharter.Values.SelectMany(names => names) : [];

    /// <summary>
    /// The first name of the realm and charter that is not taken. When every name is taken the first name gets a
    /// number (" 2", " 3", ...), as the Camlann names do. Null for a realm without a table.
    /// </summary>
    public static string Pick(eRealm realm, AutonomousGuildCharter charter, Func<string, bool> taken)
    {
        IReadOnlyList<string> names = For(realm, charter);
        if (names.Count == 0)
            return null;
        taken ??= _ => false;
        foreach (string name in names)
            if (!taken(name))
                return name;
        for (int suffix = 2; ; suffix++)
            foreach (string name in names)
                if (!taken($"{name} {suffix}"))
                    return $"{name} {suffix}";
    }

    /// <summary>
    /// Whether a guild name comes from this realm's tables (with or without a number). A managed RvR guild whose
    /// name does not was seeded before the realm names existed and is renamed once.
    /// </summary>
    public static bool IsRealmName(eRealm realm, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        string baseName = name;
        int space = name.LastIndexOf(' ');
        if (space > 0 && int.TryParse(name[(space + 1)..], out int number) && number >= 2)
            baseName = name[..space];
        return All(realm).Contains(baseName, StringComparer.Ordinal);
    }
}
