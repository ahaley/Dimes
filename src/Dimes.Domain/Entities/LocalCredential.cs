namespace Dimes.Domain.Entities;

/// <summary>Local (email + password) login credential for an <see cref="Actor"/>, used when the
/// deployment runs in local-session auth mode. One per actor. The hash is produced by the API's
/// password hasher; the plaintext password is never stored.</summary>
public class LocalCredential : Entity
{
    public Guid ActorId { get; set; }
    public Actor Actor { get; set; } = default!;

    public required string PasswordHash { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
