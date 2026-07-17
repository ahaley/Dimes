using Dimes.Domain.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Dimes.Infrastructure.Providers;

public static class ProviderRegistration
{
    /// <summary>Register the LLM/SCM provider adapters (each with its own HttpClient) and the secret
    /// resolver. Adapters are exposed as <see cref="ILlmProvider"/>/<see cref="IScmProvider"/> sets so
    /// callers can select one by provider type.</summary>
    public static IServiceCollection AddDimesProviders(this IServiceCollection services)
    {
        services.AddSingleton<ISecretResolver, ConfigurationSecretResolver>();

        services.AddHttpClient<AnthropicLlmProvider>();
        services.AddHttpClient<OpenAiCompatibleLlmProvider>();
        services.AddTransient<ILlmProvider>(sp => sp.GetRequiredService<AnthropicLlmProvider>());
        services.AddTransient<ILlmProvider>(sp => sp.GetRequiredService<OpenAiCompatibleLlmProvider>());

        services.AddHttpClient<GitHubScmProvider>();
        services.AddTransient<IScmProvider>(sp => sp.GetRequiredService<GitHubScmProvider>());

        services.AddHttpClient<GoogleChatNotificationProvider>();
        services.AddTransient<INotificationChannelProvider>(
            sp => sp.GetRequiredService<GoogleChatNotificationProvider>());

        return services;
    }
}
