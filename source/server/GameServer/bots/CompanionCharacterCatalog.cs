using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Original Classic + SI companion cast. Keys are stable save identities.</summary>
    public static class CompanionCharacterCatalog
    {
        public sealed record Character(string Key, eRealm Realm, eCharacterClass Class, string Name,
            eRace Race, eGender Gender, string Personality, string Background, string Greeting,
            string FieldNote, byte Size);

        // Each row is written for one individual. Keep keys stable after release.
        private const string Cast = """
Albion|Armsman|Aldren|Briton|Male|steady|A gate sentry who kept the evacuation road open after his post fell.|I have held narrower ground than this.|Count the exits before counting the enemy.|53
Albion|Armsman|Yselle|Highlander|Female|bold|A border veteran who trains recruits with blunted spears and honest praise.|Point me at the breach.|A shield is a promise made in public.|56
Albion|Cabalist|Merrow|Inconnu|Male|curious|An archivist tracking why abandoned shrines still answer whispered names.|The evidence is rarely where it should be.|Write down what the dead refuse to say.|48
Albion|Cabalist|Sabine|Saracen|Female|wry|A caravan apothecary who learned binding rites to guard medicine shipments.|Try not to touch the glowing part.|Every curse has a price and a receipt.|51
Albion|Cleric|Elian|Briton|Male|gentle|A hospice keeper who follows armies to bring survivors home.|Tell me who needs help first.|The last patient matters as much as the first.|52
Albion|Cleric|Rosam|Avalonian|Female|steady|A chapel scholar who tests doctrine against the needs of the wounded.|We can be brave and careful together.|Mercy should travel farther than banners.|49
Albion|Friar|Beren|Briton|Male|wry|A monastery gardener who learned to defend the harvest roads.|I brought bandages and a very plain staff.|A quiet road still deserves a watch.|54
Albion|Friar|Maireth|Briton|Female|gentle|A lay healer who traded cloister walls for work in scattered villages.|There is room for one more at our fire.|Good medicine begins by listening.|50
Albion|Infiltrator|Corven|Inconnu|Male|steady|A former courier who learned to cross guarded lines without leaving a trail.|I will look before we step.|The safest path has an answer for the way back.|47
Albion|Infiltrator|Liora|Saracen|Female|bold|A harbor scout who exposed a smuggling ring that supplied raiders.|Let them watch the wrong doorway.|Secrets only help when someone acts on them.|50
Albion|Mercenary|Garric|Highlander|Male|bold|A contract fighter who now takes only work that protects ordinary folk.|I know the cost of a bad bargain.|Keep your footing and I will keep mine.|57
Albion|Mercenary|Aveline|Briton|Female|wry|A smith's daughter who learned two blades while guarding the forge.|I sharpened both, just in case.|A good edge is useless without good judgment.|51
Albion|Minstrel|Tomasin|Briton|Male|wry|A tavern performer who carried coded warnings between border towns.|The next verse can wait until we are safe.|A song travels where a uniform cannot.|52
Albion|Minstrel|Elowen|Saracen|Female|gentle|A wandering musician who gathers the stories of missing travelers.|I know a tune for long roads.|Let the living finish their stories.|49
Albion|Paladin|Edric|Highlander|Male|steady|A watch captain who stayed with refugees after his company withdrew.|We will move together.|Duty starts with whoever walks beside you.|58
Albion|Paladin|Serelle|Avalonian|Female|gentle|A sworn defender of pilgrim routes who favors restraint over ceremony.|I will hold the line while you decide.|Vows are proven by small choices.|52
Albion|Scout|Hale|Briton|Male|curious|A surveyor whose maps record wells and shelter as carefully as roads.|The ridge should give us a view.|A map is a promise to the next traveler.|50
Albion|Scout|Mirelle|Inconnu|Female|steady|A woodland ranger who guides lost patrols out before nightfall.|I can find the way back.|Tracks tell the truth when witnesses cannot.|48
Albion|Sorcerer|Valric|Avalonian|Male|curious|A court tutor who left to study enchantments where they are cast.|Let us see what still holds here.|Knowledge without care becomes another trap.|53
Albion|Sorcerer|Nerisse|Inconnu|Female|wry|An illusion researcher who prefers field notes to courtly applause.|I have a better question than that.|Appearances are clues, not conclusions.|47
Albion|Theurgist|Orren|Avalonian|Male|steady|A lighthouse keeper who learned elemental wards against coastal storms.|The wind is changing.|Preparation is a kind of courage.|54
Albion|Theurgist|Calice|Briton|Female|curious|A miller's apprentice fascinated by the work of air and stone.|Did you feel that shift?|The smallest current can turn a wheel.|50
Albion|Wizard|Hadric|Briton|Male|bold|A firebreak warden trained to burn away threats before they reach homes.|Give me a clear line.|Fire needs a boundary and so do we.|55
Albion|Wizard|Isolde|Avalonian|Female|steady|A scholar of heat and light who measures twice before casting once.|I can make this precise.|Power is easiest to respect before it is needed.|48
Albion|Necromancer|Lucan|Inconnu|Male|wry|A mortuary record keeper seeking names erased from the parish books.|The dead keep excellent records.|Someone should remember who they were.|47
Albion|Necromancer|Vespera|Saracen|Female|curious|A desert physician studying the boundary between breath and memory.|There is more here than a grave.|Even strange knowledge can serve the living.|52
Albion|Reaver|Cadrin|Briton|Male|bold|A former siege laborer who learned to turn an enemy's fear against them.|I can open a way through.|Walls fail where their keepers stop looking.|56
Albion|Reaver|Zahra|Saracen|Female|steady|A caravan guard who fights in the gaps where shields cannot turn.|Stay close enough for me to cover you.|The best defense knows when to advance.|50
Midgard|Berserker|Torvald|Norseman|Male|bold|A dockworker who guarded winter supplies when raiders broke the ice.|Let us finish this cleanly.|Strength is for carrying people home.|58
Midgard|Berserker|Ragna|Valkyn|Female|wry|A fisher who learned fast blades defending her coastal village.|I have fought in worse weather.|Never mistake noise for nerve.|48
Midgard|Healer|Eydis|Norseman|Female|gentle|A traveling bone setter who remembers each village by its children.|I will watch your breathing.|A hand offered early saves a harder cure.|51
Midgard|Healer|Kjell|Dwarf|Male|steady|A battlefield medic who insists on counting everyone after a fight.|No one gets left on the road.|The tally ends when we are all home.|49
Midgard|Hunter|Asmund|Norseman|Male|curious|A trapline keeper who tracks missing livestock into dangerous country.|There are fresh signs ahead.|Follow the trail, then question it.|55
Midgard|Hunter|Ylva|Valkyn|Female|steady|A ridge scout who knows where snow conceals a safe crossing.|Step where I step.|Silence is useful, but attention is better.|47
Midgard|Runemaster|Sigvar|Dwarf|Male|curious|A stone cutter who found old runes beneath a collapsed road.|These marks were not here by accident.|Stone remembers work long after hands are gone.|48
Midgard|Runemaster|Freydis|Norseman|Female|bold|A rune painter who places warnings where travelers will see them.|I can mark the safe route.|A sign unseen helps no one.|53
Midgard|Shadowblade|Ivarin|Kobold|Male|wry|A tunnel messenger who crossed enemy lines to bring a siege warning.|I already checked the shadows.|Small doors can change a battle.|46
Midgard|Shadowblade|Svala|Valkyn|Female|steady|A night watcher who hunts those who prey on isolated farms.|I will take the quiet side.|No one should fear their own path home.|49
Midgard|Shaman|Brakka|Troll|Male|gentle|A village herb gatherer whose remedies travel farther than he does.|I brought enough for everyone.|Care is stronger when shared.|59
Midgard|Shaman|Kiri|Kobold|Female|curious|A bog naturalist who tests every remedy before recommending it.|Let me see that plant.|The marsh teaches patience to listeners.|46
Midgard|Skald|Gormund|Dwarf|Male|bold|A hall singer who records deeds of the overlooked as well as heroes.|We should give them a story worth telling.|A name survives when someone speaks it.|51
Midgard|Skald|Astrun|Norseman|Female|wry|A road poet whose sharp verses have ended more quarrels than blades.|I promise only one verse on the march.|Words can be lighter than steel and hit harder.|54
Midgard|Spiritmaster|Haldor|Norseman|Male|steady|A winter seer who treats summoned spirits as borrowed help.|I will ask before I call.|A spirit is a guest, not a tool.|56
Midgard|Spiritmaster|Nyra|Kobold|Female|curious|An apprentice searching for the source of voices beneath the hills.|Did you hear the second answer?|The unseen deserves questions, not guesses.|46
Midgard|Thane|Eirikun|Norseman|Male|bold|A coast guard who learned to read storms before they made landfall.|The sky is on our side for now.|Stand firm until the thunder passes.|57
Midgard|Thane|Hildra|Dwarf|Female|steady|A forge keeper who carries old oaths as carefully as her hammer.|I will take the front.|An oath should survive a difficult day.|49
Midgard|Warrior|Bjarni|Dwarf|Male|steady|A bridge keeper who defended the crossing through three harsh winters.|Behind my shield.|A road stays open because someone stays.|50
Midgard|Warrior|Signe|Valkyn|Female|bold|A mountain guide who learned formation fighting in narrow passes.|I know where to plant my feet.|Hold the gap and others can move.|48
Midgard|Bonedancer|Kelda|Troll|Female|gentle|A grave tender who calls on old guardians to protect the young.|We will ask the elders for help.|The past can shelter the future.|58
Midgard|Bonedancer|Orvik|Kobold|Male|wry|A bone carver who learned rituals from stories no one believed.|I brought more than one answer.|Odd stories often hide useful instructions.|46
Midgard|Savage|Fenna|Norseman|Female|bold|A pit fighter who left the ring to guard remote supply trails.|Keep up if you can.|A quick strike can spare a long fight.|54
Midgard|Savage|Terk|Troll|Male|steady|A quarry worker who uses careful rhythm where others expect fury.|I can set the pace.|Control is the hardest part of strength.|60
Hibernia|Bard|Ailwen|Celt|Female|gentle|A village teacher who collects songs that mark safe roads.|We will find our rhythm.|A shared tune makes the miles shorter.|51
Hibernia|Bard|Finnan|Firbolg|Male|wry|A festival drummer who carries news between distant groves.|I will keep the beat quiet.|Even bad news deserves a good listener.|58
Hibernia|Blademaster|Cianor|Celt|Male|bold|A dancing master who learned blades while escorting apprentices home.|Watch the second step.|Balance wins before the first blow.|53
Hibernia|Blademaster|Lethira|Elf|Female|steady|A dueling instructor who teaches patience before speed.|I will cover the opening.|A clean victory needs no flourish.|48
Hibernia|Champion|Brennan|Celt|Male|steady|A grove sentinel who kept a path open during the fires.|I know the ground.|Courage is a route other people can follow.|55
Hibernia|Champion|Saela|Lurikeen|Female|wry|A boundary watcher who learned to disarm boasts and brigands alike.|Loud foes are easier to find.|A small warning can avert a large mistake.|46
Hibernia|Druid|Oriana|Sylvan|Female|gentle|A seed keeper who tends damaged groves and their neighbors.|Let me see who is hurt.|Roots recover when they have room.|56
Hibernia|Druid|Dairel|Celt|Male|curious|A healer cataloging plants brought from the southern islands.|I may know what grows here.|Every new leaf has an old lesson.|52
Hibernia|Eldritch|Caelith|Elf|Female|curious|A sky reader who studies strange lights over old stone circles.|The light has shifted again.|Wonder is useful when paired with caution.|48
Hibernia|Eldritch|Odran|Lurikeen|Male|wry|An observatory keeper who tests every omen against the weather.|That is an interesting shadow.|Not every portent deserves a panic.|46
Hibernia|Enchanter|Eilis|Elf|Female|steady|A craftswoman who binds protective charms into travelers' keepsakes.|I made one for the road.|A small charm is still a promise.|50
Hibernia|Enchanter|Perran|Lurikeen|Male|curious|A glassmaker studying how summoned light takes shape.|May I try a different angle?|A good question changes the whole pattern.|46
Hibernia|Hero|Taran|Firbolg|Male|bold|A timber hauler who became a defender of river crossings.|I can hold this bank.|The river moves, so we must watch both shores.|60
Hibernia|Hero|Nessa|Sylvan|Female|steady|A grove guardian who measures victory by those she brings back.|Stay within my reach.|A shield is only as wide as our care.|56
Hibernia|Mentalist|Rianel|Celt|Male|gentle|A dream interpreter who helps survivors sleep after raids.|Speak plainly and I will listen.|A clear thought can be a shelter.|53
Hibernia|Mentalist|Mabryn|Lurikeen|Female|wry|A puzzle maker who found that minds under strain need rest.|I have a simpler solution somewhere.|A clever plan should still leave room to breathe.|46
Hibernia|Nightshade|Sorcha|Elf|Female|steady|A watcher's daughter who learned to track threats without alarming homes.|I will keep to the edge.|The warning matters more than the chase.|48
Hibernia|Nightshade|Cairn|Lurikeen|Male|bold|A hidden-path guide who rescues travelers from occupied roads.|I know another way in.|An unseen door is still a door.|46
Hibernia|Ranger|Eogan|Celt|Male|curious|A woodland surveyor who records animal paths and ruined bridges.|The tracks split here.|The forest is never truly empty.|54
Hibernia|Ranger|Lirith|Elf|Female|steady|A bowyer who tests her work on long patrols through the border hills.|I trust this bow.|Careful hands make the longer shot.|49
Hibernia|Warden|Faelan|Firbolg|Male|gentle|A village protector who knows every household along the old road.|There is space behind me.|A boundary can be kind as well as firm.|58
Hibernia|Warden|Aderyn|Sylvan|Female|steady|A grove mason repairing shelters before storms arrive.|I built this to last.|Protection begins before the danger comes.|55
Hibernia|Animist|Iona|Celt|Female|curious|A mushroom grower mapping what thrives after a forest fire.|Look at what has returned.|Growth rarely asks our permission.|52
Hibernia|Animist|Muiren|Sylvan|Male|wry|A marsh keeper who warns travelers about charming but unsafe clearings.|That patch is less friendly than it looks.|The ground has opinions too.|56
Hibernia|Valewalker|Brecan|Firbolg|Male|steady|An orchard keeper who guards the boundary between wild and tended land.|I know this season's work.|The soil remembers what we leave behind.|59
Hibernia|Valewalker|Siofra|Celt|Female|bold|A harvest runner who learned to face the threats beyond the fields.|We can move before they do.|The next harvest needs us today.|51
""";

        public static readonly IReadOnlyList<Character> All = Load();
        private static readonly IReadOnlyDictionary<string, Character> ByKey =
            All.ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);

        public static Character Find(string key) =>
            key != null && ByKey.TryGetValue(key, out Character entry) ? entry : null;

        public static Character FindByName(string name) =>
            All.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<Character> Load()
        {
            var result = new List<Character>();
            var counts = new Dictionary<(eRealm, eCharacterClass), int>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in Cast.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] fields = row.Split('|');
                if (fields.Length != 10 || !Enum.TryParse(fields[0], out eRealm realm) ||
                    !Enum.TryParse(fields[1], out eCharacterClass characterClass) ||
                    !Enum.TryParse(fields[3], out eRace race) || !Enum.TryParse(fields[4], out eGender gender) ||
                    !byte.TryParse(fields[9], out byte size) || !names.Add(fields[2]) ||
                    !AutonomousBotIdentityGenerator.GetEraClasses(realm).Contains(characterClass) ||
                    !AutonomousBotIdentityGenerator.GetEligibleRaces(characterClass).Contains(race))
                    throw new InvalidOperationException($"Invalid authored companion row: {row}");
                var pair = (realm, characterClass);
                counts[pair] = counts.GetValueOrDefault(pair) + 1;
                string key = $"{realm.ToString().ToLowerInvariant()}-{characterClass.ToString().ToLowerInvariant()}-{fields[2].ToLowerInvariant()}";
                result.Add(new Character(key, realm, characterClass, fields[2], race, gender,
                    fields[5], fields[6], fields[7], fields[8], size));
            }
            if (counts.Count != 39 || counts.Any(pair => pair.Value != 2) || result.Count != 78)
                throw new InvalidOperationException("The authored companion catalog must contain two people for each Classic + SI class.");
            return result;
        }
    }
}
