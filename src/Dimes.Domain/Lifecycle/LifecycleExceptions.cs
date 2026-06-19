namespace Dimes.Domain.Lifecycle;

/// <summary>Base for all lifecycle-guard failures.</summary>
public abstract class LifecycleException : InvalidOperationException
{
    protected LifecycleException(string message) : base(message) { }
}

/// <summary>The requested transition is not legal from the current status.</summary>
public sealed class InvalidTransitionException : LifecycleException
{
    public InvalidTransitionException(string from, string to)
        : base($"Transition '{from}' -> '{to}' is not allowed.")
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

/// <summary>The actor's role is below what the transition requires (e.g. non-Maintainer
/// attempting the whitelist gate).</summary>
public sealed class InsufficientRoleException : LifecycleException
{
    public InsufficientRoleException(MemberRole required, MemberRole actual, string to)
        : base($"Transition to '{to}' requires role '{required}' but actor has '{actual}'.")
    {
        Required = required;
        Actual = actual;
        To = to;
    }

    public MemberRole Required { get; }
    public MemberRole Actual { get; }
    public string To { get; }
}
