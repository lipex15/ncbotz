namespace BotNC.App.Services;

internal static class SpecialRoutinePolicy
{
    internal static DateTimeOffset Local(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(-3));
    internal static string Day(DateTimeOffset now) => Local(now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    internal static bool GlobalOpen(DateTimeOffset now)
    {
        var local = Local(now);
        return local.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Sunday &&
            local.TimeOfDay >= new TimeSpan(21, 1, 0) && local.TimeOfDay < new TimeSpan(23, 0, 0);
    }
    internal static DateTimeOffset GlobalDeadline(DateTimeOffset now, int minutes)
    {
        var local = Local(now);
        var close = new DateTimeOffset(local.Year, local.Month, local.Day, 23, 0, 0, local.Offset);
        var requested = now.AddMinutes(Math.Clamp(minutes, 1, 120));
        return requested < close ? requested : close;
    }
    internal static bool BuffDue(DateTimeOffset now, DateTimeOffset? last) =>
        last is null || now - last.Value >= TimeSpan.FromHours(24);

    internal static void Verify()
    {
        var sunday = new DateTimeOffset(2026, 9, 27, 21, 1, 0, TimeSpan.FromHours(-3));
        if (!GlobalOpen(sunday) || GlobalOpen(sunday.AddMinutes(-1)) || GlobalOpen(sunday.AddHours(2)) ||
            GlobalOpen(sunday.AddDays(1)) || !GlobalOpen(sunday.AddDays(4)) ||
            GlobalDeadline(sunday, 120) != sunday.AddMinutes(119) ||
            GlobalDeadline(sunday.AddMinutes(90), 120) != sunday.AddMinutes(119) ||
            GlobalDeadline(sunday, 30) != sunday.AddMinutes(30) ||
            !BuffDue(sunday, null) || BuffDue(sunday, sunday.AddHours(-23)) || !BuffDue(sunday, sunday.AddHours(-24)))
            throw new InvalidOperationException("Horário Global UTC-3 ou prazo do buff inválido.");
    }
}
