using Microsoft.Extensions.AI;

using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

namespace SqlIndexAdvisor.Core.Advice;

public sealed class AiAdvisor(IChatClient chatClient, TimeSpan? timeout = null)
{
    public const int MaxOutputTokens = 16_000;

    private static ChatOptions CreateRequestOptions() => new()
    {
        ResponseFormat = ChatResponseFormat.Json,
        MaxOutputTokens = MaxOutputTokens,
    };

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(2);

    public async Task<AdviceResult> AdviseAsync(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<ProposalValidation> validations,
        CancellationToken cancellationToken = default)
    {
        if (findings.Count == 0)
        {
            return DeterministicAdvice.For(findings, validations, reasons: []);
        }

        ChatMessage[] messages =
        [
            new(ChatRole.System, AdvisorPrompt.SystemInstructions),
            new(ChatRole.User, AdvisorPrompt.BuildUserMessage(findings, validations)),
        ];

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(messages, CreateRequestOptions(), timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fallback("timeout", $"The model did not answer within {_timeout.TotalSeconds:0} seconds.");
        }
        // Any provider failure (network, authentication, rate limit, refusal surfaced as an error) must end in the
        // deterministic ranking rather than a crash; the reason is kept for the output and the audit log.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fallback("call-failed", $"The model call failed: {exception.GetType().Name}: {exception.Message}");
        }

        var parsed = AdvisorResponseParser.Parse(response.Text, findings);

        return parsed.IsUsable
            ? new AdviceResult(AdviceSource.Model, parsed.Assessments, [], response.ModelId)
            : DeterministicAdvice.For(findings, validations, parsed.Issues);

        AdviceResult Fallback(string code, string message) =>
            DeterministicAdvice.For(findings, validations, [new AdviceIssue(code, message)]);
    }
}
