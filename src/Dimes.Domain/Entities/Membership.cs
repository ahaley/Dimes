namespace Dimes.Domain.Entities;

/// <summary>Binds an <see cref="Actor"/> to a <see cref="Project"/> with a <see cref="MemberRole"/>.
/// RBAC is scoped per project; the Maintainer role holds the whitelist (approve) authority.</summary>
public class Membership : Entity
{
    public Guid ActorId { get; set; }
    public Actor Actor { get; set; } = default!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public MemberRole Role { get; set; }
}
