namespace Dimes.Domain.Entities;

/// <summary>One change as it appeared in one exported work order, and how far its round-trip got. The
/// snapshots are what the export actually said, so the item still reads true — and a branch-named report
/// still resolves — after the change itself is edited.</summary>
public class WorkOrderItem : Entity
{
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = default!;

    public Guid ChangeRequestId { get; set; }
    public ChangeRequest ChangeRequest { get; set; } = default!;

    public required string TitleSnapshot { get; set; }

    /// <summary>The deterministic <c>change/&lt;id8&gt;-&lt;slug&gt;</c> name the export told the agent to
    /// use. Persisting it is what makes branch-based matching an exact comparison against a string Dimes
    /// itself minted, rather than a heuristic (see <see cref="WorkOrders.WorkOrderTrailer"/>).</summary>
    public required string BranchName { get; set; }

    public WorkOrderItemStatus Status { get; set; } = WorkOrderItemStatus.Pending;

    /// <summary>The agent's own words: its blocked reason, or the summary it reported alongside.</summary>
    public string? ReportNote { get; set; }

    public DateTimeOffset? ReportedAt { get; set; }
}
