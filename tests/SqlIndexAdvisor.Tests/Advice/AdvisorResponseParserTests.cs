using SqlIndexAdvisor.Core.Advice;
using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Advice;

public class AdvisorResponseParserTests
{
    private static readonly IReadOnlyList<Finding> Findings =
    [
        new("missing-index", Severity.High, Dbo("Orders"), "CustomerId,OrderDate", "Orders are scanned.", [], ProposedChange: null),
        new("unused-index", Severity.Medium, Dbo("Orders"), "IX_Orders_Status", "Never read.", [], new DropIndex(Dbo("Orders"), "IX_Orders_Status")),
    ];

    private const string MissingId = "missing-index:dbo.Orders:CustomerId,OrderDate";
    private const string UnusedId = "unused-index:dbo.Orders:IX_Orders_Status";

    [Fact]
    public void AcceptsCompleteReplyAndOrdersAssessmentsByRank()
    {
        var result = AdvisorResponseParser.Parse(
            Reply((UnusedId, 2, "Costs writes.", "None."), (MissingId, 1, " Saves a scan. ", "")),
            Findings);

        Assert.True(result.IsUsable);
        Assert.Equal(
            [new Assessment(MissingId, 1, "Saves a scan.", ""), new Assessment(UnusedId, 2, "Costs writes.", "None.")],
            result.Assessments);
    }

    [Fact]
    public void AcceptsReplyWrappedInAMarkdownCodeFence()
    {
        var fenced = $"```json\n{Reply((MissingId, 1, "A.", ""), (UnusedId, 2, "B.", ""))}\n```";

        Assert.True(AdvisorResponseParser.Parse(fenced, Findings).IsUsable);
    }

    [Theory]
    [InlineData("not json at all", "invalid-json")]
    [InlineData("{\"assessments\":[]}", "no-assessments")]
    [InlineData("{}", "no-assessments")]
    [InlineData("{\"assessments\":[null]}", "null-assessment")]
    [InlineData("{\"assessments\":[{\"findingId\":\"x\",\"rank\":1,\"explanation\":\"A.\"},null]}", "null-assessment")]
    public void RejectsRepliesWithoutUsableAssessments(string reply, string expectedCode)
    {
        var result = AdvisorResponseParser.Parse(reply, Findings);

        Assert.False(result.IsUsable);
        Assert.Empty(result.Assessments);
        Assert.Equal([expectedCode], Codes(result));
    }

    [Fact]
    public void RejectsInventedFindingAndReportsTheOneLeftOut()
    {
        var result = AdvisorResponseParser.Parse(
            Reply((MissingId, 1, "A.", ""), ("drop-everything:dbo.Orders:PK_Orders", 2, "B.", "")),
            Findings);

        Assert.Equal(["unknown-finding", "missing-finding"], Codes(result));
        Assert.Empty(result.Assessments);
    }

    [Fact]
    public void RejectsFindingAssessedTwice()
    {
        var result = AdvisorResponseParser.Parse(
            Reply((MissingId, 1, "A.", ""), (MissingId, 2, "B.", "")),
            Findings);

        Assert.Equal(["duplicate-finding", "missing-finding"], Codes(result));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(0, 1)]
    public void RejectsRanksThatAreNotOneToNWithoutGaps(int firstRank, int secondRank)
    {
        var result = AdvisorResponseParser.Parse(
            Reply((MissingId, firstRank, "A.", ""), (UnusedId, secondRank, "B.", "")),
            Findings);

        Assert.Equal(["invalid-ranks"], Codes(result));
    }

    [Fact]
    public void RejectsEmptyOrOverlongTexts()
    {
        var result = AdvisorResponseParser.Parse(
            Reply((MissingId, 1, "  ", ""), (UnusedId, 2, "B.", new string('x', AdvisorResponseParser.MaxTextLength + 1))),
            Findings);

        Assert.Equal(["empty-explanation", "text-too-long"], Codes(result));
    }

    [Fact]
    public void MatchesFindingIdsExactly()
    {
        var result = AdvisorResponseParser.Parse(
            Reply((MissingId.ToUpperInvariant(), 1, "A.", ""), (UnusedId, 2, "B.", "")),
            Findings);

        Assert.Contains("unknown-finding", Codes(result));
    }

    private static string Reply(params (string Id, int Rank, string Explanation, string Risks)[] assessments) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            assessments = assessments.Select(a => new { findingId = a.Id, rank = a.Rank, explanation = a.Explanation, risks = a.Risks }),
        });

    private static IEnumerable<string> Codes(AdviceParseResult result) => result.Issues.Select(issue => issue.Code);
}
