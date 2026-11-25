using Anthropic;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace SqlIndexAdvisor.Cli;

internal static class ChatClientFactory
{
    public const string DefaultAnthropicModel = "claude-opus-5-5";

    public static IChatClient? Create(string provider, IConfiguration configuration) =>
        provider.ToLowerInvariant() switch
        {
            "none" => null,
            "anthropic" => CreateAnthropic(configuration),
            _ => throw new ArgumentException($"Unknown AI provider '{provider}'. Use None or Anthropic."),
        };

    public static string ModelFor(string provider, IConfiguration configuration) =>
        configuration["Ai:Model"] ?? (provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? DefaultAnthropicModel : "");

    // Without a configured key the SDK falls back to its own credential lookup (ANTHROPIC_API_KEY or an
    // `ant auth login` profile); if that finds nothing the call fails and the advisor falls back to rules only.
    private static IChatClient CreateAnthropic(IConfiguration configuration)
    {
        var apiKey = configuration["Ai:Anthropic:ApiKey"];
        AnthropicClient client = string.IsNullOrWhiteSpace(apiKey) ? new() : new() { ApiKey = apiKey };

        return client.AsIChatClient(ModelFor("anthropic", configuration));
    }
}
