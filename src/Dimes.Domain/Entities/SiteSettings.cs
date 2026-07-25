namespace Dimes.Domain.Entities;

/// <summary>Site-wide branding/configuration. A single row by convention — the API reads/writes the one
/// row. Holds the customizable site title shown in the brand wordmark, the login screen and the browser
/// tab (defaults to "Dimes"), and the instance-wide cap on how many projects a non-admin may create.</summary>
public class SiteSettings : Entity
{
    public const string DefaultTitle = "Dimes";
    public const int DefaultProjectLimit = 3;

    /// <summary>An upper bound on any configured project limit — a sanity rail, not a product rule.</summary>
    public const int MaxProjectLimit = 1000;

    public string Title { get; set; } = DefaultTitle;

    /// <summary>How many projects a non-admin may create, unless their <see cref="Actor.ProjectLimit"/>
    /// overrides it. 0 disables non-admin project creation entirely (the behaviour before quotas existed).
    /// Site admins are exempt.</summary>
    public int ProjectLimit { get; set; } = DefaultProjectLimit;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
