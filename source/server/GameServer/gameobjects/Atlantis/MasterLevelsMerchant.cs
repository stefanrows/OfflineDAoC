using System;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// Demyphon, the Master Level credit merchant (TOA_PLAN WP2.2, adapted from gaheris
    /// MasterLevelsMerchant). Sells the merchant list "Master Level Credits" for bounty points
    /// (a mob row without a list gets that one). Handing a bought token back redeems it: a
    /// credit for the next Master Level raises the player one level, the Star of Destiny sets
    /// the discipline aside so the Arbiter offers the choice again. The namespace is the one
    /// gaheris sql/13 and sql/35 write into the mob rows.
    /// </summary>
    public class MasterLevelsMerchant : GameBountyMerchant
    {
        public const string CREDIT_PREFIX = "master_level_credit_bptoken_";
        public const string RESPEC_TOKEN = "master_level_respec_bptoken";
        public const string DEFAULT_ITEM_LIST = "Master Level Credits";

        public override void LoadFromDatabase(DataObject merchantobject)
        {
            base.LoadFromDatabase(merchantobject);

            if (TradeItems == null)
                TradeItems = new MerchantTradeItems(DEFAULT_ITEM_LIST);
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            TurnTo(player, 5000);
            SayTo(player, eChatLoc.CL_ChatWindow,
                "I trade in the credits of the Master Levels, for bounty points alone. Buy the credit for your " +
                "next Master Level and hand it back to me, and the level is yours. A Star of Destiny handed to me " +
                "sets your discipline aside, so the Arbiter lets you choose again.");
            return true;
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source is not GamePlayer player || item?.Id_nb == null)
                return base.ReceiveItem(source, item);

            if (item.Id_nb.StartsWith(CREDIT_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                SayTo(player, eChatLoc.CL_PopupWindow, Redeem(player, item, out bool consumed));
                return consumed;
            }

            if (item.Id_nb.Equals(RESPEC_TOKEN, StringComparison.OrdinalIgnoreCase))
            {
                SayTo(player, eChatLoc.CL_PopupWindow, Respec(player, item, out bool consumed));
                return consumed;
            }

            return base.ReceiveItem(source, item);
        }

        /// <summary>Credit for Master Level N: accepted only when N is the player's next level.</summary>
        public static string Redeem(GamePlayer player, DbInventoryItem item, out bool consumed)
        {
            consumed = false;

            if (!int.TryParse(item.Id_nb.Substring(CREDIT_PREFIX.Length), out int level) ||
                level < 1 || level > GamePlayer.ML_MAX_LEVEL)
                return "This is not a credit I recognise.";

            if (!player.MLGranted)
                return "You have not begun the trials. Speak to the Arbiter first.";

            if (level <= player.MLLevel)
                return $"You already hold Master Level {level}. Keep the credit; it teaches you nothing new.";

            if (level != player.MLLevel + 1)
                return $"You need the credit for Master Level {player.MLLevel + 1} to continue.";

            if (player.Inventory != null && !player.Inventory.RemoveItem(item))
                return "You do not hold that credit.";

            consumed = true;
            MasterLevelProgression.GrantLevel(player);
            return $"You have been granted knowledge of Master Level {player.MLLevel}.";
        }

        /// <summary>Star of Destiny: the discipline is set aside; levels and experience stay.</summary>
        public static string Respec(GamePlayer player, DbInventoryItem item, out bool consumed)
        {
            consumed = false;

            if (MasterLevelProgression.PathOf(player) == null)
                return "Keep it. You have no discipline to turn from.";

            if (player.Inventory != null && !player.Inventory.RemoveItem(item))
                return "You do not hold that star.";

            consumed = true;
            MasterLevelProgression.Respec(player);
            return "The star is spent and your discipline with it. Go back to the Arbiter and name the one you would rather walk.";
        }
    }
}
