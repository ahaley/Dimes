namespace Dimes.Domain.Entities;

/// <summary>Site-wide branding/configuration. A single row by convention — the API reads/writes the one
/// row. Currently holds the customizable site title shown in the brand wordmark, the login screen and
/// the browser tab. Defaults to "Dimes".</summary>
public class SiteSettings : Entity
{
    public const string DefaultTitle = "Dimes";

    public string Title { get; set; } = DefaultTitle;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
