using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;

namespace Dimes.Tests;

public class LifecycleServiceTests
{
    private readonly LifecycleService _lifecycle = new();

    private static Actor Human() => new() { DisplayName = "Tester", Type = ActorType.Human };

    private static ChangeRequest ChangeAt(ChangeStatus status) => new()
    {
        Title = "Test change",
        Kind = ChangeKind.Feature,
        Status = status,
        CreatedByActorId = Guid.NewGuid(),
    };

    private static Observation ObservationAt(ObservationStatus status) => new()
    {
        Kind = ObservationKind.TechnicalError,
        Status = status,
        Payload = "{}",
    };

    [Theory]
    [InlineData(ChangeStatus.Captured, ChangeStatus.Triaged, MemberRole.Contributor)]
    [InlineData(ChangeStatus.Captured, ChangeStatus.Approved, MemberRole.Maintainer)] // approval shortcut
    [InlineData(ChangeStatus.InDevelopment, ChangeStatus.Approved, MemberRole.Maintainer)] // kick back to Approved
    [InlineData(ChangeStatus.Approved, ChangeStatus.InDevelopment, MemberRole.Contributor)]
    [InlineData(ChangeStatus.InDevelopment, ChangeStatus.InReview, MemberRole.Contributor)]
    [InlineData(ChangeStatus.InReview, ChangeStatus.InDevelopment, MemberRole.Contributor)]
    [InlineData(ChangeStatus.Done, ChangeStatus.InDevelopment, MemberRole.Contributor)] // reopen
    [InlineData(ChangeStatus.Captured, ChangeStatus.Rejected, MemberRole.Contributor)]
    public void TransitionChange_AllowedWithSufficientRole_Succeeds(
        ChangeStatus from, ChangeStatus to, MemberRole role)
    {
        var change = ChangeAt(from);

        var audit = _lifecycle.TransitionChange(change, to, Human(), role, reason: "ok");

        Assert.Equal(to, change.Status);
        Assert.Equal(AuditEntityType.ChangeRequest, audit.EntityType);
        Assert.Equal(change.Id, audit.EntityId);
        Assert.Equal(from.ToString(), audit.FromStatus);
        Assert.Equal(to.ToString(), audit.ToStatus);
        Assert.Equal("ok", audit.Reason);
    }

    [Fact]
    public void TransitionChange_ApproveGate_RequiresMaintainer()
    {
        var change = ChangeAt(ChangeStatus.Triaged);

        var ex = Assert.Throws<InsufficientRoleException>(() =>
            _lifecycle.TransitionChange(change, ChangeStatus.Approved, Human(), MemberRole.Contributor));

        Assert.Equal(MemberRole.Maintainer, ex.Required);
        Assert.Equal(ChangeStatus.Triaged, change.Status); // unchanged
    }

    [Fact]
    public void TransitionChange_CapturedToApprovedShortcut_RequiresMaintainer()
    {
        var change = ChangeAt(ChangeStatus.Captured);

        var ex = Assert.Throws<InsufficientRoleException>(() =>
            _lifecycle.TransitionChange(change, ChangeStatus.Approved, Human(), MemberRole.Contributor));

        Assert.Equal(MemberRole.Maintainer, ex.Required);
        Assert.Equal(ChangeStatus.Captured, change.Status); // unchanged
    }

    [Fact]
    public void TransitionChange_InDevelopmentToApproved_RequiresMaintainer()
    {
        var change = ChangeAt(ChangeStatus.InDevelopment);

        var ex = Assert.Throws<InsufficientRoleException>(() =>
            _lifecycle.TransitionChange(change, ChangeStatus.Approved, Human(), MemberRole.Contributor));

        Assert.Equal(MemberRole.Maintainer, ex.Required);
        Assert.Equal(ChangeStatus.InDevelopment, change.Status); // unchanged
    }

    [Fact]
    public void TransitionChange_ApproveGate_AllowsMaintainer()
    {
        var change = ChangeAt(ChangeStatus.Triaged);

        _lifecycle.TransitionChange(change, ChangeStatus.Approved, Human(), MemberRole.Maintainer);

        Assert.Equal(ChangeStatus.Approved, change.Status);
    }

