using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>
    /// Project-recommended automatic builds, several per class where research
    /// supports them. Endgame targets are sourced from the companion research
    /// record; every intermediate level is derived by the documented,
    /// no-autotrain progression algorithm below. A class's first build is its
    /// default. Companions save the plan ID they follow, so published IDs,
    /// including every original general-pve-v1 ID, must never be renamed.
    /// </summary>
    public static class CompanionBuildPlanCatalog
    {
        private static readonly IReadOnlyDictionary<eCharacterClass, IReadOnlyList<CompanionBuildPlan>> Plans = Index(
            Original(eCharacterClass.Armsman, 20, "polearm", "Polearm and shield", "tank and group guard",
                ("Polearm", 50), ("Shields", 42), ("Slash", 39), ("Parry", 5)),
            Variant(eCharacterClass.Armsman, 20, "twohanded", "Two-handed and shield", "two-handed damage and group guard",
                ("Two Handed", 50), ("Shields", 42), ("Slash", 39), ("Parry", 5)),
            Variant(eCharacterClass.Armsman, 20, "onehanded", "One-handed and shield", "one-handed damage and defensive guard",
                ("Slash", 50), ("Shields", 42), ("Parry", 39)),
            Original(eCharacterClass.Cabalist, 10, "body", "Body and Spirit", "pet caster and farmer",
                ("Body Magic", 34), ("Spirit Magic", 33), ("Matter Magic", 25)),
            Variant(eCharacterClass.Cabalist, 10, "matter", "Matter (damage-over-time)", "pet caster with damage-over-time",
                ("Matter Magic", 50), ("Body Magic", 20), ("Spirit Magic", 3)),
            Variant(eCharacterClass.Cabalist, 10, "spirit", "Spirit (pet support)", "pet support and body debuff damage",
                ("Spirit Magic", 46), ("Body Magic", 28)),
            Original(eCharacterClass.Cleric, 10, "enhancement", "Enhancement (buffer)", "healer and buffer",
                ("Enhancement", 42), ("Rejuvenation", 33)),
            Variant(eCharacterClass.Cleric, 10, "rejuvenation", "Rejuvenation (healer)", "main healer and buffer",
                ("Rejuvenation", 36), ("Enhancement", 40)),
            Variant(eCharacterClass.Cleric, 10, "smite", "Smite (damage and support)", "ranged damage with healing and buffs",
                ("Smite", 44), ("Rejuvenation", 23), ("Enhancement", 21)),
            Original(eCharacterClass.Friar, 15, "group", "Group support", "melee support and healer",
                ("Enhancement", 45), ("Staff", 29), ("Rejuvenation", 34), ("Parry", 17)),
            Variant(eCharacterClass.Friar, 15, "solo", "Staff (solo)", "melee damage with support",
                ("Enhancement", 45), ("Staff", 39), ("Rejuvenation", 25), ("Parry", 12)),
            Variant(eCharacterClass.Friar, 15, "healer", "Rejuvenation (healer)", "main healer with melee support",
                ("Rejuvenation", 44), ("Enhancement", 37), ("Staff", 29), ("Parry", 13)),
            Original(eCharacterClass.Infiltrator, 25, "thrust", "Thrust assassin", "assassin",
                ("Thrust", 50), ("Critical Strike", 44), ("Stealth", 36), ("Envenom", 36), ("Dual Wield", 14)),
            Variant(eCharacterClass.Infiltrator, 25, "slash", "Slash assassin", "assassin with slash styles",
                ("Slash", 50), ("Critical Strike", 44), ("Stealth", 36), ("Envenom", 36), ("Dual Wield", 14)),
            Variant(eCharacterClass.Infiltrator, 25, "highcrit", "High Critical Strike dual wield", "critical-strike damage with stronger offhand use",
                ("Critical Strike", 50), ("Thrust", 35), ("Dual Wield", 36), ("Stealth", 33), ("Envenom", 33)),
            Original(eCharacterClass.Mercenary, 20, "dualwield", "Dual wield and shield", "dual-wield melee damage",
                ("Dual Wield", 50), ("Shields", 42), ("Slash", 36), ("Parry", 16)),
            Variant(eCharacterClass.Mercenary, 20, "damage", "Dual wield damage", "dual-wield damage with stronger weapon styles",
                ("Dual Wield", 50), ("Slash", 50), ("Parry", 28)),
            Variant(eCharacterClass.Mercenary, 20, "split", "Split weapon and shield", "dual-wield damage with shield control and two damage types",
                ("Dual Wield", 50), ("Shields", 42), ("Slash", 27), ("Thrust", 27), ("Parry", 7)),
            Original(eCharacterClass.Minstrel, 15, "instruments", "Instruments and thrust", "songs, charm, and group utility",
                ("Instruments", 50), ("Thrust", 43)),
            Variant(eCharacterClass.Minstrel, 15, "stealthgroup", "Stealth group support", "songs, charm, stealth, and melee support",
                ("Instruments", 50), ("Stealth", 33), ("Thrust", 27)),
            Variant(eCharacterClass.Minstrel, 15, "smallgroup", "Small-group compromise", "songs, charm, and stronger melee support",
                ("Instruments", 50), ("Thrust", 33), ("Stealth", 27)),
            Original(eCharacterClass.Necromancer, 10, "deathsight", "Deathsight focus", "Deathsight-primary specialization allocation",
                ("Deathsight", 49), ("Painworking", 22)),
            Variant(eCharacterClass.Necromancer, 10, "painworking", "Painworking focus", "Painworking-primary specialization allocation",
                ("Painworking", 49), ("Deathsight", 22)),
            Variant(eCharacterClass.Necromancer, 10, "hybrid", "Deathsight and Painworking", "mixed specialization allocation",
                ("Deathsight", 38), ("Painworking", 38)),
            Original(eCharacterClass.Paladin, 20, "chants", "Chants and shield", "defensive tank and group support",
                ("Chants", 46), ("Thrust", 44), ("Shields", 42)),
            Variant(eCharacterClass.Paladin, 20, "twohanded", "Two-handed hybrid", "two-handed damage and group support",
                ("Two Handed", 39), ("Shields", 42), ("Chants", 46), ("Slash", 20)),
            Variant(eCharacterClass.Paladin, 20, "thrustguard", "Thrust and shield guard", "shield guard with parry and thrust snare styles",
                ("Thrust", 44), ("Shields", 42), ("Chants", 42), ("Parry", 17)),
            Original(eCharacterClass.Reaver, 20, "flexible", "Flexible and shield", "shield control and melee pressure",
                ("Flexible", 50), ("Shields", 42), ("Soulrending", 39), ("Parry", 5)),
            Variant(eCharacterClass.Reaver, 20, "parry", "Flexible and parry", "shield control and stronger parry defense",
                ("Flexible", 50), ("Shields", 42), ("Soulrending", 36), ("Parry", 16)),
            Variant(eCharacterClass.Reaver, 20, "soulrending", "Flexible and Soulrending", "flexible melee with stronger Soulrending damage",
                ("Flexible", 50), ("Soulrending", 50), ("Shields", 28), ("Parry", 6)),
            Original(eCharacterClass.Scout, 20, "bow", "Longbow", "ranged and melee hybrid",
                ("Longbows", 50), ("Shields", 42), ("Stealth", 35), ("Slash", 18)),
            Variant(eCharacterClass.Scout, 20, "melee", "Shield and slash", "melee and ranged hybrid",
                ("Shields", 42), ("Slash", 40), ("Stealth", 35), ("Longbows", 35)),
            Variant(eCharacterClass.Scout, 20, "hybrid", "Bow and shield hybrid", "balanced ranged and melee damage with shield control",
                ("Longbows", 44), ("Shields", 42), ("Stealth", 35), ("Slash", 29)),
            Original(eCharacterClass.Sorcerer, 10, "balanced", "Body and Mind", "crowd control and ranged damage",
                ("Body Magic", 39), ("Mind Magic", 37)),
            Variant(eCharacterClass.Sorcerer, 10, "body", "Body (damage)", "ranged damage and debuffs",
                ("Body Magic", 45), ("Mind Magic", 24), ("Matter Magic", 17)),
            Variant(eCharacterClass.Sorcerer, 10, "matter", "Matter (damage-over-time)", "pet charm and damage-over-time",
                ("Matter Magic", 45), ("Body Magic", 24), ("Mind Magic", 17)),
            Original(eCharacterClass.Theurgist, 10, "earth", "Earth and Wind", "pet interrupts and caster support",
                ("Earth Magic", 40), ("Wind Magic", 36)),
            Variant(eCharacterClass.Theurgist, 10, "ice", "Ice (group leveling)", "pet caster and group damage",
                ("Cold Magic", 50), ("Earth Magic", 20)),
            Variant(eCharacterClass.Theurgist, 10, "air", "Air (crowd control and interrupts)", "ranged damage and group interruption",
                ("Wind Magic", 45), ("Earth Magic", 28), ("Cold Magic", 7)),
            Variant(eCharacterClass.Wizard, 10, "fire", "Fire (single target)", "ranged single-target damage",
                ("Fire Magic", 50), ("Earth Magic", 18), ("Cold Magic", 9)),
            Variant(eCharacterClass.Wizard, 10, "ice", "Ice (area damage)", "ranged and area damage",
                ("Cold Magic", 50), ("Earth Magic", 20)),
            Variant(eCharacterClass.Wizard, 10, "earth", "Earth (utility)", "ranged damage and utility",
                ("Earth Magic", 48), ("Fire Magic", 23), ("Cold Magic", 7)),
            Original(eCharacterClass.Berserker, 20, "sword", "Sword and left axe", "dual-wield melee damage",
                ("Sword", 50), ("Left Axe", 50), ("Parry", 28)),
            Variant(eCharacterClass.Berserker, 20, "split", "Axe and hammer split", "dual-wield damage with slash and crush styles",
                ("Axe", 39), ("Hammer", 39), ("Left Axe", 50), ("Parry", 16)),
            Variant(eCharacterClass.Berserker, 20, "defensive", "Defensive axe and hammer", "dual-wield damage with stronger parry",
                ("Axe", 39), ("Hammer", 29), ("Left Axe", 50), ("Parry", 30)),
            Original(eCharacterClass.Bonedancer, 10, "suppression", "Suppression", "pet caster with sustain",
                ("Suppression", 47), ("Darkness", 26)),
            Variant(eCharacterClass.Bonedancer, 10, "darkness", "Darkness and bone pets", "direct damage and pet utility",
                ("Darkness", 50), ("Suppression", 20), ("Bone Army", 4)),
            Variant(eCharacterClass.Bonedancer, 10, "bonearmy", "Bone Army and suppression", "pet-focused caster with area damage",
                ("Bone Army", 48), ("Suppression", 24), ("Darkness", 6)),
            Original(eCharacterClass.Healer, 10, "trispec", "Tri-spec", "healer, buffer, and crowd control",
                ("Pacification", 38), ("Mending", 33), ("Augmentation", 19)),
            Variant(eCharacterClass.Healer, 10, "mending", "Mending (healer)", "main healer",
                ("Mending", 50), ("Augmentation", 20)),
            Variant(eCharacterClass.Healer, 10, "augmentation", "Augmentation (buffer)", "buffer",
                ("Augmentation", 50), ("Mending", 20)),
            Variant(eCharacterClass.Healer, 10, "pacification", "Pacification (crowd control)", "crowd control",
                ("Pacification", 44), ("Mending", 30), ("Augmentation", 8)),
            Original(eCharacterClass.Hunter, 20, "spear", "Spear and Beastcraft", "pet and ranged/melee hybrid",
                ("Beastcraft", 50), ("Spear", 44), ("Stealth", 36), ("Composite Bow", 9)),
            Variant(eCharacterClass.Hunter, 20, "archery", "Archery", "ranged damage with a pet",
                ("Beastcraft", 40), ("Composite Bow", 35), ("Spear", 50), ("Stealth", 22)),
            Variant(eCharacterClass.Hunter, 20, "hybrid", "Bow, spear, and pet hybrid", "balanced ranged, melee, pet, and stealth skills",
                ("Beastcraft", 42), ("Composite Bow", 35), ("Stealth", 36), ("Spear", 39)),
            Original(eCharacterClass.Runemaster, 10, "darkness", "Darkness (damage)", "ranged damage and spell utility",
                ("Darkness", 47), ("Suppression", 26)),
            Variant(eCharacterClass.Runemaster, 10, "suppression", "Suppression (group support)", "group damage and utility",
                ("Suppression", 50), ("Darkness", 20)),
            Variant(eCharacterClass.Runemaster, 10, "runecarving", "Runecarving (bolts and area utility)", "ranged damage and ground-targeted area utility",
                ("Runecarving", 48), ("Darkness", 24), ("Suppression", 6)),
            Original(eCharacterClass.Savage, 15, "savagery", "Savagery and hand to hand", "melee damage with self-buffs",
                ("Savagery", 49), ("Hand to Hand", 44)),
            Variant(eCharacterClass.Savage, 15, "hammer", "Hammer and Savagery", "two-handed melee damage with stronger defense",
                ("Hammer", 39), ("Savagery", 49), ("Parry", 20)),
            Variant(eCharacterClass.Savage, 15, "handtohand", "Hand to Hand (maximum styles)", "dual-wield burst damage with the highest Hand to Hand style",
                ("Hand to Hand", 50), ("Savagery", 42), ("Parry", 9)),
            Original(eCharacterClass.Shadowblade, 22, "leftaxe", "Left axe assassin", "assassin",
                ("Left Axe", 50), ("Sword", 35), ("Stealth", 36), ("Envenom", 36), ("Critical Strike", 5)),
            Variant(eCharacterClass.Shadowblade, 22, "critblade", "Critical Strike assassin", "two-handed stealth opener and poison damage",
                ("Critical Strike", 44), ("Axe", 39), ("Stealth", 38), ("Envenom", 38)),
            Variant(eCharacterClass.Shadowblade, 22, "fivespec", "Five-spec assassin", "dual-wield styles with critical strikes, stealth, and poison",
                ("Critical Strike", 34), ("Left Axe", 39), ("Stealth", 36), ("Axe", 35), ("Envenom", 33)),
            Original(eCharacterClass.Shaman, 10, "augmentation", "Augmentation (buffer)", "buffer, healer, and ranged damage-over-time",
                ("Augmentation", 46), ("Subterranean", 27), ("Mending", 8)),
            Variant(eCharacterClass.Shaman, 10, "cave", "Cave (damage-over-time)", "ranged damage-over-time and buffs",
                ("Subterranean", 50), ("Augmentation", 20)),
            Variant(eCharacterClass.Shaman, 10, "mending", "Mending (healer)", "main healer with group buffs and damage-over-time",
                ("Mending", 43), ("Augmentation", 32), ("Subterranean", 6)),
            Original(eCharacterClass.Skald, 15, "battlesongs", "Battlesongs and hammer", "group speed and melee support",
                ("Battlesongs", 46), ("Hammer", 44), ("Parry", 17)),
            Variant(eCharacterClass.Skald, 15, "songs", "Battlesongs (group support)", "stronger group songs and ranged interrupts",
                ("Battlesongs", 50), ("Hammer", 39), ("Parry", 18)),
            Variant(eCharacterClass.Skald, 15, "hammer", "Hammer (weapon damage)", "weapon-focused melee damage and group speed",
                ("Battlesongs", 43), ("Hammer", 50), ("Parry", 2)),
            Original(eCharacterClass.Spiritmaster, 10, "darkness", "Darkness (pet caster)", "pet caster with Suppression area damage",
                ("Darkness", 47), ("Suppression", 26)),
            Variant(eCharacterClass.Spiritmaster, 10, "suppression", "Suppression (bomb)", "stronger PBAoE damage and group utility",
                ("Suppression", 50), ("Darkness", 20)),
            Variant(eCharacterClass.Spiritmaster, 10, "summoning", "Summoning (pet)", "pet caster",
                ("Summoning", 50), ("Darkness", 20)),
            Original(eCharacterClass.Thane, 20, "stormcalling", "Stormcalling and shield", "hybrid tank and support damage",
                ("Stormcalling", 46), ("Shields", 42), ("Hammer", 39), ("Parry", 20)),
            Variant(eCharacterClass.Thane, 20, "melee", "Hammer and shield", "melee damage with Stormcalling and guard",
                ("Hammer", 50), ("Shields", 42), ("Stormcalling", 38), ("Parry", 10)),
            Variant(eCharacterClass.Thane, 20, "twohanded", "Two-handed hammer and Stormcalling", "two-handed melee and ranged spell damage",
                ("Hammer", 50), ("Stormcalling", 50), ("Parry", 28)),
            Original(eCharacterClass.Warrior, 20, "sword", "Sword and shield", "tank and group guard",
                ("Sword", 50), ("Shields", 42), ("Hammer", 39)),
            Variant(eCharacterClass.Warrior, 20, "hammer", "Hammer and shield", "shield tank with hammer control and damage",
                ("Hammer", 50), ("Shields", 50), ("Parry", 28)),
            Variant(eCharacterClass.Warrior, 20, "split", "Hammer and sword split", "shield tank with crush and slash styles",
                ("Hammer", 50), ("Shields", 42), ("Sword", 39)),
            Variant(eCharacterClass.Animist, 10, "creeping", "Creeping (turret farm)", "turret caster and area damage",
                ("Creeping Path", 39), ("Arboreal Path", 36), ("Verdant Path", 7)),
            Variant(eCharacterClass.Animist, 10, "arboreal", "Arboreal (main pet)", "pet caster and ranged damage",
                ("Arboreal Path", 48), ("Creeping Path", 23), ("Verdant Path", 7)),
            Variant(eCharacterClass.Animist, 10, "creepfarm", "Creeping (farm focus)", "turret farming with stronger Creeping specialization",
                ("Creeping Path", 50), ("Arboreal Path", 20)),
            Original(eCharacterClass.Bard, 15, "nurture", "Nurture (support)", "group support and healer",
                ("Nurture", 43), ("Music", 37), ("Regrowth", 33)),
            Variant(eCharacterClass.Bard, 15, "music", "Music (crowd control)", "crowd control and group support",
                ("Music", 47), ("Nurture", 43), ("Regrowth", 16)),
            Variant(eCharacterClass.Bard, 15, "battle", "Battle (melee support)", "melee damage, group buffs, healing, and control",
                ("Nurture", 43), ("Music", 37), ("Regrowth", 16), ("Blunt", 29)),
            Variant(eCharacterClass.Blademaster, 20, "group", "Celtic Dual and shield", "dual-wield group damage with shield control",
                ("Celtic Dual", 50), ("Shields", 42), ("Blades", 39)),
            Variant(eCharacterClass.Blademaster, 20, "damage", "Dual-wield damage", "maximum weapon and Celtic Dual damage with parry",
                ("Celtic Dual", 50), ("Blades", 50), ("Parry", 28)),
            Variant(eCharacterClass.Blademaster, 20, "guard", "Shield guard", "stronger shield defense with dual-wield damage",
                ("Shields", 50), ("Celtic Dual", 42), ("Blades", 39)),
            Original(eCharacterClass.Champion, 20, "valor", "Valor and shield", "hybrid tank, debuffs, and interrupts",
                ("Valor", 50), ("Shields", 42), ("Large Weapons", 39)),
            Variant(eCharacterClass.Champion, 20, "large", "Large Weapons (group damage)", "two-handed damage with Valor debuffs and shield control",
                ("Large Weapons", 50), ("Shields", 42), ("Valor", 39)),
            Variant(eCharacterClass.Champion, 20, "damage", "Valor and Large Weapons", "two-handed damage and debuffs without a shield focus",
                ("Valor", 50), ("Large Weapons", 50), ("Parry", 28)),
            Original(eCharacterClass.Druid, 10, "nurture", "Nurture (buffer)", "healer and buffer",
                ("Nurture", 42), ("Regrowth", 33), ("Nature", 7)),
            Variant(eCharacterClass.Druid, 10, "regrowth", "Regrowth (healer)", "main healer and buffer",
                ("Regrowth", 35), ("Nurture", 40), ("Nature", 9)),
            Variant(eCharacterClass.Druid, 10, "nature", "Nature (solo)", "nature magic, a pet, and group buffs",
                ("Nature", 50), ("Nurture", 20)),
            Original(eCharacterClass.Eldritch, 10, "mana", "Mana (group damage)", "group caster damage and utility",
                ("Mana", 50), ("Light", 20)),
            Variant(eCharacterClass.Eldritch, 10, "light", "Light (single target)", "ranged single-target damage",
                ("Light", 46), ("Mana", 28)),
            Variant(eCharacterClass.Eldritch, 10, "void", "Void (bolt and debuff)", "long-range damage and resistance debuffs",
                ("Void", 46), ("Mana", 28)),
            Original(eCharacterClass.Enchanter, 10, "mana", "Mana (pet support)", "pet support and caster damage",
                ("Mana", 49), ("Light", 22)),
            Variant(eCharacterClass.Enchanter, 10, "light", "Light (ranged damage)", "ranged damage with pet and spell support",
                ("Light", 45), ("Mana", 27), ("Enchantments", 12)),
            Variant(eCharacterClass.Enchanter, 10, "enchantment", "Enchantments (pet focus)", "strong pet enchantments with supporting magic",
                ("Enchantments", 50), ("Mana", 20), ("Light", 4)),
            Variant(eCharacterClass.Hero, 20, "shield", "Celtic Spear and shield", "shield guard with Celtic Spear damage",
                ("Celtic Spear", 50), ("Shields", 50), ("Blades", 25), ("Parry", 14)),
            Variant(eCharacterClass.Hero, 20, "largeweapons", "Large Weapons hybrid", "two-handed damage with shield guard and Celtic Spear styles",
                ("Large Weapons", 50), ("Shields", 42), ("Celtic Spear", 38), ("Blades", 10)),
            Variant(eCharacterClass.Hero, 20, "celticspear", "Celtic Spear hybrid", "spear damage with shield guard and Large Weapons styles",
                ("Celtic Spear", 50), ("Shields", 42), ("Large Weapons", 38), ("Blades", 10)),
            Original(eCharacterClass.Mentalist, 10, "light", "Light and Mentalism", "healer support and ranged damage",
                ("Light", 46), ("Mentalism", 28), ("Mana", 4)),
            Variant(eCharacterClass.Mentalist, 10, "mana", "Mana (group support)", "area damage-over-time, healing over time, and mana regeneration",
                ("Mana", 50), ("Light", 20)),
            Variant(eCharacterClass.Mentalist, 10, "mentalism", "Mentalism (healing and control)", "healing, mesmerize, and support damage",
                ("Mentalism", 50), ("Light", 20), ("Mana", 4)),
            Original(eCharacterClass.Nightshade, 22, "piercing", "Piercing assassin", "assassin",
                ("Critical Strike", 44), ("Piercing", 36), ("Stealth", 35), ("Envenom", 35), ("Celtic Dual", 25)),
            Variant(eCharacterClass.Nightshade, 22, "dualfocus", "Celtic Dual focus", "higher dual-wield damage with a reduced critical-strike line",
                ("Celtic Dual", 44), ("Piercing", 36), ("Stealth", 35), ("Envenom", 35), ("Critical Strike", 24)),
            Variant(eCharacterClass.Nightshade, 22, "blades", "Blades assassin", "slash damage with critical strikes, stealth, and poison",
                ("Critical Strike", 44), ("Blades", 36), ("Stealth", 35), ("Envenom", 35), ("Celtic Dual", 25)),
            Original(eCharacterClass.Ranger, 20, "melee", "Celtic dual (melee)", "ranged/melee hybrid with self-buffs",
                ("Celtic Dual", 42), ("Pathfinding", 40), ("Piercing", 35), ("Stealth", 33), ("Recurve Bow", 11)),
            Variant(eCharacterClass.Ranger, 20, "archery", "Archery", "ranged damage with self-buffs",
                ("Recurve Bow", 35), ("Pathfinding", 40), ("Piercing", 39), ("Stealth", 33), ("Celtic Dual", 19)),
            Variant(eCharacterClass.Ranger, 20, "bowfocus", "Recurve Bow focus", "higher bow damage with self-buffs and melee backup",
                ("Recurve Bow", 45), ("Pathfinding", 40), ("Piercing", 24), ("Celtic Dual", 19), ("Stealth", 35)),
            Original(eCharacterClass.Valewalker, 15, "scythe", "Scythe and Arboreal", "melee and spell hybrid",
                ("Scythe", 50), ("Arboreal Path", 38), ("Parry", 20)),
            Variant(eCharacterClass.Valewalker, 15, "pve", "Scythe (PvE focus)", "high scythe damage with Arboreal buffs and spells",
                ("Scythe", 50), ("Arboreal Path", 43), ("Parry", 2)),
            Variant(eCharacterClass.Valewalker, 15, "arboreal", "Arboreal (spell focus)", "stronger Arboreal spells with a viable scythe line",
                ("Arboreal Path", 50), ("Scythe", 39), ("Parry", 18)),
            Original(eCharacterClass.Warden, 15, "nurture", "Nurture (support)", "defensive melee and group support",
                ("Nurture", 49), ("Regrowth", 33), ("Blades", 25), ("Parry", 14)),
            Variant(eCharacterClass.Warden, 15, "battle", "Battle Warden", "group buffs, melee damage, and healing",
                ("Nurture", 49), ("Blades", 39), ("Regrowth", 16), ("Parry", 10)),
            Variant(eCharacterClass.Warden, 15, "regrowth", "Regrowth (healer)", "strong group healing with Nurture buffs",
                ("Nurture", 45), ("Regrowth", 47), ("Parry", 9)));

        private static CompanionBuildPlan Original(eCharacterClass characterClass, int expectedMultiplier, string key,
            string name, string role, params (string Specialization, int Level)[] targets) =>
            Create(characterClass, $"general-pve-v1-{characterClass.ToString().ToLowerInvariant()}", expectedMultiplier,
                key, name, role, targets);

        private static CompanionBuildPlan Variant(eCharacterClass characterClass, int expectedMultiplier, string key,
            string name, string role, params (string Specialization, int Level)[] targets) =>
            Create(characterClass, $"{key}-v1-{characterClass.ToString().ToLowerInvariant()}", expectedMultiplier,
                key, name, role, targets);

        private static CompanionBuildPlan Create(eCharacterClass characterClass, string id, int expectedMultiplier,
            string key, string name, string role, (string Specialization, int Level)[] targets)
        {
            (BotPveGroupRole primaryRole, bool crowdControlDuty) = GroupRole(characterClass, key);
            return new(id, characterClass, key, name, role, primaryRole, crowdControlDuty, expectedMultiplier,
                targets.Select(target => new CompanionBuildRank(target.Specialization, target.Level)).ToArray());
        }

        /// <summary>
        /// The group role a build applies when it is chosen. The owner's Healer
        /// mapping (2026-09-24) is authoritative; other builds whose primary job
        /// differs from the class default are listed here, and the rest keep the
        /// class default. A hybrid build keeps one saved role and adds control as
        /// a second duty.
        /// </summary>
        private static (BotPveGroupRole Role, bool CrowdControlDuty) GroupRole(eCharacterClass characterClass, string key) =>
            (characterClass, key) switch
            {
                (eCharacterClass.Healer, "trispec") => (BotPveGroupRole.Healer, true),
                (eCharacterClass.Healer, "mending") => (BotPveGroupRole.Healer, false),
                (eCharacterClass.Healer, "augmentation") => (BotPveGroupRole.Buffer, false),
                (eCharacterClass.Healer, "pacification") => (BotPveGroupRole.CrowdControl, false),
                (eCharacterClass.Sorcerer, "balanced") => (BotPveGroupRole.CrowdControl, false),
                (eCharacterClass.Sorcerer, "matter") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Bard, "nurture") => (BotPveGroupRole.Buffer, false),
                (eCharacterClass.Bard, "music") => (BotPveGroupRole.CrowdControl, false),
                (eCharacterClass.Bard, "battle") => (BotPveGroupRole.Attacker, true),
                (eCharacterClass.Mentalist, "mentalism") => (BotPveGroupRole.CrowdControl, false),
                (eCharacterClass.Druid, "nature") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Warden, "nurture") => (BotPveGroupRole.Buffer, false),
                (eCharacterClass.Warden, "battle") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Warden, "regrowth") => (BotPveGroupRole.Healer, false),
                (eCharacterClass.Shaman, "augmentation") => (BotPveGroupRole.Buffer, false),
                (eCharacterClass.Shaman, "mending") => (BotPveGroupRole.Healer, false),
                (eCharacterClass.Skald, "songs") => (BotPveGroupRole.Buffer, false),
                (eCharacterClass.Thane, "twohanded") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Friar, "group") => (BotPveGroupRole.Healer, false),
                (eCharacterClass.Friar, "healer") => (BotPveGroupRole.Healer, false),
                (eCharacterClass.Armsman, "twohanded") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Cleric, "smite") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Paladin, "twohanded") => (BotPveGroupRole.Attacker, false),
                (eCharacterClass.Reaver, "soulrending") => (BotPveGroupRole.Attacker, false),
                _ => (DefaultRole(characterClass), false),
            };

        private static BotPveGroupRole DefaultRole(eCharacterClass characterClass) =>
            BotPartyRoles.TryParseRole(BotPartyRoles.DefaultPreference(characterClass), out BotPveGroupRole role)
                ? role : BotPveGroupRole.Attacker;

        private static IReadOnlyDictionary<eCharacterClass, IReadOnlyList<CompanionBuildPlan>> Index(
            params CompanionBuildPlan[] plans) =>
            plans.GroupBy(plan => plan.CharacterClass)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CompanionBuildPlan>)group.ToArray());

        /// <summary>Returns the class's default build ID.</summary>
        public static bool TryGetEnabledPlan(eCharacterClass characterClass, out string planId)
        {
            bool found = TryGetPlan(characterClass, out CompanionBuildPlan plan);
            planId = found ? plan.Id : string.Empty;
            return found;
        }

        /// <summary>Returns the class's default build.</summary>
        public static bool TryGetPlan(eCharacterClass characterClass, out CompanionBuildPlan plan)
        {
            plan = GetPlans(characterClass).FirstOrDefault();
            return plan != null;
        }

        public static IReadOnlyList<CompanionBuildPlan> GetPlans(eCharacterClass characterClass) =>
            Plans.TryGetValue(characterClass, out IReadOnlyList<CompanionBuildPlan> plans)
                ? plans : Array.Empty<CompanionBuildPlan>();

        /// <summary>Finds the saved build by its exact, stable plan ID.</summary>
        public static bool TryGetPlanById(eCharacterClass characterClass, string planId, out CompanionBuildPlan plan)
        {
            plan = GetPlans(characterClass).FirstOrDefault(entry => string.Equals(entry.Id, planId, StringComparison.Ordinal));
            return plan != null;
        }

        /// <summary>Finds a build from player input: its short key, plan ID, or display name.</summary>
        public static bool TryFindPlan(eCharacterClass characterClass, string query, out CompanionBuildPlan plan)
        {
            string wanted = (query ?? string.Empty).Trim();
            plan = GetPlans(characterClass).FirstOrDefault(entry =>
                string.Equals(entry.Key, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Id, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, wanted, StringComparison.OrdinalIgnoreCase));
            return plan != null && wanted.Length > 0;
        }

        public static IReadOnlyCollection<CompanionBuildPlan> GetEnabledPlans() =>
            Plans.Values.SelectMany(plans => plans).ToArray();

        public static bool TryValidateRuntimePlan(eCharacterClass characterClass, CompanionBuildPlan plan,
            out string blocker)
        {
            blocker = string.Empty;
            if (plan == null || !TryGetPlanById(characterClass, plan.Id, out _))
            {
                blocker = "the selected plan is not an enabled build for this class";
                return false;
            }

            Dictionary<string, Specialization> career = SkillBase.GetSpecializationCareer((int)characterClass)
                .Keys.Where(spec => spec.Trainable)
                .ToDictionary(spec => spec.KeyName, StringComparer.OrdinalIgnoreCase);
            foreach (CompanionBuildRank target in plan.TargetAllocations)
            {
                if (!career.TryGetValue(target.Specialization, out Specialization specialization))
                {
                    blocker = $"runtime class data has no trainable {target.Specialization} specialization";
                    return false;
                }

                int classId = (int)characterClass;
                int abilityRank = SkillBase.GetSpecAbilityList(target.Specialization, classId)
                    .Where(ability => ability.SpecLevelRequirement <= target.Level)
                    .Select(ability => (int)ability.SpecLevelRequirement).DefaultIfEmpty(0).Max();
                List<Style> styles = SkillBase.GetStyleList(target.Specialization, classId);
                if (styles.Count == 0)
                    styles = SkillBase.GetStyleList(target.Specialization, 0);
                int styleRank = styles.Where(style => style.SpecLevelRequirement <= target.Level)
                    .Select(style => style.SpecLevelRequirement).DefaultIfEmpty(0).Max();
                int spellRank = SkillBase.GetSpecsSpellLines(target.Specialization)
                    .Where(entry => entry.Item2 == 0 || entry.Item2 == classId)
                    .SelectMany(entry => SkillBase.GetSpellList(entry.Item1.KeyName))
                    .Where(spell => spell.Level <= target.Level)
                    .Select(spell => spell.Level).DefaultIfEmpty(0).Max();
                int rankedData = Math.Max(abilityRank, Math.Max(styleRank, spellRank));

                if (rankedData == 0 && !IsCareerOnlyRankLine(characterClass, target.Specialization))
                {
                    blocker = $"runtime ability, spell, and style tables have no ranked entries for {target.Specialization}";
                    return false;
                }
            }

            return true;
        }

        private static bool IsCareerOnlyRankLine(eCharacterClass characterClass, string specialization) =>
            specialization is "Parry" or "Stealth" ||
            characterClass == eCharacterClass.Scout && specialization == "Longbows" ||
            characterClass == eCharacterClass.Hunter && specialization == "Composite Bow" ||
            characterClass == eCharacterClass.Ranger && specialization == "Recurve Bow";

        public static string GetBlocker(eCharacterClass characterClass)
        {
            if (TryGetPlan(characterClass, out CompanionBuildPlan plan))
            {
                IReadOnlyList<CompanionBuildPlan> plans = GetPlans(characterClass);
                string builds = plans.Count == 1
                    ? "1 available build"
                    : $"{plans.Count} available builds ({string.Join(", ", plans.Select(entry => entry.Name))})";
                return $"{builds}; default: {DescribeRuntime(characterClass, plan)}";
            }
            return "this class has no available automatic plan";
        }

        public static string GetBlocker(eCharacterClass characterClass, string savedPlanId)
        {
            if (!TryGetPlan(characterClass, out _))
                return GetBlocker(characterClass);
            if (!TryGetPlanById(characterClass, savedPlanId, out CompanionBuildPlan plan))
                return $"saved plan ID '{savedPlanId}' is missing or changed; allocations are preserved and earned points remain manual until a compatible plan is explicitly selected";
            return DescribeRuntime(characterClass, plan);
        }

        /// <summary>Describes a build, or the runtime check that currently blocks it.</summary>
        public static string DescribeRuntime(eCharacterClass characterClass, CompanionBuildPlan plan)
        {
            ICharacterClass runtimeClass = ScriptMgr.FindCharacterClass((int)characterClass);
            if (runtimeClass == null || runtimeClass.SpecPointsMultiplier != plan.ExpectedSpecPointsMultiplier)
                return $"runtime class point multiplier does not match the validated {plan.ExpectedSpecPointsMultiplier}-point plan";
            if (!TryValidateRuntimePlan(characterClass, plan, out string runtimeBlocker))
                return runtimeBlocker;
            return $"{plan.Name} ({plan.Id}); role: {plan.Role}; level-50 targets: {plan.FormatTargets()}; per-level schedule derived without autotrain";
        }

        /// <summary>Checks everything a build needs at runtime before it can be applied.</summary>
        public static bool TryValidateRuntimeBuild(eCharacterClass characterClass, CompanionBuildPlan plan,
            out string blocker)
        {
            ICharacterClass runtimeClass = ScriptMgr.FindCharacterClass((int)characterClass);
            if (plan != null && (runtimeClass == null ||
                                 runtimeClass.SpecPointsMultiplier != plan.ExpectedSpecPointsMultiplier))
            {
                blocker = $"runtime class multiplier does not match the validated {plan.ExpectedSpecPointsMultiplier}-point plan";
                return false;
            }
            return TryValidateRuntimePlan(characterClass, plan, out blocker);
        }
    }

    public sealed class CompanionBuildPlan
    {
        public string Id { get; }
        public eCharacterClass CharacterClass { get; }
        /// <summary>Short, single-word name used by commands.</summary>
        public string Key { get; }
        public string Name { get; }
        public string Role { get; }
        /// <summary>Group role set on the companion when this build is chosen.</summary>
        public BotPveGroupRole PrimaryRole { get; }
        /// <summary>A hybrid build also controls adds when its primary job allows.</summary>
        public bool CrowdControlDuty { get; }
        public int ExpectedSpecPointsMultiplier { get; }
        public IReadOnlyList<CompanionBuildRank> TargetAllocations { get; }

        internal CompanionBuildPlan(string id, eCharacterClass characterClass, string key, string name, string role,
            BotPveGroupRole primaryRole, bool crowdControlDuty, int expectedSpecPointsMultiplier,
            IReadOnlyList<CompanionBuildRank> targetAllocations)
        {
            Id = id;
            CharacterClass = characterClass;
            Key = key;
            Name = name;
            Role = role;
            PrimaryRole = primaryRole;
            CrowdControlDuty = crowdControlDuty;
            ExpectedSpecPointsMultiplier = expectedSpecPointsMultiplier;
            TargetAllocations = targetAllocations;
        }

        /// <summary>
        /// Creates the full recommended allocation from level 1 through the
        /// requested level. Each earned point trains the eligible next rank
        /// with the least proportional progress toward its level-50 target.
        /// Candidate order breaks ties, and a rank is never bought early.
        /// </summary>
        public IReadOnlyDictionary<string, int> GetTargetsAtLevel(int targetLevel, int specPointsMultiplier)
        {
            int throughLevel = Math.Clamp(targetLevel, 1, 50);
            var levels = TargetAllocations.ToDictionary(rank => rank.Specialization, _ => 1,
                StringComparer.OrdinalIgnoreCase);
            int available = -1;

            for (int level = 1; level <= throughLevel; level++)
            {
                available += AwardPointsAtLevel(level, specPointsMultiplier);
                while (true)
                {
                    int selectedIndex = -1;
                    double leastProgress = double.MaxValue;
                    int selectedCost = 0;
                    for (int index = 0; index < TargetAllocations.Count; index++)
                    {
                        CompanionBuildRank target = TargetAllocations[index];
                        int current = levels[target.Specialization];
                        int levelTarget = Math.Min(target.Level, Math.Max(1, target.Level * level / 50));
                        int nextCost = current + 1;
                        if (current >= levelTarget || available < nextCost)
                            continue;

                        double progress = (double)(current - 1) / Math.Max(1, target.Level - 1);
                        if (progress < leastProgress)
                        {
                            selectedIndex = index;
                            selectedCost = nextCost;
                            leastProgress = progress;
                        }
                    }

                    if (selectedIndex < 0)
                        break;

                    string key = TargetAllocations[selectedIndex].Specialization;
                    levels[key]++;
                    available -= selectedCost;
                }
            }

            return levels;
        }

        public int GetRequiredPointsAtLevel(int targetLevel, int specPointsMultiplier)
        {
            IReadOnlyDictionary<string, int> targets = GetTargetsAtLevel(targetLevel, specPointsMultiplier);
            return TargetAllocations.Sum(rank => CostToReach(1, targets[rank.Specialization]));
        }

        /// <summary>
        /// The allocation after a build switch: every listed career line
        /// returns to rank 1, then the build's schedule for the level is
        /// trained from the full no-autotrain point budget.
        /// </summary>
        public bool TryGetSwitchedAllocation(IEnumerable<string> careerLines, int level, int specPointsMultiplier,
            out Dictionary<string, int> allocation, out int unspentPoints)
        {
            allocation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in careerLines ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(line))
                    allocation[line] = 1;

            IReadOnlyDictionary<string, int> targets = GetTargetsAtLevel(level, specPointsMultiplier);
            foreach (KeyValuePair<string, int> target in targets)
                allocation[target.Key] = target.Value;
            unspentPoints = GetPointBudgetAtLevel(level, specPointsMultiplier) -
                            GetRequiredPointsAtLevel(level, specPointsMultiplier);
            return unspentPoints >= 0;
        }

        /// <summary>All specialization points a companion has earned by this level.</summary>
        public static int GetPointBudgetAtLevel(int level, int specPointsMultiplier)
        {
            int points = -1;
            for (int earnedLevel = 1; earnedLevel <= Math.Clamp(level, 1, 50); earnedLevel++)
                points += AwardPointsAtLevel(earnedLevel, specPointsMultiplier);
            return Math.Max(0, points);
        }

        public string FormatTargets() => string.Join(", ", TargetAllocations.Select(rank => $"{rank.Specialization} {rank.Level}"));

        public static int CostToReach(int currentLevel, int targetLevel)
        {
            int current = Math.Max(1, currentLevel);
            int target = Math.Max(current, targetLevel);
            return target * (target + 1) / 2 - current * (current + 1) / 2;
        }

        public static int AwardPointsAtLevel(int level, int specPointsMultiplier)
        {
            if (level <= 0)
                return 0;
            if (level <= 5)
                return level;

            int points = specPointsMultiplier * level / 10;
            if (level > 40)
                points += specPointsMultiplier * (level - 1) / 20;
            return points;
        }
    }

    public sealed record CompanionBuildRank(string Specialization, int Level);
}
