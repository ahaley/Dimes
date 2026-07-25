using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Fixture sugar: create a project as an unrestricted site admin — the behaviour before project
/// creation was quota-bounded. Most tests only need a project to hang changes off, so this keeps their
/// setup a one-liner.
///
/// It takes the context because <see cref="Project.CreatedByActorId"/> is a real FK, so the creator has to
/// exist; a single reusable fixture admin is seeded on first use. Because that caller is a site admin, no
/// creator <c>Membership</c> is written — which is what the existing project-list and archive assertions
/// expect (a site admin's own project reports no role).
///
/// Tests that exercise the quota itself (see <c>ProjectQuotaTests</c>) call the real three-argument
/// <see cref="ProjectService.CreateAsync"/> directly with an actor they control.</summary>
internal static class TestProjects
{
    private const string FixtureAdminEmail = "fixture-admin@test.local";

    public static async Task<ProjectDto> CreateAsync(
        this ProjectService projects, DimesDbContext db, CreateProjectRequest req, CancellationToken ct = default)
    {
        var admin = await db.Actors.FirstOrDefaultAsync(a => a.Email == FixtureAdminEmail, ct);
        if (admin is null)
        {
            admin = new Actor
            {
                DisplayName = "Fixture Admin",
                Type = ActorType.Human,
                Email = FixtureAdminEmail,
                IsSiteAdmin = true,
            };
            db.Actors.Add(admin);
            await db.SaveChangesAsync(ct);
        }

        return await projects.CreateAsync(req, admin.Id, callerIsSiteAdmin: true, ct);
    }
}
