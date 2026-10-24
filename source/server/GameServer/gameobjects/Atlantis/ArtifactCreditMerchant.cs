using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	/// <summary>
	/// Aphaestia, the sphinx who trades artifact encounter credit for bounty points (DOLSharp,
	/// via gaheris; TOA_PLAN WP3.1b). Her list ("artifact_credits") holds one credit token per
	/// artifact, priced in bounty points. Handing a token to her or to any scholar records the
	/// encounter credit for that artifact (<see cref="ArtifactAwakening.RedeemCreditToken"/>).
	///
	/// Tokens match an artifact by item ID: <c>Id_nb == lower(Artifact.Credit) + "_credit"</c>
	/// (pack 061 aligns the Credit values that did not match). gaheris compared the item name
	/// with Credit, which matched 3 of our 57 artifacts. A token that would be useless (credit
	/// already held, a token of that artifact already carried, artifact awakened or carried, no
	/// version the class can use) is not sold, and never more than one at a time.
	/// The class name is stored in Mob.ClassType (pack 020) and must not change.
	/// </summary>
	public class ArtifactCreditMerchant : GameBountyMerchant
	{
		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player))
				return false;

			SayTo(player, $"You're {player.Name}, right? Please tell me you're here to share your [tales of battle] in the frontiers.");
			return true;
		}

		public override bool WhisperReceive(GameLiving source, string text)
		{
			if (source is not GamePlayer player || !player.IsWithinRadius(this, WorldMgr.INTERACT_DISTANCE))
				return false;

			switch (text?.Trim().ToLowerInvariant())
			{
				case "tales of battle":
					SayTo(player, "Oh, how I wish I could leave the lands of Atlantis at will as you do. I'd [leave] in a heartbeat if I could, " +
						"but I am only sustained by the lingering magic of the Atlanteans. This hall is as far as I can go.");
					return true;
				case "leave":
					SayTo(player, "I'd even join you in your war if I could, anything for a little adventure, a little excitement. " +
						"I remember a time when we sphinxes were greatly feared as guardians of more than just [knowledge]. Ah, but that time has long since passed.");
					return true;
				case "knowledge":
					SayTo(player, "Indeed, that is all I am now: a living tome of knowledge, particularly of the Atlanteans' lost artifacts. " +
						"Trade me the bounty of your battles and I will give you what you would otherwise learn in an artifact's encounter. " +
						"Hand the credit back to me, or to any scholar, and it is yours; then bring the artifact's story to a scholar.");
					return true;
			}

			return base.WhisperReceive(source, text);
		}

		public override void OnPlayerBuy(GamePlayer player, int item_slot, int number)
		{
			int pageNumber = item_slot / MerchantTradeItems.MAX_ITEM_IN_TRADEWINDOWS;
			int slotNumber = item_slot % MerchantTradeItems.MAX_ITEM_IN_TRADEWINDOWS;
			DbItemTemplate template = TradeItems?.GetItem(pageNumber, (eMerchantWindowSlot) slotNumber);

			if (template != null && template.Id_nb.EndsWith(ArtifactAwakening.CREDIT_SUFFIX, System.StringComparison.OrdinalIgnoreCase))
			{
				string problem = ArtifactAwakening.CreditTokenProblem(player, template.Id_nb, forPurchase: true);

				if (problem != null)
				{
					player.Out.SendMessage($"{Name} says, \"{problem}\"", eChatType.CT_Merchant, eChatLoc.CL_SystemWindow);
					return;
				}

				// One credit is all anybody can use; never sell a stack.
				number = 1;
			}

			base.OnPlayerBuy(player, item_slot, number);
		}

		public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
		{
			if (source is GamePlayer player && ArtifactAwakening.IsCreditToken(item))
			{
				SayTo(player, ArtifactAwakening.RedeemCreditToken(player, this, item));
				return true;
			}

			return base.ReceiveItem(source, item);
		}
	}
}
