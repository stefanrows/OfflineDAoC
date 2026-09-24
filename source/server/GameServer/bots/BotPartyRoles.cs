using System;

namespace DOL.GS
{
    public enum BotPartyRole { Damage, Tank, Support }
    // Append only: the manager's role filter maps native control IDs to the
    // first four values, and companion records save the lower-case name.
    public enum BotPveGroupRole { Tank, Healer, Buffer, Attacker, CrowdControl }

    /// <summary>Group jobs, not a test for whether a class happens to know a buff.</summary>
    public static class BotPartyRoles
    {
        public static BotPartyRole For(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cleric or
            eCharacterClass.Druid or
            eCharacterClass.Healer => BotPartyRole.Support,
            eCharacterClass.Paladin or
            eCharacterClass.Armsman or eCharacterClass.Reaver or
            eCharacterClass.Warrior or eCharacterClass.Thane or
            eCharacterClass.Hero or eCharacterClass.Champion => BotPartyRole.Tank,
            _ => BotPartyRole.Damage
        };

        public static bool IsHealingClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Druid or
            eCharacterClass.Warden or eCharacterClass.Healer or eCharacterClass.Shaman or
            eCharacterClass.Paladin;

        public static bool CanFill(eCharacterClass characterClass, BotPveGroupRole role) => role switch
        {
            BotPveGroupRole.Tank => For(characterClass) == BotPartyRole.Tank,
            BotPveGroupRole.Healer => IsHealingClass(characterClass),
            BotPveGroupRole.Buffer => For(characterClass) == BotPartyRole.Support || IsHybridSupport(characterClass),
            BotPveGroupRole.Attacker => For(characterClass) != BotPartyRole.Support || IsHybridSupport(characterClass),
            BotPveGroupRole.CrowdControl => IsCrowdControlClass(characterClass),
            _ => false
        };

        /// <summary>
        /// Classes with a single-target, non-pulsing mesmerize line that the
        /// companion add-control policy can use in PvE.
        /// </summary>
        public static bool IsCrowdControlClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Healer or eCharacterClass.Sorcerer or eCharacterClass.Bard or
            eCharacterClass.Mentalist or eCharacterClass.Spiritmaster;

        /// <summary>Saved role value, as written to a companion record.</summary>
        public static string RoleValue(BotPveGroupRole role) => role.ToString().ToLowerInvariant();

        /// <summary>Reads a command or saved role, accepting "cc" and "crowd control".</summary>
        public static bool TryParseRole(string value, out BotPveGroupRole role)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (normalized == "cc")
                normalized = "crowdcontrol";
            return Enum.TryParse(normalized, true, out role) && Enum.IsDefined(role) &&
                   !int.TryParse(normalized, out _);
        }

        /// <summary>
        /// A companion adds control when its role is Crowd control, or when its
        /// automatic build lists crowd control as a second duty (Tri-spec).
        /// </summary>
        public static bool HasCrowdControlDuty(GameBot bot)
        {
            PlayerCompanionRecord record = bot?.PlayerCompanionRecord;
            if (bot?.IsPersistentPlayerCompanion != true || record == null || bot.CharacterClass == null)
                return false;
            var characterClass = (eCharacterClass)bot.CharacterClass.ID;
            if (!IsCrowdControlClass(characterClass))
                return false;
            if (TryParseRole(record.TacticalRole, out BotPveGroupRole role) && role == BotPveGroupRole.CrowdControl)
                return true;
            return string.Equals(record.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase) &&
                   CompanionBuildPlanCatalog.TryGetPlanById(characterClass, record.TrainingPlanId, out CompanionBuildPlan plan) &&
                   plan.CrowdControlDuty;
        }

        public static string GroupRoleLabel(BotPveGroupRole role) => role switch
        {
            BotPveGroupRole.Tank => "Tank",
            BotPveGroupRole.Healer => "Healer",
            BotPveGroupRole.Buffer => "Buffer",
            BotPveGroupRole.CrowdControl => "Crowd control",
            _ => "Attacker"
        };

        /// <summary>Display label for a saved role value; unknown text is shown as saved.</summary>
        public static string GroupRoleLabel(string savedRole) =>
            TryParseRole(savedRole, out BotPveGroupRole role) ? GroupRoleLabel(role) : savedRole;

        public static bool IsHybridSupport(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Warden or eCharacterClass.Paladin or eCharacterClass.Shaman or eCharacterClass.Friar or
            eCharacterClass.Sorcerer or eCharacterClass.Mentalist or
            eCharacterClass.Bard or eCharacterClass.Minstrel or eCharacterClass.Skald;

        public static string DefaultPreference(eCharacterClass characterClass) => For(characterClass) switch
        {
            BotPartyRole.Tank => "tank",
            BotPartyRole.Support => "healer",
            _ => "attacker",
        };

        /// <summary>
        /// A support class is a non-combat role only when the party has an
        /// actual combat-capable partner. A solo healer must retain its attack path.
        /// </summary>
        public static bool IsSupport(GameBot bot) => bot?.Group?.MemberCount > 1 &&
            bot.CharacterClass != null &&
            (bot.IsPersistentPlayerCompanion &&
             TryParseRole(bot.PlayerCompanionRecord?.TacticalRole, out BotPveGroupRole preference)
                ? preference is BotPveGroupRole.Healer or BotPveGroupRole.Buffer ||
                  preference == BotPveGroupRole.CrowdControl &&
                  For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Support
                : For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Support) &&
            HasCombatPartner(bot);

        private static bool HasCombatPartner(GameBot bot)
        {
            Group group = bot?.Group;
            if (group == null)
                return false;

            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member == null || member == bot || !member.IsAlive ||
                    member.ObjectState != GameObject.eObjectState.Active)
                    continue;

                // A real player is always a combat-capable party partner from
                // the bot's point of view, even when that player is currently
                // idle.  This preserves player-led healer/buffer behavior.
                if (member is GamePlayer)
                    return true;

                if (member is GameBot other && other.CharacterClass != null &&
                    For((eCharacterClass)other.CharacterClass.ID) != BotPartyRole.Support)
                    return true;
            }

            return false;
        }

        public static bool IsTank(GameBot bot) => bot?.CharacterClass != null &&
            (bot.IsPersistentPlayerCompanion &&
             TryParseRole(bot.PlayerCompanionRecord?.TacticalRole, out BotPveGroupRole preference)
                ? preference == BotPveGroupRole.Tank
                : For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Tank);

        public static string Label(eCharacterClass characterClass) => IsCrowdControlClass(characterClass)
            ? ClassLabel(characterClass) + "/Crowd control"
            : ClassLabel(characterClass);

        private static string ClassLabel(eCharacterClass characterClass) => IsHybridSupport(characterClass)
            ? (For(characterClass) == BotPartyRole.Tank ? "Tank/Healer/Buffer"
                : IsHealingClass(characterClass) ? "Attacker/Healer/Buffer" : "Attacker/Buffer")
            : For(characterClass) switch
        {
            BotPartyRole.Tank => "Tank/Attacker",
            BotPartyRole.Support => IsHealingClass(characterClass) ? "Healer/Buffer" : "Song Support/Buffer",
            _ => "Damage/Attacker"
        };
    }
}
