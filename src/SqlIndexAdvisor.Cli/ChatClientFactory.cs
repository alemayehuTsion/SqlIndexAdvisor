using System.ClientModel;

using Anthropic;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

using OllamaSharp;

using OpenAI;

namespace SqlIndexAdvisor.Cli;

internal static class ChatClientFactory
{
    public const string Providers = "None, Anthropic, OpenAI, AzureOpenAI or Ollama";
    public const string DefaultAnthropicModel = "claude-opus-5-5";
    public const string DefaultOllamaEndpoint = "http://localhost:11434";

    public static IChatClient? Create(string provider, IConfiguration configuration) =>
        provider.ToLowerInvariant() switch
        {
            "none" => null,
            "anthropic" => CreateAnthropic(configuration),
            "openai" => CreateOpenAiCompatible(configuration, "Ai:OpenAI", endpoint: configuration["Ai:OpenAI:Endpoint"]),
            "azureopenai" => CreateOpenAiCompatible(configuration, "Ai:AzureOpenAI", endpoint: Required(configuration, "Ai:AzureOpenAI:Endpoint")),
            "ollama" => new OllamaApiClient(
                new Uri(configuration["Ai:Ollama:Endpoint"] ?? DefaultOllamaEndpoint), Required(configuration, "Ai:Model")),
            _ => throw new ArgumentException($"Unknown AI provider '{provider}'. Use {Providers}."),
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

    // Azure OpenAI's v1 API is OpenAI-compatible, so both providers share the OpenAI client; for Azure the
    // endpoint is https://<resource>.openai.azure.com/openai/v1/ and the model is the deployment name.
    private static IChatClient CreateOpenAiCompatible(IConfiguration configuration, string section, string? endpoint)
    {
        var options = endpoint is null ? new OpenAIClientOptions() : new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(new ApiKeyCredential(Required(configuration, $"{section}:ApiKey")), options);

        return client.GetChatClient(Required(configuration, "Ai:Model")).AsIChatClient();
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"'{key}' is not configured. Set it with 'dotnet user-secrets set {key} <value>' " +
                $"or the environment variable SQLINDEXADVISOR_{key.Replace(":", "__")}.");
}
