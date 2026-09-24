using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// Rare contextual chat assembled from interchangeable clauses. The cross-product
/// yields tens of thousands of lines without an LLM or a huge resident string table.
/// </summary>
public static class AutonomousBotChat
{
    public sealed record Context(
        string BotName,
        string ClassName,
        string ZoneName,
        string MonsterName,
        string NpcName,
        string ItemName,
        string CraftName,
        int Level,
        int GroupSize,
        eRealm Realm,
        AutonomousPlayerType PlayerType = AutonomousPlayerType.Leveler,
        int Chattiness = 50);

    private static readonly string[] Openers =
    [
        "Anyone", "Anybody", "Is anyone", "Would anyone", "Any adventurers", "Anyone nearby",
        "Quick question, anyone", "If anyone is nearby, would they", "Does anyone", "Could someone",
        "Looking around—anyone", "Before I head out, does anyone", "One moment—can anybody", "Greetings, can anyone",
    ];

    private static readonly string[] GrindQuestions =
    [
        "grinding {monster}?", "hunting {monster} near {zone}?", "want to hunt {monster}?",
        "know a good camp for {monster}?", "still getting experience from {monster}?",
        "forming a group for {monster}?", "seen many {monster} around {zone}?", "up for a few pulls of {monster}?",
    ];

    private static readonly string[] FindQuestions =
    [
        "help me find {monster}?", "help me find {npc}?", "point me toward {npc}?",
        "tell me where {monster} gather?", "seen {npc} around {zone}?", "show me the road to {npc}?",
        "remember where {npc} stands?", "help track down {monster}?", "know the safest route to {npc}?",
    ];

    private static readonly string[] Statements =
    [
        "These {monster} are keeping me busy.", "{zone} feels crowded tonight.", "I may look for a new camp soon.",
        "My packs are nearly full.", "I should visit {npc} before long.", "That last {monster} put up a fight.",
        "I could use an upgrade from the Realm Exchange.", "Saving this {item}; it may fetch a fair price.",
        "I have enough {craft} materials for another attempt.", "A short rest, then back to {monster}.",
        "I wonder whether a dungeon group is forming.", "The frontier sounds tempting, but not quite yet.",
    ];

    private static readonly string[] GroupLines =
    [
        "Group has room for {count} more near {zone}.", "We could use a healer for {monster}.",
        "A shield would help with these {monster}.", "Anyone want to join us in {zone}?",
        "We are taking careful pulls at {monster}.", "Our group may try harder prey soon.",
    ];

    private static readonly IReadOnlyDictionary<AutonomousPlayerType, string[]> SocialLines =
        new Dictionary<AutonomousPlayerType, string[]>
        {
            [AutonomousPlayerType.Leveler] =
            [
                "lfg for {monster} around {zone}, level {level}.",
                "anyone grinding {monster} near {zone}?",
                "lf a healer for a few pulls around {zone}.",
                "anyone from the guild near {zone}? leveling at {monster}.",
                "trainer stop, then back to {monster}.",
            ],
            [AutonomousPlayerType.Casual] =
            [
                "brb, trainer in town. might be back for a few pulls later.",
                "anyone around {zone}? taking it easy tonight.",
                "got a bit of gear to sort, then maybe a short group.",
                "saving up for a better {item}; these drops are rough.",
            ],
            [AutonomousPlayerType.Hybrid] =
            [
                "lfg for xp near {zone}; might roam after.",
                "anyone want a quick group for {monster}?",
                "inc? we can take a few pulls, then head out.",
                "who is around {zone} tonight?",
            ],
            [AutonomousPlayerType.Hunter] =
            [
                "eyes open near {zone}; saw movement on the road.",
                "anyone seen a small crew around {zone}?",
                "inc near {zone}; keep your group close.",
                "quiet road so far. checking the next camp.",
            ],
            [AutonomousPlayerType.Roamer] =
            [
                "lf8 for an Emain loop; bring a healer and stick together.",
                "inc at {zone}; who is out roaming?",
                "lfg for frontier, leaving when the group is full.",
                "need a couple more before we head to the loop.",
            ],
            [AutonomousPlayerType.KeepWarrior] =
            [
                "need a few more for a keep run near {zone}.",
                "who can bring siege? organizing a crew now.",
                "keep road is quiet; looking for defenders near {zone}.",
                "lf a group to check the frontier keeps after this pull.",
            ],
        };

    public static int MinimumCombinatorialVariants => Openers.Length * (GrindQuestions.Length + FindQuestions.Length) + Statements.Length + GroupLines.Length;
    public static int EstimatedVariantsWithTwentyLiveNames => MinimumCombinatorialVariants * 20;

    public static string Generate(Context context, Random random = null)
    {
        random ??= Random.Shared;
        string template;
        int category = random.Next(context.GroupSize > 1 ? 4 : 3);
        if (category == 0)
            template = $"{Openers[random.Next(Openers.Length)]} {GrindQuestions[random.Next(GrindQuestions.Length)]}";
        else if (category == 1)
            template = $"{Openers[random.Next(Openers.Length)]} {FindQuestions[random.Next(FindQuestions.Length)]}";
        else if (category == 2)
            template = Statements[random.Next(Statements.Length)];
        else
            template = GroupLines[random.Next(GroupLines.Length)];

        return AutonomousChatSafetyPolicy.Sanitize(Fill(template, context));
    }

