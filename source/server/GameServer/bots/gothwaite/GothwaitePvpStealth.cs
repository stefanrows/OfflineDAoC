namespace DOL.GS;

/// <summary>
/// Stealth for the mode's hunters (docs/GOTHWAITE_PVP_MODE.md section 13): a stealth class with the Stealth skill that is alone or
/// in a doctrine that hunts stealthed (solo assassin, stealth pack, gank squad, archer group) stays hidden outside the castle while
/// it is not fighting, approaches its victim stealthed and opens with its stealth styles, as the Thidranki infiltrators did. The
/// brain's stealth gates ask <see cref="Applies"/> and <see cref="KeepsStealth"/> through one guarded condition each; both are
/// false for every bot that is not a mode bot, so the world bots' stealth loop is untouched.
/// </summary>
public static class GothwaitePvpStealth
{
    /// <summary>A failed stealth attempt (a running song, combat) is retried after this long.</summary>
    public const int RetryMilliseconds = 2_000;

    /// <summary>
    /// A mode bot that hunts stealthed: an assassin or archer (not a Minstrel) with the Stealth skill, alone or in a stealth doctrine.
    /// False for any other bot.
    /// </summary>
    public static bool Applies(GameBot bot)
    {
        if (!GothwaitePvpMode.IsModeBot(bot) || bot.CharacterClass == null)
            return false;
        var characterClass = (eCharacterClass)bot.CharacterClass.ID;
        if (!IsHunterClass(characterClass) || (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) <= 0)
            return false;
        if (GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership) &&
            membership.Shape != GothwaitePvpShape.Solo)
            return membership.Doctrine is { } doctrine && GothwaitePvpTactics.HuntsStealthed(doctrine.Kind);
        return true;
    }

    /// <summary>The classes that hunt stealthed: assassins and archers. A Minstrel is a visible group class.</summary>
    public static bool IsHunterClass(eCharacterClass characterClass) =>
        BotRvrAmbush.IsStealthClass(characterClass) && characterClass != eCharacterClass.Minstrel;

    /// <summary>
    /// The brain keeps this bot stealthed while it idles (one guarded condition in BotBrain.TryEnterStealthWhileIdle): a hunter
    /// outside the castle that is on the hunt. False for any other bot, so the brain drops stealth as before.
    /// </summary>
    public static bool KeepsStealth(GameBot bot) => Applies(bot) && GothwaitePvpDriver.WantsStealth(bot);

    /// <summary>
    /// Stealths a hunter on the hunt when it can: alive, not fighting, not casting, not within the server's combat window since its
    /// last hit (a player could not hide right after a fight either). A failed attempt is retried after <see cref="RetryMilliseconds"/>.
    /// </summary>
    public static void Maintain(GameBot bot, long now, ref long nextTry)
    {
        if (bot == null || bot.IsStealthed || now < nextTry || !Applies(bot) || !GothwaitePvpDriver.WantsStealth(bot))
            return;
        nextTry = now + RetryMilliseconds;
        if (!bot.IsAlive || bot.InCombat || bot.IsCasting || bot.IsAttacking || bot.IsCrowdControlled)
            return;
        bot.Stealth(true);
    }
}
