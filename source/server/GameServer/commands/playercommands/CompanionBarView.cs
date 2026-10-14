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
    /// message line, fourteen tiles (four stance orders, Default, Follow, Stay, Pet pull, PULL, MANAGER, then the
    /// group row's Kick, Leader, Leave and Grind) and eight member rows (a stance in one of four colours and a
    /// leader mark). The Invite tile is native and has no label. The native group adapters draw the rest of a
    /// row. ToLabels returns the 98 label values in the order the native window expects.
    /// </summary>
    public sealed class CompanionBarView
    {
        public static readonly string[] TileTexts =
        {
            "Aggressive", "Defensive", "Passive", "Assist", "Default", "Follow", "Stay", "Pet pull", "PULL", "MANAGER",
            "Kick", "Leader", "Leave", "Grind",
        };

        public const string GrindActiveText = "Stop grind";
        public const string LeaderMark = "*";

        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public CompanionBarTileState[] Tiles { get; } = new CompanionBarTileState[BarTiles];

        /// <summary>A tile's text; defaults to TileTexts, the Grind tile reads "Stop grind" while grinding.</summary>
        public string[] TileLabels { get; } = (string[])TileTexts.Clone();

        public (string Text, eCompanionEngagementMode Color)?[] Slots { get; } = new (string Text, eCompanionEngagementMode Color)?[BarSlots];
        public bool[] Leaders { get; } = new bool[BarSlots];

        /// <summary>The 98 values for labels BarLabelBase..TotalLabelCount-1; unused labels are empty strings.</summary>
        public string[] ToLabels()
        {
            var labels = new string[BarLabelCount];
            Array.Fill(labels, string.Empty);
            labels[BarLabelStatus - BarLabelBase] = Fit(Sanitize(Status), BarWidthStatus);
            labels[BarLabelMessage - BarLabelBase] = Fit(Sanitize(Message), BarWidthMessage);
            for (int tile = 0; tile < BarTiles; tile++)
            {
                int at = BarLabelTileBase - BarLabelBase + BarTileStride * tile;
                CompanionBarTileState state = Tiles[tile];
                labels[at + (int)state] = Fit(Sanitize(TileLabels[tile]), BarWidthTile);
                if (state == CompanionBarTileState.Active)
                    labels[at + BarTileUnderline] = Underline(BarWidthTile);
            }
            for (int slot = 0; slot < BarSlots; slot++)
            {
                int at = BarLabelSlotBase - BarLabelBase + BarSlotStride * slot;
                if (Slots[slot] is { } value)
                    labels[at + (int)value.Color] = Fit(Sanitize(value.Text), BarWidthSlot);
                if (Leaders[slot])
                    labels[at + BarSlotLeader] = LeaderMark;
            }
            return labels;
        }

        /// <summary>Underscores that span a tile's underline label (its width minus 12 pixels of margin).</summary>
        public static string Underline(int width) => new string('_', Math.Max(1, (width - 12) / TextWidth("_")));

        /// <summary>
        /// The tile table. An order tile is active when that order is set; Default when none is set. Stay is
        /// disabled while pet pull is off and the companions are not staying. Pet pull is disabled under Assist
        /// train. PULL is disabled with no owned companion. MANAGER is always enabled. Kick and Leave need a group,
        /// Leave is active (gold) while its confirmation is pending, Leader needs the player to lead a group of at
        /// least two, and Grind is active while grinding.
        /// </summary>
        public static CompanionBarTileState[] ComputeTiles(bool ordered, eCompanionEngagementMode order, bool petPull,
            bool staying, bool assistTrain, bool hasCompanions, bool grouped = false, bool leads = false,
            bool leavePending = false, bool grinding = false)
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
            tiles[BarControlKick - BarControlFirst] = Mark(false, disabled: !grouped);
            tiles[BarControlLeader - BarControlFirst] = Mark(false, disabled: !leads);
            tiles[BarControlLeave - BarControlFirst] = Mark(grouped && leavePending, disabled: !grouped);
            tiles[BarControlGrind - BarControlFirst] = Mark(grinding);
            return tiles;
        }

        /// <summary>
        /// One member row. Null when the group member is not an owned companion (the row's stance stays empty; the
        /// native adapters still draw the member). A dead or out-of-region companion shows Dead or Away in the
        /// passive colour. Otherwise its effective stance.
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

        /// <summary>The saved stance a row click cycles to: aggressive, defensive, passive, then aggressive again.</summary>
        public static string NextSavedStance(string saved)
        {
            string current = (saved ?? string.Empty).Trim().ToLowerInvariant();
            if (current.Length == 0)
                current = "aggressive"; // An unset preference is the aggressive default.
            return current switch
            {
                "aggressive" => "defensive",
                "defensive" => "passive",
                _ => "aggressive",
            };
        }

        private static CompanionBarTileState Mark(bool active, bool disabled = false) =>
            active ? CompanionBarTileState.Active : disabled ? CompanionBarTileState.Disabled : CompanionBarTileState.Inactive;
    }
}
