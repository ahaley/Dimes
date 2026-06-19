namespace Dimes.Domain.Entities;

/// <summary>A comment on a change. Recommend-only LLM output is simply a comment authored by an
/// Agent actor with <see cref="CommentKind.AgentRecommendation"/> — it never changes state.</summary>
public class Comment : Entity
{
    public Guid ChangeRequestId { get; set; }
    public ChangeRequest ChangeRequest { get; set; } = default!;

    public Guid AuthorActorId { get; set; }
    public Actor Author { get; set; } = default!;

    public required string Body { get; set; }
    public CommentKind Kind { get; set; } = CommentKind.Human;
}