    public static string GenerateForType(Context context, Random random = null)
    {
        random ??= Random.Shared;
        if (!SocialLines.TryGetValue(context.PlayerType, out string[] lines) || lines.Length == 0)
            lines = SocialLines[AutonomousPlayerType.Leveler];
        return AutonomousChatSafetyPolicy.Sanitize(Fill(lines[random.Next(lines.Length)], context));
    }

    public static string GenerateGuildReply(Context context, string incoming, Random random = null)
    {
        random ??= Random.Shared;
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(incoming);
        string zone = Fallback(context.ZoneName, "the frontier");
        string monster = Fallback(context.MonsterName, "the local mobs");
        string item = Fallback(context.ItemName, "that gear");
        string response = intent switch
        {
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => context.PlayerType switch
            {
                AutonomousPlayerType.Leveler => $"lfg for {monster} near {zone}; level {context.Level}.",
                AutonomousPlayerType.Casual => "might join for a short run after I finish in town.",
                AutonomousPlayerType.Hybrid => $"I can do a few pulls near {zone}, then roam.",
                AutonomousPlayerType.Hunter => $"small crew only; meet near {zone} and keep moving.",
                AutonomousPlayerType.Roamer => $"count me for frontier; still need {Math.Max(1, 8 - context.GroupSize)}.",
                _ => $"I can bring siege if we have enough for a keep run near {zone}.",
            },
            eAutonomousChatIntent.Trading => context.PlayerType == AutonomousPlayerType.Casual
                ? "pst with an offer; I will check when I am back in town."
                : $"check the exchange first; I have seen {item} listed there.",
            eAutonomousChatIntent.Banter or eAutonomousChatIntent.Abuse => GenerateTaunt(context.PlayerType, random),
            eAutonomousChatIntent.RvR => context.PlayerType switch
            {
                AutonomousPlayerType.Hunter => $"eyes on {zone}; do not wander off alone.",
                AutonomousPlayerType.KeepWarrior => $"need more bodies before we push the keep.",
                _ => $"inc at {zone}; regroup before the next fight.",
            },
            eAutonomousChatIntent.Farewell => context.PlayerType == AutonomousPlayerType.Casual
                ? "brb; save me a spot later."
                : "gg, see you on the road.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "back to the hunt; anyone need a group?",
            _ => GenerateForType(context, random),
        };
        return AutonomousChatSafetyPolicy.Sanitize(response);
    }

    public static bool ShouldSpeak(DateTime utcNow, DateTime lastSpokeUtc, bool inCombat, Random random = null) =>
        ShouldSpeak(utcNow, lastSpokeUtc, inCombat, 50, random);

    public static bool ShouldSpeak(DateTime utcNow, DateTime lastSpokeUtc, bool inCombat,
        int chattiness, Random random = null)
    {
        if (inCombat || utcNow - lastSpokeUtc < TimeSpan.FromMinutes(4))
            return false;
        random ??= Random.Shared;
        double chance = 0.003 + Math.Clamp(chattiness, 0, 100) / 10_000d;
        return random.NextDouble() < chance;
    }

    private static string GenerateTaunt(AutonomousPlayerType type, Random random) => type switch
    {
        AutonomousPlayerType.Hunter => random.Next(3) switch
        {
            0 => "free realm points; that was a shit pull.",
            1 => "run back, you bastard. we are still here.",
            _ => "that road was yours until you stopped moving.",
        },
        AutonomousPlayerType.Roamer => random.Next(3) switch
        {
            0 => "that was a shit fight; regroup and go again.",
            1 => "gg. bring a full group next time.",
            _ => "we are still standing. anyone need a rez?",
        },
        AutonomousPlayerType.KeepWarrior => random.Next(3) switch
        {
            0 => "nice try; the wall is still ours.",
            1 => "that push was a mess. repair the ram and regroup.",
            _ => "good fight. we will see you at the next keep.",
        },
        _ => random.Next(3) switch
        {
            0 => "that pull was a shitshow; take a breath and try again.",
            1 => "you hit like a wet noodle. back to the camp?",
            _ => "gg; we all get a bad pull now and then.",
        },
    };

    public static string GeneratePostFightTaunt(AutonomousPlayerType type, Random random = null)
    {
        random ??= Random.Shared;
        return AutonomousChatSafetyPolicy.Sanitize(GenerateTaunt(type, random));
    }

    private static string Fill(string text, Context context)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{realm}"] = context.Realm.ToString(),
            ["{zone}"] = Fallback(context.ZoneName, "this area"),
            ["{monster}"] = Fallback(context.MonsterName, "the local creatures"),
            ["{npc}"] = Fallback(context.NpcName, "the nearest trainer"),
            ["{item}"] = Fallback(context.ItemName, "drop"),
            ["{craft}"] = Fallback(context.CraftName, "crafting"),
            ["{count}"] = Math.Max(1, 8 - context.GroupSize).ToString(),
            ["{level}"] = Math.Clamp(context.Level, 1, 50).ToString(),
        };

        foreach ((string token, string value) in values)
            text = text.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
