namespace OfflineDaoc.Launcher;

// Display-only clock. Never queries SQLite, contacts the server or expires a task.
internal static class TaskTimerDisplay
{
    public static long? Remaining(string deadlineUtc, DateTime? utcNow) =>
        utcNow is DateTime now && DateTime.TryParse(deadlineUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime deadline)
                ? Math.Max(0, (long)Math.Ceiling((deadline.ToUniversalTime() - now.ToUniversalTime()).TotalMilliseconds))
                : null;

    public static string Format(long milliseconds)
    {
        long seconds = (long)Math.Ceiling(Math.Max(0, milliseconds) / 1000d);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    public static string FormatAuctionExpiry(DateTime expiresUtc, DateTime? simulationUtc)
    {
        if (simulationUtc is not DateTime now)
            return "Unavailable";
        TimeSpan remaining = expiresUtc.ToUniversalTime() - now.ToUniversalTime();
        if (remaining <= TimeSpan.Zero)
            return "Expired — removing";
        string timeLeft = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m"
                : $"{Math.Max(0, remaining.Minutes)}m";
        return $"{timeLeft} left • {expiresUtc.ToLocalTime():MMM d HH:mm}";
    }
}
