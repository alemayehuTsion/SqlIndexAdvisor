using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Rules;

public class UnusedIndexRuleTests
{
    private static readonly UnusedIndexRule Rule = new();
    private static readonly RuleOptions Options = new() { MinimumStatisticsWindow = TimeSpan.FromDays(7) };

    [Fact]
    public void ProposesDroppingNonclusteredIndexWithWritesButNoReads()
    {
        var snapshot = Snapshot()
            .WithWindow(TimeSpan.FromDays(8))
            .WithIndex("Orders", "IX_Orders_Status", keys: ["Status"], reads: 0, writes: 500_000)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Equal(new DropIndex(Dbo("Orders"), "IX_Orders_Status"), finding.ProposedChange);
        Assert.Equal("IX_Orders_Status has not been read in 8 days but absorbed 500,000 row writes.", finding.Summary);
        Assert.Contains(new Evidence("reads", "0"), finding.Evidence);
        Assert.Contains(new Evidence("rowWrites", "500000"), finding.Evidence);
    }

    [Fact]
    public void IgnoresIndexThatIsRead()
    {
        var snapshot = Snapshot().WithIndex("Orders", "IX_Orders_Status", reads: 1, writes: 500_000).Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void IgnoresIndexWithoutWrites()
    {
        var snapshot = Snapshot().WithIndex("Orders", "IX_Orders_Status", reads: 0, writes: 0).Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void NeverFlagsPrimaryKeysOrUniqueIndexes()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "PK_Orders", reads: 0, writes: 500_000, isClustered: true, isPrimaryKey: true)
            .WithIndex("Customers", "UQ_Customers_Email", keys: ["Email"], reads: 0, writes: 50_000, isUnique: true)
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void ReportsInfoWithoutProposalWhenStatisticsWindowIsTooShort()
    {
        var snapshot = Snapshot()
            .WithWindow(TimeSpan.FromHours(2.5))
            .WithIndex("Orders", "IX_Orders_Status", reads: 0, writes: 500_000)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.ProposedChange);
        Assert.Contains("only 2.5 hours (minimum 7 days)", finding.Summary);
        Assert.Contains(new Evidence("minimumWindowHours", "168"), finding.Evidence);
    }
}
