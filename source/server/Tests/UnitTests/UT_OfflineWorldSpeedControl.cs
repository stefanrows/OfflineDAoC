using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DOL.GS;
using Newtonsoft.Json;
using NUnit.Framework;

namespace DOL.Tests.UnitTests;

[TestFixture]
public sealed class UT_OfflineWorldSpeedControl
{
    [TestCase(1, 1000.0 / 30.0)]
    [TestCase(2, 1000.0 / 60.0)]
    [TestCase(3, 1000.0 / 90.0)]
    [TestCase(5, 1000.0 / 150.0)]
    [TestCase(10, 1000.0 / 300.0)]
    [TestCase(20, 1000.0 / 600.0)]
    public void TickPacerUsesLogicalDurationDividedByMultiplier(int multiplier, double expectedMilliseconds)
    {
        double duration = GameLoopTickPacer.GetTargetTickDuration(1000.0 / 30.0, multiplier);
        Assert.That(duration, Is.EqualTo(expectedMilliseconds).Within(0.0001));
    }

    [Test]
    public void CheckpointCadenceIsOneRealSecondAtEverySpeed()
    {
        foreach (int multiplier in new[] { 1, 2, 3, 5, 10, 20 })
            Assert.That(OfflineWorldSpeedControl.GetCheckpointIntervalMilliseconds(multiplier), Is.EqualTo(1000));
    }

    [Test]
    public void AchievedSpeedIsClampedDuringCatchUpBursts()
    {
        Assert.That(OfflineWorldSpeedControl.CalculateAchievedMultiplier(3000, 1), Is.EqualTo(20));
        Assert.That(OfflineWorldSpeedControl.CalculateAchievedMultiplier(600, 1), Is.EqualTo(20));
        Assert.That(OfflineWorldSpeedControl.CalculateAchievedMultiplier(90, 1), Is.EqualTo(3));
        Assert.That(OfflineWorldSpeedControl.CalculateAchievedMultiplier(0, 1), Is.Zero);
        Assert.That(OfflineWorldSpeedControl.CalculateAchievedMultiplier(30, 0), Is.Zero);
    }

    [Test]
    public void AcceptedRequestIdRetentionIncludesFutureTimestampAllowance()
    {
        const long frequency = 1000;
        const long acceptedAt = 10_000;

        Assert.That(OfflineWorldSpeedControl.IsAcceptedRequestIdExpired(acceptedAt, acceptedAt + 40_000, frequency), Is.False);
        Assert.That(OfflineWorldSpeedControl.IsAcceptedRequestIdExpired(acceptedAt, acceptedAt + 40_001, frequency), Is.True);
    }

    [Test]
    public void ThirtyCompletedLogicalTicksAdvanceExactlyOneSecond()
    {
        int remainder = 0;
        int elapsed = 0;
        for (int i = 0; i < 30; i++)
            elapsed += GameLoop.CalculateLogicalTickDeltaMilliseconds(30, ref remainder);

        Assert.That(elapsed, Is.EqualTo(1000));
        Assert.That(remainder, Is.Zero);
    }

    [Test]
    public void RestartAdvancesSavedSimulationUtcByDowntimeAtOneTimesSpeed()
    {
        DateTime simulation = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        DateTime savedWall = new(2026, 9, 24, 12, 30, 0, DateTimeKind.Utc);
        DateTime restartedWall = savedWall.AddHours(3);
        string checkpoint = WorldSimulationClock.CreateCheckpointJson(simulation, savedWall);

        DateTime restored = WorldSimulationClock.RestoreCheckpoint(checkpoint, restartedWall, out string error);

        Assert.That(error, Is.Null);
        Assert.That(restored, Is.EqualTo(simulation.AddHours(3)));
    }

    [Test]
    public void RestartDoesNotMoveSimulationUtcBackWhenWallClockMovedBack()
    {
        DateTime simulation = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        DateTime savedWall = new(2026, 9, 24, 12, 30, 0, DateTimeKind.Utc);
        string checkpoint = WorldSimulationClock.CreateCheckpointJson(simulation, savedWall);

        DateTime restored = WorldSimulationClock.RestoreCheckpoint(checkpoint, savedWall.AddMinutes(-5), out string error);

        Assert.That(error, Is.Null);
        Assert.That(restored, Is.EqualTo(simulation));
    }

    [Test]
    public void UnsupportedCheckpointVersionIsReportedInsteadOfResettingSilently()
    {
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        const string checkpoint = "{\"Version\":2,\"SimulationUtc\":\"2026-09-24T11:00:00Z\",\"WallUtc\":\"2026-09-24T11:00:00Z\"}";

        DateTime restored = WorldSimulationClock.RestoreCheckpoint(checkpoint, now, out string error);

        Assert.That(restored, Is.EqualTo(now));
        Assert.That(error, Does.Contain("invalid"));
    }

    [Test]
    public void EmptyExistingCheckpointIsReportedAsInvalid()
    {
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        DateTime restored = WorldSimulationClock.RestoreCheckpoint(" ", now, out string error);

        Assert.That(restored, Is.EqualTo(now));
        Assert.That(error, Does.Contain("empty"));
    }