    [Fact]
    public void TransitionChange_Acceptance_StampsCompletedAt_AndReopenClearsIt()
    {
        var change = ChangeAt(ChangeStatus.InReview);
        Assert.Null(change.CompletedAt);

        // In Review → Done (acceptance) stamps CompletedAt.
        _lifecycle.TransitionChange(change, ChangeStatus.Done, Human(), MemberRole.Maintainer);
        Assert.NotNull(change.CompletedAt);

        // Done → In Development (reopen) clears it.
        _lifecycle.TransitionChange(change, ChangeStatus.InDevelopment, Human(), MemberRole.Contributor);
        Assert.Null(change.CompletedAt);
    }

    [Fact]
    public void TransitionChange_DoneAcceptance_RequiresMaintainer()
    {
        var change = ChangeAt(ChangeStatus.InReview);

        Assert.Throws<InsufficientRoleException>(() =>
            _lifecycle.TransitionChange(change, ChangeStatus.Done, Human(), MemberRole.Contributor));
    }

    [Theory]
    [InlineData(ChangeStatus.Captured, ChangeStatus.Done)]
    [InlineData(ChangeStatus.Triaged, ChangeStatus.InDevelopment)] // skipping approval
    public void TransitionChange_IllegalTransition_Throws(ChangeStatus from, ChangeStatus to)
    {
        var change = ChangeAt(from);

        Assert.Throws<InvalidTransitionException>(() =>
            _lifecycle.TransitionChange(change, to, Human(), MemberRole.Maintainer));

        Assert.Equal(from, change.Status);
    }

    [Theory]
    [InlineData(ChangeStatus.Rejected, ChangeStatus.Triaged)]
    [InlineData(ChangeStatus.Duplicate, ChangeStatus.InDevelopment)]
    public void TransitionChange_FromTerminal_Throws(ChangeStatus from, ChangeStatus to)
    {
        var change = ChangeAt(from);

        Assert.Throws<InvalidTransitionException>(() =>
            _lifecycle.TransitionChange(change, to, Human(), MemberRole.Maintainer));
    }

    [Fact]
    public void TransitionChange_ToSameStatus_Throws()
    {
        var change = ChangeAt(ChangeStatus.Captured);

        Assert.Throws<InvalidTransitionException>(() =>
            _lifecycle.TransitionChange(change, ChangeStatus.Captured, Human(), MemberRole.Maintainer));
    }

    [Fact]
    public void AllowedChangeTransitions_FromTerminal_IsEmpty()
    {
        Assert.Empty(_lifecycle.AllowedChangeTransitions(ChangeStatus.Rejected));
        Assert.Empty(_lifecycle.AllowedChangeTransitions(ChangeStatus.Duplicate));
    }

    [Theory]
    [InlineData(ObservationStatus.New, ObservationStatus.Promoted)]
    [InlineData(ObservationStatus.New, ObservationStatus.Clustered)]
    [InlineData(ObservationStatus.New, ObservationStatus.Dismissed)]
    [InlineData(ObservationStatus.Clustered, ObservationStatus.Promoted)]
    public void TransitionObservation_Allowed_Succeeds(ObservationStatus from, ObservationStatus to)
    {
        var observation = ObservationAt(from);

        var audit = _lifecycle.TransitionObservation(observation, to, Human(), MemberRole.Contributor);

        Assert.Equal(to, observation.Status);
        Assert.Equal(AuditEntityType.Observation, audit.EntityType);
    }

    [Fact]
    public void TransitionObservation_RequiresAtLeastContributor()
    {
        var observation = ObservationAt(ObservationStatus.New);

        Assert.Throws<InsufficientRoleException>(() =>
            _lifecycle.TransitionObservation(observation, ObservationStatus.Promoted, Human(), MemberRole.Reporter));
    }

    [Fact]
    public void TransitionObservation_FromTerminal_Throws()
    {
        var observation = ObservationAt(ObservationStatus.Promoted);

        Assert.Throws<InvalidTransitionException>(() =>
            _lifecycle.TransitionObservation(observation, ObservationStatus.Dismissed, Human(), MemberRole.Maintainer));
    }
}
