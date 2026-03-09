using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Parses cron expressions and calculates next run times
/// Supports standard 5-field cron format: minute hour day-of-month month day-of-week
///
/// Field values:
/// - minute: 0-59
/// - hour: 0-23
/// - day-of-month: 1-31
/// - month: 1-12
/// - day-of-week: 0-6 (0 = Sunday)
///
/// Special characters:
/// - * : any value
/// - , : value list separator (e.g., 1,3,5)
/// - - : range of values (e.g., 1-5)
/// - / : step values (e.g., */15 for every 15)
/// </summary>
public class CronExpressionParser
{
    /// <summary>
    /// Calculate the next run time for a cron job based on its schedule configuration
    /// </summary>
    public static DateTime? GetNextRunTime(CronJob job, DateTime? fromTime = null)
    {
        var now = fromTime ?? DateTime.UtcNow;

        // Apply timezone if specified
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(job.Timezone);
        }
        catch
        {
            // Fallback to UTC if timezone is invalid
            tz = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, tz);

        DateTime? nextLocal = job.ScheduleType switch
        {
            ScheduleType.Cron => GetNextCronTime(job.CronExpression!, localNow),
            ScheduleType.Interval => GetNextIntervalTime(job.IntervalMinutes ?? 1, localNow, job.LastRunAt),
            ScheduleType.Daily => GetNextDailyTime(job.TimeOfDay!, localNow),
            ScheduleType.Weekly => GetNextWeeklyTime(job.TimeOfDay!, job.DaysOfWeek!, localNow),
            ScheduleType.Monthly => GetNextMonthlyTime(job.TimeOfDay!, job.DayOfMonth ?? 1, localNow),
            ScheduleType.OneTime => job.NextRunAt, // Already set
            _ => null
        };

        if (nextLocal == null) return null;

