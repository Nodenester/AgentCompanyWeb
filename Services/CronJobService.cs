using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for managing cron jobs
/// </summary>
public class CronJobService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<CronJobService> _logger;

    public CronJobService(IDbContextFactory<AppDbContext> dbFactory, ILogger<CronJobService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Get all cron jobs for a group
    /// </summary>
    public async Task<List<CronJob>> GetJobsByGroupAsync(int groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.CronJobs
            .Include(j => j.TargetAgent)
            .Include(j => j.TargetTeam)
            .Include(j => j.SenderAgent)
            .Where(j => j.GroupId == groupId)
            .OrderBy(j => j.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get all enabled cron jobs that are due to run
    /// </summary>
    public async Task<List<CronJob>> GetDueJobsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        return await db.CronJobs
            .Include(j => j.Group)
            .Include(j => j.TargetAgent)
            .Include(j => j.TargetTeam)
            .Include(j => j.SenderAgent)
            .Where(j => j.Enabled
                && j.NextRunAt != null
                && j.NextRunAt <= now
                && (j.ExpiresAt == null || j.ExpiresAt > now)
                && (j.MaxExecutions == null || j.ExecutionCount < j.MaxExecutions))
            .ToListAsync();
    }

    /// <summary>
    /// Get a cron job by ID
    /// </summary>
    public async Task<CronJob?> GetJobAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.CronJobs
            .Include(j => j.Group)
            .Include(j => j.TargetAgent)
            .Include(j => j.TargetTeam)
            .Include(j => j.SenderAgent)
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    /// <summary>
    /// Create a new cron job
    /// </summary>
    public async Task<CronJob> CreateJobAsync(CronJob job)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        job.Id = Guid.NewGuid().ToString();
        job.CreatedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        // Calculate next run time
        job.NextRunAt = CronExpressionParser.GetNextRunTime(job);

        db.CronJobs.Add(job);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created cron job '{Name}' (ID: {Id}) for group {GroupId}, next run: {NextRun}",
            job.Name, job.Id, job.GroupId, job.NextRunAt);

        return job;
    }

    /// <summary>
    /// Update an existing cron job
    /// </summary>
    public async Task<CronJob?> UpdateJobAsync(string id, Action<CronJob> updateAction)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var job = await db.CronJobs.FindAsync(id);
        if (job == null) return null;

        updateAction(job);
        job.UpdatedAt = DateTime.UtcNow;

        // Recalculate next run time if schedule changed
        job.NextRunAt = CronExpressionParser.GetNextRunTime(job);

        await db.SaveChangesAsync();

        _logger.LogInformation("Updated cron job '{Name}' (ID: {Id}), next run: {NextRun}",
            job.Name, job.Id, job.NextRunAt);

        return job;
    }

    /// <summary>
    /// Delete a cron job
    /// </summary>
    public async Task<bool> DeleteJobAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var job = await db.CronJobs.FindAsync(id);
        if (job == null) return false;

        db.CronJobs.Remove(job);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted cron job '{Name}' (ID: {Id})", job.Name, id);

        return true;
    }

    /// <summary>
    /// Toggle job enabled status
    /// </summary>
    public async Task<bool> ToggleJobAsync(string id, bool enabled)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var job = await db.CronJobs.FindAsync(id);
        if (job == null) return false;

        job.Enabled = enabled;
        job.UpdatedAt = DateTime.UtcNow;

        if (enabled)
        {
            job.NextRunAt = CronExpressionParser.GetNextRunTime(job);
        }

        await db.SaveChangesAsync();

        _logger.LogInformation("Toggled cron job '{Name}' (ID: {Id}) to {Status}",
            job.Name, id, enabled ? "enabled" : "disabled");

        return true;
    }

    /// <summary>
    /// Update job after execution
    /// </summary>
    public async Task UpdateJobExecutionAsync(string id, JobExecutionStatus status, string? error = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var job = await db.CronJobs.FindAsync(id);
        if (job == null) return;

        job.LastRunAt = DateTime.UtcNow;
        job.LastRunStatus = status;
        job.LastRunError = error;
        job.ExecutionCount++;
        job.UpdatedAt = DateTime.UtcNow;

        // Calculate next run time
        if (job.ScheduleType != ScheduleType.OneTime)
        {
            job.NextRunAt = CronExpressionParser.GetNextRunTime(job);
        }
        else
        {
            job.NextRunAt = null; // One-time jobs don't run again
        }

        // Disable if max executions reached
        if (job.MaxExecutions.HasValue && job.ExecutionCount >= job.MaxExecutions)
        {
            job.Enabled = false;
            _logger.LogInformation("Cron job '{Name}' reached max executions ({Max}), disabled",
                job.Name, job.MaxExecutions);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Add execution log entry
    /// </summary>
    public async Task<CronJobLog> AddLogAsync(CronJobLog log)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        log.Id = Guid.NewGuid().ToString();
        db.CronJobLogs.Add(log);
        await db.SaveChangesAsync();

        return log;
    }

    /// <summary>
    /// Update execution log entry
    /// </summary>
    public async Task UpdateLogAsync(string logId, JobExecutionStatus status, string? result = null, string? error = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var log = await db.CronJobLogs.FindAsync(logId);
        if (log == null) return;

        log.Status = status;
        log.CompletedAt = DateTime.UtcNow;
        log.DurationMs = (long)(log.CompletedAt.Value - log.StartedAt).TotalMilliseconds;
        log.Result = result;
        log.Error = error;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Get execution logs for a job
    /// </summary>
    public async Task<List<CronJobLog>> GetLogsAsync(string jobId, int limit = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.CronJobLogs
            .Where(l => l.CronJobId == jobId)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Get recent logs for a group
    /// </summary>
    public async Task<List<CronJobLog>> GetRecentLogsAsync(int groupId, int limit = 100)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.CronJobLogs
            .Include(l => l.CronJob)
            .Where(l => l.CronJob != null && l.CronJob.GroupId == groupId)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Clean up old log entries
    /// </summary>
    public async Task<int> CleanupLogsAsync(int daysToKeep = 30)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var oldLogs = await db.CronJobLogs
            .Where(l => l.StartedAt < cutoff)
            .ToListAsync();

        if (oldLogs.Count > 0)
        {
            db.CronJobLogs.RemoveRange(oldLogs);
            await db.SaveChangesAsync();
            _logger.LogInformation("Cleaned up {Count} old cron job logs", oldLogs.Count);
        }

        return oldLogs.Count;
    }

    /// <summary>
    /// Render message template with variable substitution
    /// </summary>
    public string RenderMessage(CronJob job, Dictionary<string, string>? additionalVars = null)
    {
        var message = job.MessageTemplate;
        var now = DateTime.UtcNow;

        // Apply timezone
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(job.Timezone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, tz);

        // Standard variables
        message = message.Replace("{date}", localNow.ToString("yyyy-MM-dd"));
        message = message.Replace("{time}", localNow.ToString("HH:mm:ss"));
        message = message.Replace("{datetime}", localNow.ToString("yyyy-MM-dd HH:mm:ss"));
        message = message.Replace("{timestamp}", now.ToString("O"));

        // Entity variables
        message = message.Replace("{agent_name}", job.TargetAgent?.Name ?? "Unknown");
        message = message.Replace("{team_name}", job.TargetTeam?.Name ?? "Unknown");
        message = message.Replace("{group_name}", job.Group?.Name ?? "Unknown");
        message = message.Replace("{job_name}", job.Name);
        message = message.Replace("{execution_count}", (job.ExecutionCount + 1).ToString());

        // Additional custom variables
        if (additionalVars != null)
        {
            foreach (var (key, value) in additionalVars)
            {
                message = message.Replace($"{{{key}}}", value);
            }
        }

        return message;
    }
}
