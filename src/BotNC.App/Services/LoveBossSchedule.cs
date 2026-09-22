using System.Globalization;

namespace BotNC.App.Services;

public static class LoveBossSchedule
{
    private static readonly TimeSpan GameOffset = TimeSpan.FromHours(-3);
    private static readonly int[] SpawnHours = [0, 9, 12, 16, 20];

    public static DateTimeOffset GameNow(DateTimeOffset utcNow) => utcNow.ToOffset(GameOffset);

    public static DateTimeOffset? EntrySlot(DateTimeOffset utcNow)
    {
        var now = GameNow(utcNow);
        foreach (var hour in SpawnHours)
        {
            var spawnDay = hour == 0 && now.Hour == 23 ? now.AddDays(1) : now;
            var spawn = new DateTimeOffset(spawnDay.Year, spawnDay.Month, spawnDay.Day, hour, 0, 0, GameOffset);
            if (now >= spawn.AddMinutes(-2) && now < spawn.AddSeconds(-15))
                return spawn;
        }
        return null;
    }

    public static string SlotKey(DateTimeOffset slot) =>
        slot.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string DailyKey(DateTimeOffset utcNow)
    {
        var now = GameNow(utcNow);
        return (now.TimeOfDay < TimeSpan.FromHours(4) ? now.Date.AddDays(-1) : now.Date)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string WeeklyKey(DateTimeOffset utcNow)
    {
        var now = GameNow(utcNow).DateTime;
        if (now.DayOfWeek == DayOfWeek.Monday && now.TimeOfDay < TimeSpan.FromHours(4))
            now = now.AddDays(-1);
        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        return now.Date.AddDays(-daysSinceMonday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static void VerifyPolicy()
    {
        var justBefore = new DateTimeOffset(2026, 9, 22, 15, 57, 59, GameOffset);
        var opening = justBefore.AddSeconds(1);
        var ending = new DateTimeOffset(2026, 9, 22, 16, 0, 0, GameOffset);
        if (EntrySlot(justBefore) is not null ||
            EntrySlot(opening) != ending ||
            EntrySlot(ending) is not null)
            throw new InvalidOperationException("Janela de entrada do Boss do Amor incorreta.");
        var midnight = new DateTimeOffset(2026, 9, 23, 0, 0, 0, GameOffset);
        if (EntrySlot(midnight.AddMinutes(-2)) != midnight || EntrySlot(midnight) is not null)
            throw new InvalidOperationException("Janela de meia-noite do Boss do Amor incorreta.");
        var monday = new DateTimeOffset(2026, 9, 28, 4, 0, 0, GameOffset);
        if (WeeklyKey(monday.AddSeconds(-1)) != "2026-09-21" ||
            WeeklyKey(monday) != "2026-09-28")
            throw new InvalidOperationException("Reset semanal do Boss do Amor incorreto.");
    }
}