        // Convert back to UTC
        return TimeZoneInfo.ConvertTimeToUtc(nextLocal.Value, tz);
    }

    /// <summary>
    /// Parse a standard cron expression and get the next run time
    /// </summary>
    public static DateTime? GetNextCronTime(string cronExpression, DateTime from)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return null;

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return null;

        try
        {
            var minutes = ParseCronField(parts[0], 0, 59);
            var hours = ParseCronField(parts[1], 0, 23);
            var daysOfMonth = ParseCronField(parts[2], 1, 31);
            var months = ParseCronField(parts[3], 1, 12);
            var daysOfWeek = ParseCronField(parts[4], 0, 6);

            // Start from the next minute
            var candidate = from.AddMinutes(1);
            candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day,
                                     candidate.Hour, candidate.Minute, 0);

            // Search for the next matching time (max 2 years out)
            var maxDate = from.AddYears(2);

            while (candidate < maxDate)
            {
                // Check month
                if (!months.Contains(candidate.Month))
                {
                    candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0).AddMonths(1);
                    continue;
                }

                // Check day of month and day of week
                if (!daysOfMonth.Contains(candidate.Day) && !daysOfWeek.Contains((int)candidate.DayOfWeek))
                {
                    candidate = candidate.AddDays(1);
                    candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, 0, 0, 0);
                    continue;
                }

                // Check hour
                if (!hours.Contains(candidate.Hour))
                {
                    candidate = candidate.AddHours(1);
                    candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day,
                                             candidate.Hour, 0, 0);
                    continue;
                }

                // Check minute
                if (!minutes.Contains(candidate.Minute))
                {
                    candidate = candidate.AddMinutes(1);
                    continue;
                }

                // Found a match!
                return candidate;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get next interval time based on minutes since last run
    /// </summary>
    public static DateTime GetNextIntervalTime(int intervalMinutes, DateTime from, DateTime? lastRun)
    {
        if (lastRun == null)
        {
            // First run - schedule immediately or next minute
            return from.AddMinutes(1);
        }

        var next = lastRun.Value.AddMinutes(intervalMinutes);

        // If the calculated next time is in the past, calculate the next future occurrence
        while (next <= from)
        {
            next = next.AddMinutes(intervalMinutes);
        }

        return next;
    }

    /// <summary>
    /// Get next daily time based on time of day
    /// </summary>
    public static DateTime? GetNextDailyTime(string timeOfDay, DateTime from)
    {
        if (!TryParseTimeOfDay(timeOfDay, out var hour, out var minute))
            return null;

        var today = new DateTime(from.Year, from.Month, from.Day, hour, minute, 0);

        if (today > from)
            return today;

        return today.AddDays(1);
    }

    /// <summary>
    /// Get next weekly time based on time of day and days of week
    /// </summary>
    public static DateTime? GetNextWeeklyTime(string timeOfDay, string daysOfWeek, DateTime from)
    {
        if (!TryParseTimeOfDay(timeOfDay, out var hour, out var minute))
            return null;

        var days = daysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(d => int.TryParse(d.Trim(), out var day) ? day : -1)
                             .Where(d => d >= 0 && d <= 6)
                             .ToHashSet();

        if (days.Count == 0)
            return null;

        // Search up to 8 days out (covers all days of week)
        for (int i = 0; i < 8; i++)
        {
            var candidate = from.Date.AddDays(i);
            candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, hour, minute, 0);

            if (candidate > from && days.Contains((int)candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Get next monthly time based on time of day and day of month
    /// </summary>
    public static DateTime? GetNextMonthlyTime(string timeOfDay, int dayOfMonth, DateTime from)
    {
        if (!TryParseTimeOfDay(timeOfDay, out var hour, out var minute))
            return null;

        var candidate = from.Date;

        // Search up to 13 months out
        for (int i = 0; i < 13; i++)
        {
            var month = candidate.AddMonths(i);
            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

            // Handle "last day of month" (dayOfMonth = 0 or > days in month)
            var actualDay = dayOfMonth <= 0 || dayOfMonth > daysInMonth
                ? daysInMonth
                : dayOfMonth;

            var target = new DateTime(month.Year, month.Month, actualDay, hour, minute, 0);

            if (target > from)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// Parse a single cron field and return all matching values
    /// </summary>
    private static HashSet<int> ParseCronField(string field, int min, int max)
    {
        var values = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            var trimmed = part.Trim();

            // Handle step values (e.g., */5 or 1-10/2)
            int step = 1;
            var stepParts = trimmed.Split('/');
            if (stepParts.Length == 2)
            {
                trimmed = stepParts[0];
                step = int.Parse(stepParts[1]);
            }

            if (trimmed == "*")
            {
                // All values with step
                for (int i = min; i <= max; i += step)
                    values.Add(i);
            }
            else if (trimmed.Contains('-'))
            {
                // Range (e.g., 1-5)
                var rangeParts = trimmed.Split('-');
                var rangeStart = int.Parse(rangeParts[0]);
                var rangeEnd = int.Parse(rangeParts[1]);

                for (int i = rangeStart; i <= rangeEnd && i <= max; i += step)
                    values.Add(i);
            }
            else
            {
                // Single value
                var value = int.Parse(trimmed);
                if (value >= min && value <= max)
                    values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Parse time of day string (HH:mm format)
    /// </summary>
    private static bool TryParseTimeOfDay(string timeOfDay, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;

        if (string.IsNullOrWhiteSpace(timeOfDay))
            return false;

        var parts = timeOfDay.Split(':');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out hour) || hour < 0 || hour > 23)
            return false;

        if (!int.TryParse(parts[1], out minute) || minute < 0 || minute > 59)
            return false;

        return true;
    }

    /// <summary>
    /// Validate a cron expression
    /// </summary>
    public static (bool IsValid, string? Error) ValidateCronExpression(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return (false, "Cron expression cannot be empty");

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return (false, "Cron expression must have 5 fields: minute hour day-of-month month day-of-week");

        try
        {
            ParseCronField(parts[0], 0, 59);
            ParseCronField(parts[1], 0, 23);
            ParseCronField(parts[2], 1, 31);
            ParseCronField(parts[3], 1, 12);
            ParseCronField(parts[4], 0, 6);

            // Try to get next run time to verify it's solvable
            var next = GetNextCronTime(cronExpression, DateTime.UtcNow);
            if (next == null)
                return (false, "Cron expression doesn't produce any valid run times");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid cron expression: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a human-readable description of the schedule
    /// </summary>
    public static string GetScheduleDescription(CronJob job)
    {
        return job.ScheduleType switch
        {
            ScheduleType.Cron => $"Cron: {job.CronExpression}",
            ScheduleType.Interval => $"Every {job.IntervalMinutes} minute(s)",
            ScheduleType.Daily => $"Daily at {job.TimeOfDay}",
            ScheduleType.Weekly => $"Weekly on {FormatDaysOfWeek(job.DaysOfWeek)} at {job.TimeOfDay}",
            ScheduleType.Monthly => $"Monthly on day {job.DayOfMonth} at {job.TimeOfDay}",
            ScheduleType.OneTime => job.NextRunAt.HasValue
                ? $"Once at {job.NextRunAt:yyyy-MM-dd HH:mm}"
                : "One-time (not scheduled)",
            _ => "Unknown schedule"
        };
    }

    private static string FormatDaysOfWeek(string? daysOfWeek)
    {
        if (string.IsNullOrEmpty(daysOfWeek))
            return "no days";

        var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var days = daysOfWeek.Split(',')
            .Select(d => int.TryParse(d.Trim(), out var day) && day >= 0 && day <= 6
                ? dayNames[day]
                : null)
            .Where(d => d != null);

        return string.Join(", ", days);
    }
}
