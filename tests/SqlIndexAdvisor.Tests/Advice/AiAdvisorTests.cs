using System.Text.Json;

using Microsoft.Extensions.AI;

using SqlIndexAdvisor.Core.Advice;
using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Advice;

public class AiAdvisorTests
{
    private static readonly Finding Missing =
        new("missing-index", Severity.High, Dbo("Orders"), "CustomerId", "Orders are scanned.", [],
            new CreateIndex(Dbo("Orders"), "IX_Orders_CustomerId", [new IndexKeyColumn("CustomerId", false)], []));

    private static readonly Finding Unused =
        new("unused-index", Severity.Medium, Dbo("Orders"), "IX_Orders_Status", "Never read.", [],
            new DropIndex(Dbo("Orders"), "IX_Orders_Status"));

    private static readonly IReadOnlyList<Finding> Findings = [Missing, Unused];

    private static readonly IReadOnlyList<ProposalValidation> Validations =
    [
        new(Missing, []),
        new(Unused, [ValidationIssue.Warning("drop-unindexes-foreign-key", "No index would support FK_Orders_Customers.")]),
    ];

    [Fact]
    public async Task UsesTheModelRankingWhenTheReplyIsValid()
    {
        var client = FakeChatClient.Replying(Reply((Unused.Id, 1), (Missing.Id, 2)), modelId: "claude-test");

        var result = await new AiAdvisor(client).AdviseAsync(Findings, Validations, TestContext.Current.CancellationToken);

        Assert.Equal(AdviceSource.Model, result.Source);
        Assert.Equal("claude-test", result.ModelId);
        Assert.Equal([Unused.Id, Missing.Id], result.Assessments.Select(assessment => assessment.FindingId));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task SendsSystemInstructionsAndFindingsAndAsksForJson()
    {
        var client = FakeChatClient.Replying(Reply((Missing.Id, 1), (Unused.Id, 2)));

        await new AiAdvisor(client).AdviseAsync(Findings, Validations, TestContext.Current.CancellationToken);

        Assert.Equal([ChatRole.System, ChatRole.User], client.ReceivedMessages.Select(message => message.Role));
        Assert.Equal(AdvisorPrompt.SystemInstructions, client.ReceivedMessages[0].Text);
        Assert.Contains(Unused.Id, client.ReceivedMessages[1].Text);
        Assert.Contains("drop-unindexes-foreign-key", client.ReceivedMessages[1].Text);
        Assert.Same(ChatResponseFormat.Json, client.ReceivedOptions?.ResponseFormat);
    }

    [Fact]
    public async Task FallsBackToDeterministicRankingWhenTheReplyIsRejected()
    {
        var client = FakeChatClient.Replying(Reply((Missing.Id, 1), ("invented:dbo.Orders:X", 2)));

        var result = await new AiAdvisor(client).AdviseAsync(Findings, Validations, TestContext.Current.CancellationToken);

        Assert.Equal(AdviceSource.Deterministic, result.Source);
        Assert.Null(result.ModelId);
        Assert.Equal(["unknown-finding", "missing-finding"], result.Issues.Select(issue => issue.Code));
        Assert.Equal([Missing.Id, Unused.Id], result.Assessments.Select(assessment => assessment.FindingId));
    }

    [Fact]
    public async Task FallsBackWhenTheProviderThrows()
    {
        var client = FakeChatClient.Throwing(new HttpRequestException("401 Unauthorized"));

        var result = await new AiAdvisor(client).AdviseAsync(Findings, Validations, TestContext.Current.CancellationToken);

        Assert.Equal(AdviceSource.Deterministic, result.Source);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("call-failed", issue.Code);
        Assert.Contains("401 Unauthorized", issue.Message);
    }

    [Fact]
    public async Task FallsBackWhenTheModelDoesNotAnswerInTime()
    {
        var advisor = new AiAdvisor(FakeChatClient.Hanging(), timeout: TimeSpan.FromMilliseconds(50));

        var result = await advisor.AdviseAsync(Findings, Validations, TestContext.Current.CancellationToken);

        Assert.Equal(AdviceSource.Deterministic, result.Source);
        Assert.Equal("timeout", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task LetsTheCallersCancellationThroughInsteadOfFallingBack()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var advisor = new AiAdvisor(FakeChatClient.Hanging(), timeout: TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => advisor.AdviseAsync(Findings, Validations, cancellation.Token));
    }

    [Fact]
    public async Task DoesNotCallTheModelWithoutFindings()
    {
        var client = FakeChatClient.Replying("{}");

        var result = await new AiAdvisor(client).AdviseAsync([], [], TestContext.Current.CancellationToken);

        Assert.Equal(0, client.Calls);
        Assert.Empty(result.Assessments);
    }

    [Fact]
    public void DeterministicAdviceKeepsEngineOrderAndUsesValidationIssuesAsRisks()
    {
        var result = DeterministicAdvice.For(Findings, Validations, reasons: []);

        Assert.Equal(
            [
                new Assessment(Missing.Id, 1, "Orders are scanned.", ""),
                new Assessment(Unused.Id, 2, "Never read.", "Warning: No index would support FK_Orders_Customers."),
            ],
            result.Assessments);
    }

    private static string Reply(params (string Id, int Rank)[] assessments) =>
        JsonSerializer.Serialize(new
        {
            assessments = assessments.Select(a => new { findingId = a.Id, rank = a.Rank, explanation = "Because.", risks = "" }),
        });
}
