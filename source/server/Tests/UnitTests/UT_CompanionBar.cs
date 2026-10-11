using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionBar
{
    private static int TileOffset(int tile) => BarLabelTileBase - BarLabelBase + BarTileStride * tile;
    private static int SlotOffset(int slot) => BarLabelSlotBase - BarLabelBase + BarSlotStride * slot;

    private static int[] NonEmptyIndices(string[] labels) =>
        labels.Select((text, index) => (text, index)).Where(pair => pair.text.Length > 0).Select(pair => pair.index).ToArray();

    private static int[] IntConstants(Func<FieldInfo, bool> filter) =>
        typeof(CompanionManagerProtocol).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(int) && filter(field))
            .Select(field => (int)field.GetRawConstantValue())
            .ToArray();

    [Test]
    public void LabelRangesChainWithoutGapsOrOverlap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BarLabelBase, Is.EqualTo(LabelCount), "The bar starts right after the manager labels");
            Assert.That(BarLabelStatus, Is.EqualTo(BarLabelBase));
            Assert.That(BarLabelMessage, Is.EqualTo(BarLabelStatus + 1));
            Assert.That(BarLabelTileBase, Is.EqualTo(BarLabelMessage + 1));
            Assert.That(BarLabelSlotBase, Is.EqualTo(BarLabelTileBase + BarTileStride * BarTiles));
            Assert.That(BarLabelSlotBase + BarSlotStride * BarSlots, Is.EqualTo(TotalLabelCount));
            Assert.That(BarLabelCount, Is.EqualTo(TotalLabelCount - BarLabelBase));
            Assert.That(new CompanionBarView().ToLabels(), Has.Length.EqualTo(BarLabelCount));
            Assert.That(CompanionBarView.TileTexts, Has.Length.EqualTo(BarTiles));
            Assert.That(BarTileStride, Is.EqualTo(4), "Four state labels per tile");
            Assert.That(BarSlotStride, Is.EqualTo(4), "Four stance colours per member row");
            Assert.That((BarTileInactive, BarTileActive, BarTileDisabled, BarTileUnderline), Is.EqualTo((0, 1, 2, 3)));
        });
    }

    [Test]
    public void BarControlsSitInsideTheBarRangeAndClearTheManagerControls()
    {
        int[] barControls = IntConstants(field => field.Name.StartsWith("BarControl", StringComparison.Ordinal)
            && field.Name is not ("BarControlFirst" or "BarControlLimit"));
        Assert.That(barControls, Has.Length.EqualTo(BarTiles + 1), "Ten tiles and the ready control");
        Assert.Multiple(() =>
        {
            Assert.That(BarControlLimit, Is.LessThanOrEqualTo(ControlLimit));
            Assert.That(barControls, Is.All.InRange(BarControlFirst, BarControlLimit - 1));
            Assert.That(BarControlReady, Is.EqualTo(BarControlLimit - 1));
        });
        int[] managerControls = IntConstants(field => field.Name.StartsWith("Control", StringComparison.Ordinal));
        Assert.That(managerControls.Where(value => value is >= BarControlFirst and < BarControlLimit), Is.Empty,
            "No manager control may use the COMPANIONS range");
        Assert.That(managerControls, Is.Not.Empty);
    }

    [Test]
    public void TileControlsAreConsecutiveInTileOrder()
    {
        int[] tiles =
        {
            BarControlAggressive, BarControlDefensive, BarControlPassive, BarControlAssist, BarControlDefault,
            BarControlFollow, BarControlStay, BarControlPetPull, BarControlPull, BarControlManager,
        };
        Assert.That(tiles, Is.EqualTo(Enumerable.Range(BarControlFirst, BarTiles)));
    }

    [Test]
    public void ClientControlsAcceptTheReadyAndFirstBarControl()
    {
        Assert.That(TryParseClientControl("0a9f", "bf", out _, out int ready), Is.True);
        Assert.That(ready, Is.EqualTo(BarControlReady));
        Assert.That(TryParseClientControl("0a9f", "a8", out _, out int first), Is.True);
        Assert.That(first, Is.EqualTo(BarControlFirst));
    }

    [Test]
    public void ActiveTileFillsTextAndUnderlineOnly()
    {
        var view = new CompanionBarView();
        view.Tiles[BarControlDefault - BarControlFirst] = CompanionBarTileState.Active;
        view.Tiles[BarControlStay - BarControlFirst] = CompanionBarTileState.Disabled;
        string[] labels = view.ToLabels();
        int defaultAt = TileOffset(BarControlDefault - BarControlFirst);
        int stayAt = TileOffset(BarControlStay - BarControlFirst);
        Assert.Multiple(() =>
        {
            Assert.That(labels[defaultAt + BarTileInactive], Is.Empty);
            Assert.That(labels[defaultAt + BarTileActive], Is.EqualTo("Default"));
            Assert.That(labels[defaultAt + BarTileDisabled], Is.Empty);
            Assert.That(labels[defaultAt + BarTileUnderline], Is.EqualTo(CompanionBarView.Underline(BarWidthTile)));
            Assert.That(labels[stayAt + BarTileDisabled], Is.EqualTo("Stay"));
            Assert.That(labels[stayAt + BarTileInactive], Is.Empty);
        });
        // Every other tile shows its text in the inactive label; the status and message lines stay empty.
        var expected = new List<int>();
        for (int tile = 0; tile < BarTiles; tile++)
        {
            if (tile == BarControlDefault - BarControlFirst)
                expected.AddRange(new[] { defaultAt + 1, defaultAt + 3 });
            else if (tile == BarControlStay - BarControlFirst)
                expected.Add(stayAt + 2);
            else
                expected.Add(TileOffset(tile));
        }
        Assert.That(NonEmptyIndices(labels), Is.EqualTo(expected));
    }

    [Test]
    public void WideTilesUseTheWideUnderlineAndPullAndManagerAreWide()
    {
        var view = new CompanionBarView();
        view.Tiles[BarControlPull - BarControlFirst] = CompanionBarTileState.Active;
        string[] labels = view.ToLabels();
        string underline = labels[TileOffset(BarControlPull - BarControlFirst) + BarTileUnderline];
        Assert.That(underline, Is.EqualTo(CompanionBarView.Underline(BarWidthWideTile)));
        Assert.That(underline.Length, Is.GreaterThan(CompanionBarView.Underline(BarWidthTile).Length));
        Assert.That(CompanionBarView.Underline(1), Is.EqualTo("_"), "The underline never comes out empty");
    }

    [Test]
    public void TileTextsFitTheirTiles()
    {
        for (int tile = 0; tile < BarTiles; tile++)
        {
            int width = tile >= BarControlPull - BarControlFirst ? BarWidthWideTile : BarWidthTile;
            Assert.That(TextWidth(CompanionBarView.TileTexts[tile]), Is.LessThanOrEqualTo(width),
                CompanionBarView.TileTexts[tile]);
        }
    }

    [Test]
    public void MemberRowTextGoesToItsStanceColourIndex()
    {
        var view = new CompanionBarView();
        view.Slots[2] = ("Defensive", eCompanionEngagementMode.Defensive);
        view.Slots[5] = ("Dead", eCompanionEngagementMode.Passive);
        string[] labels = view.ToLabels();
        int second = SlotOffset(2);
        int fifth = SlotOffset(5);
        Assert.Multiple(() =>
        {
            Assert.That(labels[second + (int)eCompanionEngagementMode.Defensive], Is.EqualTo("Defensive"));
            Assert.That(labels[second + (int)eCompanionEngagementMode.Aggressive], Is.Empty);
            Assert.That(labels[second + (int)eCompanionEngagementMode.Passive], Is.Empty);
            Assert.That(labels[second + (int)eCompanionEngagementMode.AssistTrain], Is.Empty);
            Assert.That(labels[fifth + (int)eCompanionEngagementMode.Passive], Is.EqualTo("Dead"));
            Assert.That(NonEmptyIndices(labels).Where(index => index >= SlotOffset(0)), Is.EqualTo(new[] { second + 1, fifth + 2 }));
        });
    }

    [Test]
    public void NullSlotLeavesItsFourLabelsBlank()
    {
        var view = new CompanionBarView();
        view.Slots[0] = ("Aggressive", eCompanionEngagementMode.Aggressive);
        view.Slots[0] = null;
        string[] labels = view.ToLabels();
        int first = SlotOffset(0);
        Assert.That(labels.Skip(first).Take(BarSlotStride), Is.All.Empty);
    }

    [Test]
    public void StatusAndMessageAreSanitizedAndFitted()
    {
        var view = new CompanionBarView { Status = new string('x', 300) + "—", Message = "Pulled → the camp" };
        string[] labels = view.ToLabels();
        Assert.Multiple(() =>
        {
            Assert.That(labels[BarLabelStatus - BarLabelBase], Does.EndWith("..."));
            Assert.That(TextWidth(labels[BarLabelStatus - BarLabelBase]), Is.LessThanOrEqualTo(BarWidthStatus));
            Assert.That(labels[BarLabelMessage - BarLabelBase], Is.EqualTo("Pulled > the camp"));
        });
    }

    [Test]
    public void ComputeTilesShowsDefaultWhenNoOrderIsSet()
    {
        CompanionBarTileState[] tiles = CompanionBarView.ComputeTiles(false, eCompanionEngagementMode.Aggressive,
            petPull: false, staying: false, assistTrain: false, hasCompanions: true);
        Assert.Multiple(() =>
        {
            Assert.That(tiles[BarControlDefault - BarControlFirst], Is.EqualTo(CompanionBarTileState.Active));
            Assert.That(tiles[BarControlAggressive - BarControlFirst], Is.EqualTo(CompanionBarTileState.Inactive));
            Assert.That(tiles[BarControlFollow - BarControlFirst], Is.EqualTo(CompanionBarTileState.Active), "Not staying");
            Assert.That(tiles[BarControlManager - BarControlFirst], Is.EqualTo(CompanionBarTileState.Inactive));
        });
    }

    [Test]
    public void ComputeTilesActivatesTheOrderTileOnly()
    {
        CompanionBarTileState[] tiles = CompanionBarView.ComputeTiles(true, eCompanionEngagementMode.Defensive,
            petPull: false, staying: false, assistTrain: false, hasCompanions: true);
        Assert.Multiple(() =>
        {
            Assert.That(tiles[BarControlDefensive - BarControlFirst], Is.EqualTo(CompanionBarTileState.Active));
            Assert.That(tiles[BarControlAggressive - BarControlFirst], Is.EqualTo(CompanionBarTileState.Inactive));
            Assert.That(tiles[BarControlDefault - BarControlFirst], Is.EqualTo(CompanionBarTileState.Inactive));
        });
    }

    [Test]
    public void StayIsDisabledWhilePetPullIsOffAndNotStaying()
    {
        CompanionBarTileState Stay(bool petPull, bool staying) =>
            CompanionBarView.ComputeTiles(false, eCompanionEngagementMode.Aggressive, petPull, staying, false, true)
                [BarControlStay - BarControlFirst];
        Assert.Multiple(() =>
        {
            Assert.That(Stay(petPull: false, staying: false), Is.EqualTo(CompanionBarTileState.Disabled));
            Assert.That(Stay(petPull: true, staying: false), Is.EqualTo(CompanionBarTileState.Inactive));
            Assert.That(Stay(petPull: false, staying: true), Is.EqualTo(CompanionBarTileState.Active));
        });
    }

    [Test]
    public void PetPullIsDisabledUnderAssistTrainUntilItIsTurnedOff()
    {
        CompanionBarTileState PetPull(bool petPull, bool assistTrain) =>
            CompanionBarView.ComputeTiles(assistTrain, eCompanionEngagementMode.AssistTrain, petPull, false, assistTrain, true)
                [BarControlPetPull - BarControlFirst];
        Assert.Multiple(() =>
        {
            Assert.That(PetPull(petPull: false, assistTrain: true), Is.EqualTo(CompanionBarTileState.Disabled));
            Assert.That(PetPull(petPull: true, assistTrain: true), Is.EqualTo(CompanionBarTileState.Active));
            Assert.That(PetPull(petPull: false, assistTrain: false), Is.EqualTo(CompanionBarTileState.Inactive));
        });
    }

    [Test]
    public void PullIsDisabledWithNoCompanions()
    {
        CompanionBarTileState[] none = CompanionBarView.ComputeTiles(false, eCompanionEngagementMode.Aggressive,
            false, false, false, hasCompanions: false);
        CompanionBarTileState[] some = CompanionBarView.ComputeTiles(false, eCompanionEngagementMode.Aggressive,
            false, false, false, hasCompanions: true);
        Assert.Multiple(() =>
        {
            Assert.That(none[BarControlPull - BarControlFirst], Is.EqualTo(CompanionBarTileState.Disabled));
            Assert.That(some[BarControlPull - BarControlFirst], Is.EqualTo(CompanionBarTileState.Inactive));
        });
    }

    [Test]
    public void DescribeSlotShowsOwnedCompanionsAndNothingElse()
    {
        Assert.That(CompanionBarView.DescribeSlot(false, true, true, eCompanionEngagementMode.Aggressive), Is.Null);
        Assert.That(CompanionBarView.DescribeSlot(true, false, true, eCompanionEngagementMode.Aggressive),
            Is.EqualTo(("Dead", eCompanionEngagementMode.Passive)));
        Assert.That(CompanionBarView.DescribeSlot(true, true, false, eCompanionEngagementMode.Defensive),
            Is.EqualTo(("Away", eCompanionEngagementMode.Passive)));
        Assert.That(CompanionBarView.DescribeSlot(true, true, true, eCompanionEngagementMode.Aggressive),
            Is.EqualTo(("Aggressive", eCompanionEngagementMode.Aggressive)));
        Assert.That(CompanionBarView.DescribeSlot(true, true, true, eCompanionEngagementMode.Defensive),
            Is.EqualTo(("Defensive", eCompanionEngagementMode.Defensive)));
        Assert.That(CompanionBarView.DescribeSlot(true, true, true, eCompanionEngagementMode.Passive),
            Is.EqualTo(("Passive", eCompanionEngagementMode.Passive)));
        Assert.That(CompanionBarView.DescribeSlot(true, true, true, eCompanionEngagementMode.AssistTrain),
            Is.EqualTo(("Assist train", eCompanionEngagementMode.AssistTrain)));
    }

    [Test]
    public void SlotTextsFitTheirColumn()
    {
        foreach (eCompanionEngagementMode mode in Enum.GetValues<eCompanionEngagementMode>())
        {
            var result = CompanionBarView.DescribeSlot(true, true, true, mode);
            Assert.That(TextWidth(result!.Value.Text), Is.LessThanOrEqualTo(BarWidthSlot), result.Value.Text);
        }
        Assert.That(TextWidth("Dead"), Is.LessThanOrEqualTo(BarWidthSlot));
        Assert.That(TextWidth("Away"), Is.LessThanOrEqualTo(BarWidthSlot));
    }

    [Test]
    public void PreferenceSerializesAsOnOffAndRoundTrips()
    {
        Assert.That(CompanionBarPreferences.Serialize(true), Is.EqualTo("on"));
        Assert.That(CompanionBarPreferences.Serialize(false), Is.EqualTo("off"));
        foreach (bool enabled in new[] { true, false })
        {
            Assert.That(CompanionBarPreferences.TryParse(CompanionBarPreferences.Serialize(enabled), out bool parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(enabled));
        }
    }

    [Test]
    public void PreferenceAcceptsOnlyOnOffIgnoringCaseAndSpaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBarPreferences.TryParse("ON", out bool upper), Is.True);
            Assert.That(upper, Is.True);
            Assert.That(CompanionBarPreferences.TryParse(" Off ", out bool mixed), Is.True);
            Assert.That(mixed, Is.False);
        });
        foreach (string garbage in new[] { null, "", "   ", "yes", "no", "1", "0", "true", "false", "onoff", "o n" })
        {
            Assert.That(CompanionBarPreferences.TryParse(garbage, out bool enabled), Is.False, garbage);
            Assert.That(enabled, Is.EqualTo(CompanionBarPreferences.DefaultEnabled), "A rejected value leaves the default");
        }
    }

    [Test]
    public void PreferenceKeyIsLowerCaseAndPerAccount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBarPreferences.KeyForAccount("  StefanRows "), Is.EqualTo("companion_bar.enabled.stefanrows"));
            Assert.That(CompanionBarPreferences.KeyForAccount("  "), Is.Null);
            Assert.That(CompanionBarPreferences.KeyForAccount(null), Is.Null);
        });
    }

    [Test]
    public void CommandArgumentsMapToShowOnOffOrUsage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBar.ParseArgument(null), Is.EqualTo(CompanionBarArgument.Show));
            Assert.That(CompanionBar.ParseArgument("   "), Is.EqualTo(CompanionBarArgument.Show));
            Assert.That(CompanionBar.ParseArgument("On"), Is.EqualTo(CompanionBarArgument.On));
            Assert.That(CompanionBar.ParseArgument(" off "), Is.EqualTo(CompanionBarArgument.Off));
        });
        foreach (string text in new[] { "yes", "1", "0", "true", "onoff", "show" })
            Assert.That(CompanionBar.ParseArgument(text), Is.EqualTo(CompanionBarArgument.Usage), text);
    }

    [Test]
    public void RoutingPredicateClaimsOnlyTheBarControls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBar.IsBarControl(BarControlFirst), Is.True);
            Assert.That(CompanionBar.IsBarControl(BarControlManager), Is.True);
            Assert.That(CompanionBar.IsBarControl(BarControlReady), Is.True, "The ready control is a bar control too");
            Assert.That(CompanionBar.IsBarControl(BarControlLimit - 1), Is.True);
            Assert.That(CompanionBar.IsBarControl(BarControlFirst - 1), Is.False, "Manager rows and tabs stay with the manager");
            Assert.That(CompanionBar.IsBarControl(BarControlLimit), Is.False, "List paging stays with the manager");
            Assert.That(CompanionBar.IsBarControl(ControlSearch), Is.False);
            Assert.That(CompanionBar.IsBarControl(ControlClose), Is.False);
        });
    }

    [Test]
    public void StatusTextNamesTheOrderAndCounts()
    {
        Assert.That(CompanionBar.StatusText("Defensive", 4, 2), Is.EqualTo("Defensive | 4 with you | 2 in squads"));
        Assert.That(CompanionBar.StatusText("Own stances", 1, 0), Is.EqualTo("Own stances | 1 with you"),
            "No squad part when no companion is in a squad");
    }

    [Test]
    public void OrderLabelUsesOwnStancesWithoutAnOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBar.OrderLabel(false, eCompanionEngagementMode.Passive), Is.EqualTo("Own stances"));
            Assert.That(CompanionBar.OrderLabel(true, eCompanionEngagementMode.Aggressive), Is.EqualTo("Aggressive"));
            Assert.That(CompanionBar.OrderLabel(true, eCompanionEngagementMode.AssistTrain), Is.EqualTo("Assist train"));
        });
    }

    [Test]
    public void ShortMessageKeepsTheFirstSentence()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionBar.ShortMessage(
                "Companions: AGGRESSIVE. They assist your attacks but return when left far behind."),
                Is.EqualTo("Companions: AGGRESSIVE."));
            Assert.That(CompanionBar.ShortMessage("Stay is already off."), Is.EqualTo("Stay is already off."));
            Assert.That(CompanionBar.ShortMessage(null), Is.Empty);
            Assert.That(CompanionBar.ShortMessage("  "), Is.Empty);
        });
    }

    [Test]
    public void MissingPlayerKeepsTheDefaultAndSaveReportsFailure()
    {
        Assert.That(CompanionBarPreferences.DefaultEnabled, Is.True, "The window is on by default");
        Assert.That(CompanionBarPreferences.Load(null), Is.True);
        Assert.That(CompanionBarPreferences.Save(null, false), Is.False);
    }
}
