using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>
/// Exercises the EF model + migration end-to-end on an in-memory SQLite database, and proves the
/// lifecycle engine's audit events persist alongside the status change. The connection is held open
/// for the test's lifetime so the in-memory schema survives between context instances.
/// </summary>
public sealed class DimesPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DimesDbContext> _options;
    private readonly LifecycleService _lifecycle = new();

    public DimesPersistenceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DimesDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new DimesDbContext(_options);
        db.Database.Migrate();
    }

    [Fact]
    public void CaptureThroughApprove_PersistsStatusAndAuditTrail()
    {
        Guid changeId;

        // Seed a project, a maintainer, and a change at Captured.
        using (var db = new DimesDbContext(_options))
        {
            var project = new Project { Name = "Demo" };
            var maintainer = new Actor { DisplayName = "Maud", Type = ActorType.Human, Email = "maud@example.com" };
            db.AddRange(project, maintainer);
            db.Memberships.Add(new Membership
            {
                Actor = maintainer,
                Project = project,
                Role = MemberRole.Maintainer,
            });

            var change = new ChangeRequest
            {
                Project = project,
                Title = "Add export button",
                Kind = ChangeKind.Feature,
                CreatedBy = maintainer,
            };
            db.ChangeRequests.Add(change);
            db.SaveChanges();
            changeId = change.Id;

            // Drive Captured -> Triaged -> Approved, persisting each audit event.
            var a1 = _lifecycle.TransitionChange(change, ChangeStatus.Triaged, maintainer, MemberRole.Maintainer);
            var a2 = _lifecycle.TransitionChange(change, ChangeStatus.Approved, maintainer, MemberRole.Maintainer, "looks good");
            db.AuditEvents.AddRange(a1, a2);
            db.SaveChanges();
        }

        // Reload in a fresh context and assert persisted state.
        using (var db = new DimesDbContext(_options))
        {
            var change = db.ChangeRequests.Single(c => c.Id == changeId);
            Assert.Equal(ChangeStatus.Approved, change.Status);

            var trail = db.AuditEvents
                .Where(e => e.EntityType == AuditEntityType.ChangeRequest && e.EntityId == changeId)
                .OrderBy(e => e.Timestamp)
                .ToList();

            Assert.Equal(2, trail.Count);
            Assert.Equal("Captured", trail[0].FromStatus);
            Assert.Equal("Triaged", trail[0].ToStatus);
            Assert.Equal("Approved", trail[1].ToStatus);
            Assert.Equal("looks good", trail[1].Reason);
        }
    }

    [Fact]
    public void EnumsArePersistedAsStrings()
    {
        using (var db = new DimesDbContext(_options))
        {
            var project = new Project { Name = "Enum check" };
            var actor = new Actor { DisplayName = "A", Type = ActorType.Human };
            db.AddRange(project, actor);
            db.ChangeRequests.Add(new ChangeRequest
            {
                Project = project,
                Title = "x",
                Kind = ChangeKind.Problem,
                CreatedBy = actor,
            });
            db.SaveChanges();
        }

        // Read the raw column to confirm the enum persisted as its name, not an ordinal.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Status FROM ChangeRequests LIMIT 1";
        var status = (string?)cmd.ExecuteScalar();

        Assert.Equal("Captured", status);
    }

    public void Dispose() => _connection.Dispose();
}
