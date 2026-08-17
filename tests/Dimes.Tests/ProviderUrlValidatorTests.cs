using Dimes.Api;
using Dimes.Api.Services;
using Dimes.Domain;

namespace Dimes.Tests;

/// <summary>SSRF guard on the LLM provider BaseUrl. The stored URL becomes an outbound request target
/// driven by an agent comment whose response is echoed back as a comment, so a malicious BaseUrl is an
/// exfiltration vector — and the adapter attaches the config's credential to that request. These cases use
/// IP literals / scheme / host checks only, so they need no DNS.</summary>
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
        // Does not throw. OpenAI-compatible is the permissive type: local runners are the point of it.
        await ProviderUrlValidator.ValidateAsync(LlmProviderType.OpenAICompatible, baseUrl);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS/GCP/Azure instance metadata
    [InlineData("http://169.254.170.2/v2/credentials")]      // ECS task-role credentials
    [InlineData("http://[fe80::1]/v1")]                      // IPv6 link-local
    public async Task ValidateAsync_RejectsLinkLocalMetadataTargets(string baseUrl)
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => ProviderUrlValidator.ValidateAsync(LlmProviderType.OpenAICompatible, baseUrl));
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://169.254.169.254/")]
    [InlineData("not-a-url")]
    [InlineData("//api.openai.com/v1")] // not absolute
    public async Task ValidateAsync_RejectsNonHttpSchemes(string baseUrl)
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => ProviderUrlValidator.ValidateAsync(LlmProviderType.OpenAICompatible, baseUrl));
    }

    /// <summary>The vendor-hosted types allow an override only within the vendor's domain. Without this a
    /// provider-config admin could point a key-bearing Anthropic/Gemini request at any host and collect the
    /// resolved credential — the local-runner exemption must not extend to types that always send a key.</summary>
    [Theory]
    [InlineData(LlmProviderType.Anthropic, "https://api.anthropic.com")]
    [InlineData(LlmProviderType.Anthropic, "https://gateway.anthropic.com/v1")]
    [InlineData(LlmProviderType.Gemini, "https://generativelanguage.googleapis.com")]
    [InlineData(LlmProviderType.GeminiVertex, "https://us-central1-aiplatform.googleapis.com")]
    [InlineData(LlmProviderType.GeminiVertex, "https://aiplatform.googleapis.com")]
    public async Task ValidateAsync_AllowsVendorHostOverrides(LlmProviderType type, string baseUrl)
    {
        await ProviderUrlValidator.ValidateAsync(type, baseUrl);
    }

    [Theory]
    [InlineData(LlmProviderType.Anthropic, "https://attacker.example.com/v1")]
    [InlineData(LlmProviderType.Anthropic, "http://127.0.0.1:8080")]     // permitted for OpenAI-compatible, not here
    [InlineData(LlmProviderType.Gemini, "https://attacker.example.com")]
    [InlineData(LlmProviderType.GeminiVertex, "https://attacker.example.com")]
    // Suffix matching must be on label boundaries — a bare EndsWith would accept these look-alikes.
    [InlineData(LlmProviderType.Gemini, "https://evilgoogleapis.com")]
    [InlineData(LlmProviderType.Anthropic, "https://notanthropic.com")]
    public async Task ValidateAsync_RejectsOffVendorHostsForVendorTypes(LlmProviderType type, string baseUrl)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => ProviderUrlValidator.ValidateAsync(type, baseUrl));
    }
}
