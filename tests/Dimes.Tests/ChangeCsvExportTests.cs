using Dimes.Api;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>The CSV export is a read-only snapshot of the change list, ordered along the lifecycle spine
/// (Captured first, Done last) and by change number within a status. Unlike the work-order export it mints
/// nothing — so these tests care about ordering, what's excluded, and the two ways a spreadsheet quietly
/// corrupts or executes exported text.</summary>
public sealed class ChangeCsvExportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ChangeRequestService _changes;
    private Project _project = null!;
    private Actor _actor = null!;

    public ChangeCsvExportTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier(), new NotificationDispatcher(_db));
    }

    private async Task SeedProjectAsync(string key = "DIMES")
    {
        _project = new Project { Name = "Demo", Key = key };
        _actor = new Actor { DisplayName = "Mae", Type = ActorType.Human };
        _db.Projects.Add(_project);
        _db.Actors.Add(_actor);
        await _db.SaveChangesAsync();
    }

    private async Task AddAsync(
        string title, ChangeStatus status, int? number, Guid? assignee = null, ChangeKind kind = ChangeKind.Feature)
    {
        _db.ChangeRequests.Add(new ChangeRequest
        {
            ProjectId = _project.Id,
            Title = title,
            Status = status,
            Kind = kind,
            Number = number,
            AssigneeActorId = assignee,
            CreatedByActorId = _actor.Id,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>Data rows only — the header is asserted separately.</summary>
    private static string[] Rows(string csv) =>
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

    [Fact]
    public async Task Export_OrdersAlongTheSpine_ThenByNumber()
    {
        await SeedProjectAsync();
        // Seeded deliberately out of order, and with numbers that disagree with insertion order, so
        // neither insertion order nor a lucky alphabetical Status sort could produce a passing result.
        await AddAsync("Done thing", ChangeStatus.Done, 1);
        await AddAsync("Captured second", ChangeStatus.Captured, 9);
        await AddAsync("In review thing", ChangeStatus.InReview, 4);
        await AddAsync("Captured first", ChangeStatus.Captured, 2);
        await AddAsync("Approved thing", ChangeStatus.Approved, 7);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        var titles = Rows(csv).Select(r => r.Split(',')[1]).ToArray();
        Assert.Equal(
            ["Captured first", "Captured second", "Approved thing", "In review thing", "Done thing"],
            titles);
    }

    [Fact]
    public async Task Export_ExcludesRejectedAndDuplicate()
    {
        await SeedProjectAsync();
        await AddAsync("Live one", ChangeStatus.Captured, 1);
        await AddAsync("Rejected one", ChangeStatus.Rejected, 2);
        await AddAsync("Duplicate one", ChangeStatus.Duplicate, 3);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        Assert.Single(Rows(csv));
        Assert.Contains("Live one", csv);
        Assert.DoesNotContain("Rejected one", csv);
        Assert.DoesNotContain("Duplicate one", csv);
    }

    [Fact]
    public async Task Export_HeaderAndColumns_UseTheAppsOwnVocabulary()
    {
        await SeedProjectAsync();
        await AddAsync("A feature", ChangeStatus.InDevelopment, 42, assignee: _actor.Id);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        Assert.StartsWith("Key,Title,Status,Kind,Priority,Recipient,CreatedAt,CompletedAt\r\n", csv);
        var cells = Rows(csv).Single().Split(',');
        Assert.Equal("DIMES-42", cells[0]);
        // Not "InDevelopment": the wire format mirrors the C# enum, but a spreadsheet is read by people.
        Assert.Equal("In Development", cells[2]);
        Assert.Equal("Mae", cells[5]);
    }

    [Fact]
    public async Task Export_UnnumberedChange_SortsLastAndHasABlankKey()
    {
        await SeedProjectAsync();
        // Number is nullable only until the startup backfill reaches a row. Such a row must not sort ahead
        // of DIMES-1, and must not render a half-built key.
        await AddAsync("Not yet numbered", ChangeStatus.Captured, null);
        await AddAsync("Numbered", ChangeStatus.Captured, 1);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        var rows = Rows(csv);
        Assert.Equal("DIMES-1", rows[0].Split(',')[0]);
        Assert.Equal(string.Empty, rows[1].Split(',')[0]);
        Assert.Equal("Not yet numbered", rows[1].Split(',')[1]);
    }

    [Fact]
    public async Task Export_ProjectWithoutAKey_LeavesTheKeyBlankRatherThanEmittingABareNumber()
    {
        await SeedProjectAsync(key: null!);
        await AddAsync("Keyless", ChangeStatus.Captured, 3);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        // "3" alone would read as a key and mislead.
        Assert.Equal(string.Empty, Rows(csv).Single().Split(',')[0]);
    }

    [Fact]
    public async Task Export_UnassignedChange_SaysUnassigned()
    {
        await SeedProjectAsync();
        await AddAsync("Nobody's", ChangeStatus.Captured, 1);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        var cells = Rows(csv).Single().Split(',');
        Assert.Equal("Unassigned", cells[5]);   // the word the create modal and detail view already use
        Assert.Equal(string.Empty, cells[7]);   // CompletedAt: only Done carries one
    }

    [Fact]
    public async Task Export_TitleThatLooksLikeAFormula_IsNeutralized()
    {
        await SeedProjectAsync();
        // Titles arrive from an SDK embedded in host apps and from LLM synthesis — untrusted text that a
        // spreadsheet will execute on open unless it's forced to read as text.
        await AddAsync("=cmd|'/c calc'!A1", ChangeStatus.Captured, 1);
        await AddAsync("+1 to this", ChangeStatus.Captured, 2);
        await AddAsync("@import evil", ChangeStatus.Captured, 3);
        await AddAsync("-1 regression", ChangeStatus.Captured, 4);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        Assert.Contains("'=cmd|'/c calc'!A1", csv);
        Assert.Contains("'+1 to this", csv);
        Assert.Contains("'@import evil", csv);
        Assert.Contains("'-1 regression", csv);
        Assert.DoesNotContain(",=cmd", csv);
    }

    [Fact]
    public async Task Export_TitleWithCommaQuoteOrNewline_IsEscapedPerRfc4180()
    {
        await SeedProjectAsync();
        await AddAsync("Fix a, b and c", ChangeStatus.Captured, 1);
        await AddAsync("The \"quoted\" one", ChangeStatus.Captured, 2);
        await AddAsync("Line one\nline two", ChangeStatus.Captured, 3);

        var csv = (await _changes.ExportChangesCsvAsync(_project.Id)).Csv;

        Assert.Contains("\"Fix a, b and c\"", csv);
        Assert.Contains("\"The \"\"quoted\"\" one\"", csv);
        Assert.Contains("\"Line one\nline two\"", csv);
        // The embedded newline must live inside quotes, not split the row: 3 changes = 3 data rows.
        Assert.Equal(3, Rows(csv).Length);
    }

    [Fact]
    public async Task Export_FileName_IsSluggedAndStamped()
    {
        await SeedProjectAsync();

        var export = await _changes.ExportChangesCsvAsync(_project.Id);

        Assert.StartsWith("demo-changes-", export.FileName);
        Assert.EndsWith(".csv", export.FileName);
    }

    [Fact]
    public async Task Export_ForAMissingProject_IsNotFound()
    {
        await SeedProjectAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _changes.ExportChangesCsvAsync(Guid.NewGuid()));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