    [Test]
    public void ClientConnectionForcesOneTimesAndDisconnectGraceLastsFiveSeconds()
    {
        const long frequency = 1000;
        const long disconnectedAt = 10_000;

        Assert.That(OfflineWorldSpeedControl.ComputeEffectiveMultiplier(3, 1, false, 0, disconnectedAt, frequency), Is.EqualTo(1));
        Assert.That(OfflineWorldSpeedControl.ComputeEffectiveMultiplier(3, 0, true, disconnectedAt, disconnectedAt + 4_999, frequency), Is.EqualTo(1));
        Assert.That(OfflineWorldSpeedControl.ComputeEffectiveMultiplier(3, 0, true, disconnectedAt, disconnectedAt + 5_000, frequency), Is.EqualTo(3));
        Assert.That(OfflineWorldSpeedControl.ComputeEffectiveMultiplier(20, 0, true, disconnectedAt, disconnectedAt + 5_000, frequency), Is.EqualTo(20));
        Assert.That(OfflineWorldSpeedControl.ComputeEffectiveMultiplier(20, 1, false, 0, disconnectedAt, frequency), Is.EqualTo(1));
    }

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    [TestCase(5, true)]
    [TestCase(7, false)]
    [TestCase(10, true)]
    [TestCase(15, false)]
    [TestCase(20, true)]
    [TestCase(21, false)]
    [TestCase(0, false)]
    public void OnlyTheListedSpeedStepsAreSupported(int multiplier, bool expected)
    {
        Assert.That(OfflineWorldSpeedControl.IsSupportedMultiplier(multiplier), Is.EqualTo(expected));
    }

    [Test]
    public void RequestValidationRejectsWrongSessionStaleDuplicateAndInvalidMultipliers()
    {
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        WorldSpeedRequest request = new()
        {
            SessionId = "current-session",
            RequestId = "request-1",
            CreatedUtc = now,
            Multiplier = 2
        };
        HashSet<string> acceptedIds = new(StringComparer.Ordinal);

        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(request, "current-session", acceptedIds, now, out _), Is.True);
        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(request, "other-session", acceptedIds, now, out _), Is.False);
        acceptedIds.Add("request-1");
        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(request, "current-session", acceptedIds, now, out _), Is.False);
        acceptedIds.Add("request-2");
        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(request, "current-session", acceptedIds, now, out _), Is.False, "A → B → A must still reject the first request ID.");
        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(new WorldSpeedRequest
        {
            SessionId = request.SessionId,
            RequestId = "request-2",
            CreatedUtc = request.CreatedUtc,
            Multiplier = 4
        }, "current-session", acceptedIds, now, out _), Is.False);
        Assert.That(OfflineWorldSpeedControl.TryValidateRequest(new WorldSpeedRequest
        {
            SessionId = request.SessionId,
            RequestId = "request-3",
            CreatedUtc = now.AddMinutes(-1),
            Multiplier = 2
        }, "current-session", acceptedIds, now, out _), Is.False);
    }

    [Test]
    [NonParallelizable]
    public void PublishStatusRetriesPastATransientLockOnTheDestinationFile()
    {
        Type controlType = typeof(OfflineWorldSpeedControl);
        FieldInfo statusPathField = controlType.GetField("_statusPath", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo statusErrorField = controlType.GetField("_statusError", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo publishStatus = controlType.GetMethod("PublishStatus", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(Action<int>) }, null);

        object previousStatusPath = statusPathField.GetValue(null);
        object previousStatusError = statusErrorField.GetValue(null);
        string statusPath = Path.Combine(AppContext.BaseDirectory, "world-speed.status.retry-test.json");
        FileStream blockingHandle = null;
        try
        {
            statusPathField.SetValue(null, statusPath);
            File.WriteAllText(statusPath, "stale status held by another reader");

            // Simulate a reader that opened the previously published status
            // file without delete sharing (a naive reader, an indexer or an
            // antivirus scan), which is what makes File.Move throw on
            // Windows while the handle is open. Release it deterministically
            // when the retry helper reports the first failed attempt, rather
            // than racing a fixed delay against its backoff.
            blockingHandle = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Action<int> onRetryAttemptFailed = _ => blockingHandle.Dispose();

            publishStatus.Invoke(null, new object[] { onRetryAttemptFailed });

            var statusError = (string)statusErrorField.GetValue(null);
            Assert.That(statusError, Is.Null,
                "PublishStatus should retry past a transient lock instead of recording a failure.");
            WorldSpeedStatus status = JsonConvert.DeserializeObject<WorldSpeedStatus>(File.ReadAllText(statusPath));
            Assert.That(status, Is.Not.Null);
            Assert.That(status.SessionId, Is.Not.Null.And.Not.Empty);
        }
        finally
        {
            blockingHandle?.Dispose();
            statusPathField.SetValue(null, previousStatusPath);
            statusErrorField.SetValue(null, previousStatusError);
            File.Delete(statusPath);
        }
    }
}
