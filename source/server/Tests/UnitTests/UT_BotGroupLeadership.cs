using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_BotGroupLeadership
{
    [Test]
    public void PromotingABotUpdatesThePartyAndAnnouncesTheLivingLeader()
    {
        using var server = new EpicTestServerScope();
        // No bot login, persistent record, equipment, or live world is needed
        // to exercise the actual group promotion and notification path.
        var first = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var promoted = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        promoted.Name = "Raid leader";
        var group = new RecordingGroup(first);
        var members = (List<GameLiving>)typeof(Group)
            .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
        members.AddRange([first, promoted]);
        first.Group = promoted.Group = group;
        first.GroupIndex = 0;
        promoted.GroupIndex = 1;

        Assert.That(group.MakeLeader(promoted), Is.True);
        Assert.That(group.LivingLeader, Is.SameAs(promoted));
        Assert.That(group.Leader, Is.Null, "a bot is a GameLiving, not a GamePlayer");
        Assert.That(members, Is.EqualTo(new GameLiving[] { promoted, first }));
        Assert.That((promoted.GroupIndex, first.GroupIndex), Is.EqualTo(((byte)0, (byte)1)));
        Assert.That(group.Message, Is.EqualTo("Raid leader is the new group leader."));
        Assert.That(group.MakeLeader(promoted), Is.False);
    }

    private sealed class RecordingGroup(GameLiving leader) : Group(leader)
    {
        public string Message { get; private set; }
        public override void SendMessageToGroupMembers(string msg, eChatType type, eChatLoc loc) => Message = msg;
    }
}
