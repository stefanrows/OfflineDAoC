using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// A bot in Darkness Falls with seals in its bags walks to a seal merchant
/// and buys the best upgrade it can afford, then goes back to its business.
/// Checked at most once a minute per bot, only out of combat.
/// </summary>
public static class AutonomousSealShopping
{
    private sealed class State
    {
        public long NextCheck;
        public GameItemCurrencyMerchant Target;
        public long GiveUpTick;
    }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();
    public const ushort DarknessFallsRegion = 249;
    private const int SearchRadius = 6_000;

    /// <summary>True while the bot is walking to or trading with a seal merchant (it owns the AI turn).</summary>
    public static bool TryRun(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || bot.CurrentRegionID != DarknessFallsRegion ||
            !bot.IsAlive || bot.InCombat || brain.HasAggro || bot.IsCasting)
            return false;
        State state = States.GetOrCreateValue(bot);
        long now = GameLoop.GameLoopTime;

        if (state.Target == null)
        {
            if (now < state.NextCheck) return false;
            state.NextCheck = now + 60_000;
            state.Target = bot.GetNPCsInRadius(SearchRadius).OfType<GameItemCurrencyMerchant>()
                .Where(merchant => merchant.MoneyItem?.Item?.Id_nb is { } currency &&
                    AutonomousBotEconomy.CarriesCurrency(bot, currency, 1) &&
                    merchant.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(template =>
                        template.Price > 0 && AutonomousBotEconomy.CarriesCurrency(bot, currency, (int)template.Price)) == true)
                .OrderBy(bot.GetDistanceTo)
                .FirstOrDefault();
            if (state.Target == null) return false;
            state.GiveUpTick = now + 120_000;
        }

        GameItemCurrencyMerchant merchant = state.Target;
        if (now > state.GiveUpTick || merchant.ObjectState != GameObject.eObjectState.Active)
        {
            state.Target = null;
            return false;
        }
        if (!merchant.IsWithinRadius(bot, GS.ServerProperties.Properties.WORLD_PICKUP_DISTANCE - 20))
        {
            if (!bot.IsMoving)
                bot.PathTo(new System.Numerics.Vector3(merchant.X, merchant.Y, merchant.Z), bot.MaxSpeed);
            return true;
        }

        bot.StopMoving();
        if (AutonomousBotEconomy.TryBuyWithCurrency(bot, merchant, out DbInventoryItem bought, out int seals))
            DOL.Logging.LoggerManager.Create(typeof(AutonomousSealShopping)).Info(
                $"AUTONOMOUS_SEAL_PURCHASE bot=\"{bot.Name}\" item=\"{bought.Name}\" seals={seals} merchant=\"{merchant.Name}\"");
        state.Target = null;
        return false;
    }
}
