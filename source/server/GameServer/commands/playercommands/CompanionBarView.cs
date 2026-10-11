using System;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>How one COMPANIONS tile is drawn: its inactive, active (gold, underlined) or disabled (grey) label.</summary>
    public enum CompanionBarTileState
    {
        Inactive = BarTileInactive,
        Active = BarTileActive,
        Disabled = BarTileDisabled,
    }

    /// <summary>
    /// The text the server writes into the COMPANIONS window (Custom7) for one refresh: a status line, a
    /// message line, ten tiles (four stance orders, Default, Follow, Stay, Pet pull, PULL, MANAGER) and eight
    /// member rows. ToLabels returns the 74 label values in the order the native window expects.
    /// </summary>
    public sealed class CompanionBarView
    {
        public static readonly string[] TileTexts =
        {
            "Aggressive", "Defensive", "Passive", "Assist", "Default", "Follow", "Stay", "Pet pull", "PULL", "MANAGER",
        };

        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public CompanionBarTileState[] Tiles { get; } = new CompanionBarTileState[BarTiles];
        public (string Text, eCompanionEngagementMode Color)?[] Slots { get; } = new (string Text, eCompanionEngagementMode Color)?[BarSlots];

        /// <summary>The 74 values for labels BarLabelBase..TotalLabelCount-1; unused labels are empty strings.</summary>
        public string[] ToLabels()
        {
            var labels = new string[BarLabelCount];
            Array.Fill(labels, string.Empty);
            labels[BarLabelStatus - BarLabelBase] = Fit(Sanitize(Status), BarWidthStatus);
            labels[BarLabelMessage - BarLabelBase] = Fit(Sanitize(Message), BarWidthMessage);
            for (int tile = 0; tile < BarTiles; tile++)
            {
                int width = tile >= BarControlPull - BarControlFirst ? BarWidthWideTile : BarWidthTile;
                int at = BarLabelTileBase - BarLabelBase + BarTileStride * tile;
                CompanionBarTileState state = Tiles[tile];
                labels[at + (int)state] = Fit(TileTexts[tile], width);
                if (state == CompanionBarTileState.Active)
                    labels[at + BarTileUnderline] = Underline(width);
            }
            for (int slot = 0; slot < BarSlots; slot++)
            {
                if (Slots[slot] is not { } value)
                    continue;
                int at = BarLabelSlotBase - BarLabelBase + BarSlotStride * slot;
                labels[at + (int)value.Color] = Fit(value.Text, BarWidthSlot);
            }
            return labels;
        }

        /// <summary>Underscores that span a tile's underline label (its width minus 12 pixels of margin).</summary>
        public static string Underline(int width) => new string('_', Math.Max(1, (width - 12) / TextWidth("_")));

        /// <summary>
        /// The tile table. An order tile is active when that order is set; Default when none is set. Stay is
        /// disabled while pet pull is off and the companions are not staying. Pet pull is disabled under Assist
        /// train. PULL is disabled with no owned companion. MANAGER is always enabled.
        /// </summary>
        public static CompanionBarTileState[] ComputeTiles(bool ordered, eCompanionEngagementMode order, bool petPull,
            bool staying, bool assistTrain, bool hasCompanions)
        {
            var tiles = new CompanionBarTileState[BarTiles];
            tiles[BarControlAggressive - BarControlFirst] = Mark(ordered && order == eCompanionEngagementMode.Aggressive);
            tiles[BarControlDefensive - BarControlFirst] = Mark(ordered && order == eCompanionEngagementMode.Defensive);
            tiles[BarControlPassive - BarControlFirst] = Mark(ordered && order == eCompanionEngagementMode.Passive);
            tiles[BarControlAssist - BarControlFirst] = Mark(ordered && order == eCompanionEngagementMode.AssistTrain);
            tiles[BarControlDefault - BarControlFirst] = Mark(!ordered);
            tiles[BarControlFollow - BarControlFirst] = Mark(!staying);
            tiles[BarControlStay - BarControlFirst] = Mark(staying, disabled: !petPull && !staying);
            tiles[BarControlPetPull - BarControlFirst] = Mark(petPull, disabled: assistTrain && !petPull);
            tiles[BarControlPull - BarControlFirst] = Mark(false, disabled: !hasCompanions);
            tiles[BarControlManager - BarControlFirst] = Mark(false);
            return tiles;
        }

        /// <summary>
        /// One member row. Null when the group member is not an owned companion (the row stays empty). A dead or
        /// out-of-region companion shows Dead or Away in the passive colour. Otherwise its effective stance.
        /// </summary>
        public static (string Text, eCompanionEngagementMode Color)? DescribeSlot(bool owned, bool alive, bool sameRegion,
            eCompanionEngagementMode effective)
        {
            if (!owned)
                return null;
            if (!alive)
                return ("Dead", eCompanionEngagementMode.Passive);
            if (!sameRegion)
                return ("Away", eCompanionEngagementMode.Passive);
            return effective switch
            {
                eCompanionEngagementMode.Aggressive => ("Aggressive", effective),
                eCompanionEngagementMode.Defensive => ("Defensive", effective),
                eCompanionEngagementMode.Passive => ("Passive", effective),
                eCompanionEngagementMode.AssistTrain => ("Assist train", effective),
                _ => throw new ArgumentOutOfRangeException(nameof(effective)),
            };
        }

        private static CompanionBarTileState Mark(bool active, bool disabled = false) =>
            active ? CompanionBarTileState.Active : disabled ? CompanionBarTileState.Disabled : CompanionBarTileState.Inactive;
    }
}
