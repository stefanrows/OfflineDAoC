using System;

namespace DOL.GS
{
    /// <summary>Small, persistent voice and default-stance templates; never emits combat chatter.</summary>
    public static class CompanionPersonality
    {
        private static readonly string[] Keys = ["steady", "bold", "wry", "gentle", "curious"];

        public static string RandomKey() => Keys[Random.Shared.Next(Keys.Length)];

        public static string DefaultEngagement(string key) => key is "steady" or "gentle"
            ? "defensive" : "aggressive";

        public static string Dialogue(PlayerCompanionRecord record, string occasion)
        {
            CompanionCharacterCatalog.Character authored = CompanionCharacterCatalog.Find(record?.AuthoredRecruitKey);
            if (authored != null)
            {
                string authoredLine = occasion switch
                {
                    "bench" => authored.FieldNote,
                    "profile" => authored.Greeting + " " + authored.FieldNote,
                    _ => authored.Greeting,
                };
                return $"{record.Name}: \"{authoredLine}\"";
            }
            string key = record?.PersonalityKey ?? string.Empty;
            string line = (key, occasion) switch
            {
                ("bold", "bench") => "Call me when the next road opens.",
                ("bold", _) => "I am ready to take the lead when you ask.",
                ("gentle", "bench") => "Rest well; I will be near.",
                ("gentle", _) => "Let us keep everyone together.",
                ("curious", "bench") => "I will think over what we found.",
                ("curious", _) => "There is more to learn out there.",
                ("wry", "bench") => "Try to leave me a little work.",
                ("wry", _) => "This should make an interesting story.",
                ("steady", "bench") => "I will be ready when needed.",
                _ => "We can take this one step at a time.",
            };
            return $"{record?.Name}: \"{line}\"";
        }

        public static string Profile(PlayerCompanionRecord record)
        {
            CompanionCharacterCatalog.Character authored = CompanionCharacterCatalog.Find(record?.AuthoredRecruitKey);
            string identity = authored == null
                ? $"Generated {(string.IsNullOrWhiteSpace(record?.PersonalityKey) ? "steady" : record.PersonalityKey)} companion."
                : $"{authored.Background} Personality: {authored.Personality}.";
            string plan = CompanionBuildPlanCatalog.TryGetPlan((eCharacterClass)(record?.ClassId ?? 0), out CompanionBuildPlan build)
                ? build.Id : "manual training (no validated automatic plan)";
            return $"{record?.Name}, level {record?.Level} {(eCharacterClass)(record?.ClassId ?? 0)}; " +
                   $"{(eRace)(record?.RaceId ?? 0)}, {(eGender)(record?.GenderId ?? 0)}.\n{identity}\n" +
                   $"Preferred build: {plan}. Role: {(string.IsNullOrWhiteSpace(record?.TacticalRole) ? BotPartyRoles.DefaultPreference((eCharacterClass)(record?.ClassId ?? 0)) : record.TacticalRole)}; " +
                   $"stance: {(string.IsNullOrWhiteSpace(record?.EngagementPreference) ? "aggressive" : record.EngagementPreference)}.\n" +
                   Dialogue(record, "profile");
        }
    }
}
