namespace Dimes.Domain.Providers;

/// <summary>Which namespace a reference resolves in. Stated explicitly at every call site rather than
/// defaulted, because the default would be the dangerous one.
///
/// **This is a privilege boundary, not tidiness.** A reference name reaches the resolver from one of two
/// very different places. <see cref="Operator"/> names come from configuration the operator controls
/// (<c>Auth:Oidc:ClientSecretRef</c>); the rest are typed into a form by a project Maintainer and stored
/// on a DB row. When both resolved in one flat namespace, "can configure an LLM provider" implied "can
/// read every secret the host process can see" — a Maintainer could put the OIDC client secret's
/// reference name on a provider config pointed at a host they control, and the adapter would send it
/// there as a bearer token. Those are different trust levels, so they now get different namespaces:
/// a Maintainer-supplied name can only ever address its own section.</summary>
public enum SecretPurpose
{
    /// <summary>Named by the operator in configuration, never by a user. Resolves against the unprefixed
    /// routes — the whole secret store is legitimately in reach, and this is also what keeps existing
    /// deployments' auth working unchanged.</summary>
    Operator,

    /// <summary>An LLM provider config's credential reference. Maintainer-supplied.</summary>
    LlmProvider,

    /// <summary>A notification channel's credential reference. Maintainer-supplied.</summary>
    Notification,

    /// <summary>An SCM provider config's token reference. Maintainer-supplied.</summary>
    Scm,
}

/// <summary>Resolves a secret reference (stored on provider configs) to its actual value. Keeps API
/// keys / tokens out of the database — the DB holds only the reference.
///
/// The returned value is always the secret's <em>contents</em>, never a path: an implementation backed by
/// files (see <c>ConfigurationSecretResolver</c>) reads them and returns what it read, so callers never
/// touch the filesystem. Implementations throw rather than returning null when a reference is configured
/// but unreadable, so a broken mount can't masquerade as "not configured".
///
/// <paramref name="purpose"/> selects the namespace the name is looked up in — see
/// <see cref="SecretPurpose"/>. There is deliberately no single-argument overload: a caller that omitted
/// the purpose would silently get the unrestricted one, which is exactly the bug this parameter exists
/// to prevent.</summary>
public interface ISecretResolver
{
    string? Resolve(SecretPurpose purpose, string? secretRef);
}
