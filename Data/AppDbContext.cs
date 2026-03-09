using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    // Agent Network tables
    public DbSet<NetworkMessage> NetworkMessages => Set<NetworkMessage>();
    public DbSet<NetworkConnection> NetworkConnections => Set<NetworkConnection>();
    public DbSet<NetworkGroup> NetworkGroups => Set<NetworkGroup>();

    // Group Credentials (flexible credential storage)
    public DbSet<GroupCredential> GroupCredentials => Set<GroupCredential>();

    // Cron Jobs and Triggers
    public DbSet<CronJob> CronJobs => Set<CronJob>();
    public DbSet<CronJobLog> CronJobLogs => Set<CronJobLog>();
    public DbSet<Trigger> Triggers => Set<Trigger>();
    public DbSet<TriggerLog> TriggerLogs => Set<TriggerLog>();

    // GitHub Webhooks
    public DbSet<GitHubWebhook> GitHubWebhooks => Set<GitHubWebhook>();
    public DbSet<GitHubWebhookLog> GitHubWebhookLogs => Set<GitHubWebhookLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Group configuration
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired().HasMaxLength(100);
            entity.Property(g => g.Color).HasMaxLength(20).HasDefaultValue("blue");
            entity.HasIndex(g => g.Name).IsUnique();
        });

        // Agent configuration
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Role).HasMaxLength(50);
            entity.HasOne(a => a.Group)
                  .WithMany(g => g.Agents)
                  .HasForeignKey(a => a.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Team configuration
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Color).HasMaxLength(20).HasDefaultValue("blue");
            entity.HasOne(t => t.Group)
                  .WithMany(g => g.Teams)
                  .HasForeignKey(t => t.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // TeamMember (junction) configuration
        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasKey(tm => tm.Id);
            entity.HasOne(tm => tm.Agent)
                  .WithMany(a => a.TeamMemberships)
                  .HasForeignKey(tm => tm.AgentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(tm => tm.Team)
                  .WithMany(t => t.Members)
                  .HasForeignKey(tm => tm.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(tm => new { tm.AgentId, tm.TeamId }).IsUnique();
        });

        // NetworkMessage configuration
        modelBuilder.Entity<NetworkMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.From).IsRequired().HasMaxLength(100);
            entity.Property(m => m.To).HasMaxLength(100);
            entity.Property(m => m.GroupName).HasMaxLength(100);
            entity.Property(m => m.Content).IsRequired();
            entity.HasIndex(m => m.Timestamp);
            entity.HasIndex(m => new { m.From, m.To }); // For DM conversations
            entity.HasIndex(m => m.GroupName); // For group messages
        });

        // NetworkConnection configuration
        modelBuilder.Entity<NetworkConnection>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.AgentName).IsRequired().HasMaxLength(100);
            entity.HasIndex(c => c.AgentName).IsUnique();
            entity.Property(c => c.Status).HasMaxLength(20).HasDefaultValue("offline");
            entity.HasIndex(c => c.LastSeen);
        });

        // NetworkGroup configuration
        modelBuilder.Entity<NetworkGroup>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(g => g.Name).IsUnique();
        });

        // GroupCredential configuration
        modelBuilder.Entity<GroupCredential>(entity =>
        {
            entity.HasKey(gc => gc.Id);
            entity.Property(gc => gc.CredentialType).IsRequired().HasMaxLength(50);
            entity.Property(gc => gc.EncryptedValue).IsRequired();
            entity.HasOne(gc => gc.Group)
                  .WithMany()
                  .HasForeignKey(gc => gc.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(gc => new { gc.GroupId, gc.CredentialType }).IsUnique();
        });

        // CronJob configuration
        modelBuilder.Entity<CronJob>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Description).HasMaxLength(500);
            entity.Property(c => c.CronExpression).HasMaxLength(100);
            entity.Property(c => c.TimeOfDay).HasMaxLength(5);
            entity.Property(c => c.DaysOfWeek).HasMaxLength(20);
            entity.Property(c => c.Timezone).HasMaxLength(50).HasDefaultValue("UTC");
            entity.Property(c => c.LastRunError).HasMaxLength(1000);
            entity.HasOne(c => c.Group)
                  .WithMany()
                  .HasForeignKey(c => c.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.TargetAgent)
                  .WithMany()
                  .HasForeignKey(c => c.TargetAgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(c => c.TargetTeam)
                  .WithMany()
                  .HasForeignKey(c => c.TargetTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(c => c.SenderAgent)
                  .WithMany()
                  .HasForeignKey(c => c.SenderAgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(c => c.GroupId);
            entity.HasIndex(c => c.NextRunAt);
            entity.HasIndex(c => c.Enabled);
        });

        // CronJobLog configuration
        modelBuilder.Entity<CronJobLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasOne(l => l.CronJob)
                  .WithMany(c => c.Logs)
                  .HasForeignKey(l => l.CronJobId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(l => l.Target).HasMaxLength(200);
            entity.HasIndex(l => l.CronJobId);
            entity.HasIndex(l => l.StartedAt);
            entity.HasIndex(l => l.Status);
        });

        // Trigger configuration
        modelBuilder.Entity<Trigger>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Description).HasMaxLength(500);
            entity.HasOne(t => t.Group)
                  .WithMany()
                  .HasForeignKey(t => t.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.SourceAgent)
                  .WithMany()
                  .HasForeignKey(t => t.SourceAgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(t => t.SourceTeam)
                  .WithMany()
                  .HasForeignKey(t => t.SourceTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(t => t.TargetAgent)
                  .WithMany()
                  .HasForeignKey(t => t.TargetAgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(t => t.TargetTeam)
                  .WithMany()
                  .HasForeignKey(t => t.TargetTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(t => t.GroupId);
            entity.HasIndex(t => t.Enabled);
            entity.HasIndex(t => t.EventType);
        });

        // TriggerLog configuration
        modelBuilder.Entity<TriggerLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasOne(l => l.Trigger)
                  .WithMany(t => t.Logs)
                  .HasForeignKey(l => l.TriggerId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(l => l.Target).HasMaxLength(200);
            entity.HasIndex(l => l.TriggerId);
            entity.HasIndex(l => l.ExecutedAt);
            entity.HasIndex(l => l.Status);
        });

        // GitHubWebhook configuration
        modelBuilder.Entity<GitHubWebhook>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired().HasMaxLength(100);
            entity.Property(w => w.Description).HasMaxLength(500);
            entity.Property(w => w.Repository).IsRequired().HasMaxLength(200);
            entity.Property(w => w.WebhookSecret).HasMaxLength(100);
            entity.Property(w => w.BranchFilter).HasMaxLength(200);
            entity.HasOne(w => w.Group)
                  .WithMany()
                  .HasForeignKey(w => w.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(w => w.TargetAgent)
                  .WithMany()
                  .HasForeignKey(w => w.TargetAgentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(w => w.TargetTeam)
                  .WithMany()
                  .HasForeignKey(w => w.TargetTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(w => w.GroupId);
            entity.HasIndex(w => w.Enabled);
        });

        // GitHubWebhookLog configuration
        modelBuilder.Entity<GitHubWebhookLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.EventType).IsRequired().HasMaxLength(50);
            entity.Property(l => l.EventAction).HasMaxLength(50);
            entity.Property(l => l.DeliveryId).HasMaxLength(50);
            entity.Property(l => l.Repository).HasMaxLength(200);
            entity.Property(l => l.Branch).HasMaxLength(200);
            entity.Property(l => l.Sender).HasMaxLength(100);
            entity.Property(l => l.Target).HasMaxLength(200);
            entity.HasOne(l => l.Webhook)
                  .WithMany()
                  .HasForeignKey(l => l.WebhookId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(l => l.WebhookId);
            entity.HasIndex(l => l.ReceivedAt);
            entity.HasIndex(l => l.Status);
        });

        // Seed data - create a default group
        modelBuilder.Entity<Group>().HasData(new Group
        {
            Id = 1,
            Name = "Default",
            Description = "Default group for agents",
            Color = "blue",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
}
