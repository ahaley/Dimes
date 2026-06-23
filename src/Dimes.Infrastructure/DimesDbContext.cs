using System.Reflection;
using Dimes.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dimes.Infrastructure;

/// <summary>
/// The Dimes persistence context. Defaults to SQLite (configured at registration time);
/// the same model runs on PostgreSQL for heavier installs. Enums are stored as strings for a
/// human-readable, ordinal-stable database, and actor-referencing relationships use Restrict to
/// keep the model free of multiple-cascade-path conflicts on stricter providers.
/// </summary>
public class DimesDbContext(DbContextOptions<DimesDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ObservationSource> ObservationSources => Set<ObservationSource>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ScmLink> ScmLinks => Set<ScmLink>();
    public DbSet<LlmProviderConfig> LlmProviderConfigs => Set<LlmProviderConfig>();
    public DbSet<ScmProviderConfig> ScmProviderConfigs => Set<ScmProviderConfig>();
    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();
    public DbSet<AssistConversation> AssistConversations => Set<AssistConversation>();
    public DbSet<AssistMessage> AssistMessages => Set<AssistMessage>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    // Backing store for the ASP.NET Core Data Protection key ring (encrypts the BFF session cookie),
    // so cookies stay valid across restarts/deploys. Managed by the framework — not a domain entity.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Membership>(b =>
        {
            // One role per actor per project.
            b.HasIndex(m => new { m.ActorId, m.ProjectId }).IsUnique();
            b.HasOne(m => m.Actor).WithMany(a => a.Memberships)
                .HasForeignKey(m => m.ActorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(m => m.Project).WithMany(p => p.Memberships)
                .HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Actor>(b =>
        {
            b.HasOne(a => a.LlmProviderConfig).WithMany()
                .HasForeignKey(a => a.LlmProviderConfigId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Observation>(b =>
        {
            // Clustering / inbox queries.
            b.HasIndex(o => new { o.ProjectId, o.Fingerprint });
            b.HasIndex(o => new { o.ProjectId, o.Status });
            // Directed-inbox query: an assistant's pending AssistRequests.
            b.HasIndex(o => new { o.ProjectId, o.TargetActorId, o.Status });
            b.HasOne(o => o.Project).WithMany(p => p.Observations)
                .HasForeignKey(o => o.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(o => o.Source).WithMany(s => s.Observations)
                .HasForeignKey(o => o.SourceId).OnDelete(DeleteBehavior.Restrict);
            // Evidence link: detaching a change must not delete the observation.
            b.HasOne(o => o.ChangeRequest).WithMany(c => c.Evidence)
                .HasForeignKey(o => o.ChangeRequestId).OnDelete(DeleteBehavior.SetNull);
            // Directed target (Capture Assist): Restrict matches the actor-FK convention.
            b.HasOne(o => o.TargetActor).WithMany()
                .HasForeignKey(o => o.TargetActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssistConversation>(b =>
        {
            // Pending-requests query: an assistant's open conversations.
            b.HasIndex(c => new { c.ProjectId, c.AssistantActorId, c.Status });
            b.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            // Actor FKs use Restrict (avoid multiple-cascade-path conflicts; keep history valid).
            b.HasOne(c => c.Requester).WithMany()
                .HasForeignKey(c => c.RequesterActorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.Assistant).WithMany()
                .HasForeignKey(c => c.AssistantActorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.Observation).WithMany()
                .HasForeignKey(c => c.ObservationId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(c => c.ChangeRequest).WithMany()
                .HasForeignKey(c => c.ChangeRequestId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssistMessage>(b =>
        {
            b.HasIndex(m => m.ConversationId);
            b.HasOne(m => m.Conversation).WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Author).WithMany()
                .HasForeignKey(m => m.AuthorActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(b =>
        {
            // Project keys are globally unique (NULLs permitted pre-backfill, and distinct in a unique index).
            b.HasIndex(p => p.Key).IsUnique();
        });

        modelBuilder.Entity<ChangeRequest>(b =>
        {
            b.HasIndex(c => new { c.ProjectId, c.Status });
            // Per-project display number is unique (NULLs distinct, so pre-backfill rows don't collide).
            b.HasIndex(c => new { c.ProjectId, c.Number }).IsUnique();
            b.HasOne(c => c.Project).WithMany(p => p.ChangeRequests)
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.CreatedBy).WithMany()
                .HasForeignKey(c => c.CreatedByActorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.Assignee).WithMany()
                .HasForeignKey(c => c.AssigneeActorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.DuplicateOf).WithMany()
                .HasForeignKey(c => c.DuplicateOfId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Comment>(b =>
        {
            b.HasOne(c => c.ChangeRequest).WithMany(cr => cr.Comments)
                .HasForeignKey(c => c.ChangeRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.Author).WithMany()
                .HasForeignKey(c => c.AuthorActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScmLink>(b =>
        {
            b.HasOne(l => l.ChangeRequest).WithMany(cr => cr.ScmLinks)
                .HasForeignKey(l => l.ChangeRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            // Append-only and polymorphic: EntityId is a loose reference, indexed for lookup.
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.HasOne(e => e.Actor).WithMany()
                .HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScmProviderConfig>(b =>
        {
            b.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmProviderConfig>(b =>
        {
            b.HasOne(c => c.Project).WithMany()
                .HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteSettings>(b =>
        {
            // Single-row site config; cap the title so it can't break the brand layout.
            b.Property(s => s.Title).HasMaxLength(60);
        });

        modelBuilder.Entity<LocalCredential>(b =>
        {
            // One credential per actor; Restrict matches the actor-FK convention (keep history valid).
            b.HasIndex(c => c.ActorId).IsUnique();
            b.HasOne(c => c.Actor).WithOne()
                .HasForeignKey<LocalCredential>(c => c.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        StoreEnumsAsStrings(modelBuilder);
        ApplySqliteDateTimeOffsetConversion(modelBuilder);
    }

    /// <summary>SQLite's provider cannot ORDER BY a <see cref="DateTimeOffset"/> column. Storing it
    /// via the binary converter (a chronologically-sortable long) keeps time-ordered queries working.
    /// PostgreSQL handles DateTimeOffset natively, so this is applied only for SQLite.</summary>
    private void ApplySqliteDateTimeOffsetConversion(ModelBuilder modelBuilder)
    {
        if (!Database.IsSqlite())
        {
            return;
        }

        var converter = new DateTimeOffsetToBinaryConverter();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var underlying = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (underlying == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(converter);
                }
            }
        }
    }

    /// <summary>Persist every enum property as its name rather than an ordinal — readable in the
    /// database and resilient to enum reordering.</summary>
    private static void StoreEnumsAsStrings(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var underlying = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!underlying.IsEnum)
                {
                    continue;
                }

                var converterType = typeof(EnumToStringConverter<>).MakeGenericType(underlying);
                var converter = (ValueConverter)Activator.CreateInstance(converterType)!;
                property.SetValueConverter(converter);
            }
        }
    }
}
