using System.Text.Json;

using SqlIndexAdvisor.Core.Advice;
using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Advice;

public class AdvisorPromptTests
{
    [Fact]
    public void UserMessageCarriesIdsEvidenceAndProposalsAsJson()
    {
        Finding[] findings =
        [
            new("missing-index", Severity.High, Dbo("Orders"), "CustomerId,OrderDate", "Orders are scanned.",
                [Evidence.From("userSeeks", 120L)],
                new CreateIndex(Dbo("Orders"), "IX_Orders_CustomerId_OrderDate",
                    [new IndexKeyColumn("CustomerId", false), new IndexKeyColumn("OrderDate", true)], ["Status"])),
            new("unused-index", Severity.Info, Dbo("Orders"), "IX_Orders_Status", "Too little history.", [], ProposedChange: null),
        ];

        using var message = JsonDocument.Parse(AdvisorPrompt.BuildUserMessage(findings, validations: []));
        var items = message.RootElement.GetProperty("findings").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal("missing-index:dbo.Orders:CustomerId,OrderDate", items[0].GetProperty("id").GetString());
        Assert.Equal("High", items[0].GetProperty("severity").GetString());
        Assert.Equal("120", items[0].GetProperty("evidence").GetProperty("userSeeks").GetString());
        Assert.Equal(
            "create nonclustered index IX_Orders_CustomerId_OrderDate on [dbo].[Orders] (CustomerId, OrderDate DESC) include (Status)",
            items[0].GetProperty("proposal").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("proposal").ValueKind);
    }

    [Fact]
    public void UserMessageCarriesValidationResultsSoTheModelKnowsRejectionsAndRisks()
    {
        var drop = new Finding("unused-index", Severity.Medium, Dbo("Orders"), "IX_Orders_CustomerId", "Never read.", [],
            new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId"));
        var create = new Finding("missing-index", Severity.High, Dbo("Orders"), "Notes", "Wide key.", [],
            new CreateIndex(Dbo("Orders"), "IX_Orders_Notes", [new IndexKeyColumn("Notes", false)], []));
        ProposalValidation[] validations =
        [
            new(drop, [ValidationIssue.Warning("drop-unindexes-foreign-key", "No index would support FK_Orders_Customers.")]),
            new(create, [ValidationIssue.Error("key-type-not-allowed", "Column Notes (nvarchar(max)) cannot be an index key.")]),
        ];

        using var message = JsonDocument.Parse(AdvisorPrompt.BuildUserMessage([drop, create], validations));
        var items = message.RootElement.GetProperty("findings").EnumerateArray().ToList();

        var warned = items[0].GetProperty("validation");
        Assert.True(warned.GetProperty("accepted").GetBoolean());
        Assert.Equal("drop-unindexes-foreign-key", warned.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.Equal("Warning", warned.GetProperty("issues")[0].GetProperty("level").GetString());

        var rejected = items[1].GetProperty("validation");
        Assert.False(rejected.GetProperty("accepted").GetBoolean());
        Assert.Equal("key-type-not-allowed", rejected.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void FindingWithoutProposalHasNoValidation()
    {
        var info = new Finding("unused-index", Severity.Info, Dbo("Orders"), "IX_Orders_Status", "Too little history.", [], null);

        using var message = JsonDocument.Parse(AdvisorPrompt.BuildUserMessage([info], validations: []));

        Assert.Equal(JsonValueKind.Null, message.RootElement.GetProperty("findings")[0].GetProperty("validation").ValueKind);
    }

    [Fact]
    public void SystemInstructionsStateTheRulesTheParserEnforces()
    {
        Assert.Contains("cannot add, remove or alter", AdvisorPrompt.SystemInstructions);
        Assert.Contains("\"assessments\"", AdvisorPrompt.SystemInstructions);
        Assert.Contains($"under {AdvisorResponseParser.MaxTextLength} characters", AdvisorPrompt.SystemInstructions);
        Assert.Contains("rejected", AdvisorPrompt.SystemInstructions);
    }
}
