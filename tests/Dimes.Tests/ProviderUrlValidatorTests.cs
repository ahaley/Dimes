using Dimes.Api;
using Dimes.Api.Services;

namespace Dimes.Tests;

/// <summary>SSRF guard on the LLM provider BaseUrl. The stored URL becomes an outbound request target
/// driven by an agent comment whose response is echoed back as a comment, so a malicious BaseUrl is an
/// exfiltration vector. These cases use IP literals / scheme checks only, so they need no DNS.</summary>
public class ProviderUrlValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://127.0.0.1:11434")]   // local runner (Ollama) by loopback IP — allowed
    [InlineData("http://10.0.0.5:8000/v1")]  // private-LAN model server — allowed
    [InlineData("http://[fd00::1]/v1")]      // IPv6 unique-local (private) — allowed
    public async Task ValidateAsync_AllowsSafeOrLocalTargets(string? baseUrl)
    {
        // Does not throw.
        await ProviderUrlValidator.ValidateAsync(baseUrl);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS/GCP/Azure instance metadata
    [InlineData("http://169.254.170.2/v2/credentials")]      // ECS task-role credentials
    [InlineData("http://[fe80::1]/v1")]                      // IPv6 link-local
    public async Task ValidateAsync_RejectsLinkLocalMetadataTargets(string baseUrl)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => ProviderUrlValidator.ValidateAsync(baseUrl));
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://169.254.169.254/")]
    [InlineData("not-a-url")]
    [InlineData("//api.openai.com/v1")] // not absolute
    public async Task ValidateAsync_RejectsNonHttpSchemes(string baseUrl)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => ProviderUrlValidator.ValidateAsync(baseUrl));
    }
}
