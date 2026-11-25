using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Rules;

public class RulesEngineTests
{
    private static readonly RuleOptions Options = new() { MinimumStatisticsWindow = TimeSpan.Zero, MinimumMissingIndexSeeks = 1 };

    [Fact]
    public void ForeignKeyIndexIsAbsorbedByWiderMissingIndexProposal()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var findings = RulesEngine.Default.Evaluate(snapshot, Options);

        var missing = Assert.Single(findings, finding => finding.RuleId == "missing-index");
        var foreignKey = Assert.Single(findings, finding => finding.RuleId == "unindexed-foreign-key");
        Assert.Equal("IX_Orders_CustomerId_OrderDate", missing.ProposedChange?.IndexName);
        Assert.Null(foreignKey.ProposedChange);
        Assert.Contains(new Evidence("resolvedBy", "IX_Orders_CustomerId_OrderDate"), foreignKey.Evidence);
        Assert.EndsWith("Covered by the missing-index proposal for IX_Orders_CustomerId_OrderDate.", foreignKey.Summary);
    }

    [Fact]
    public void KeepsNarrowerProposalWhenWiderOneLacksItsIncludedColumns()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], includes: ["TotalAmount"])
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"])
            .Build();

        var findings = RulesEngine.Default.Evaluate(snapshot, Options);

        Assert.Equal(2, findings.Count(finding => finding.ProposedChange is not null));
    }

    [Fact]
    public void MergesIdenticalDropProposalsFromDifferentRules()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"], reads: 5, writes: 50_000)
            .WithIndex("Customers", "IX_Customers_EmailLookup", keys: ["Email"], reads: 0, writes: 50_000)
            .Build();

        var findings = RulesEngine.Default.Evaluate(snapshot, Options);

        var drop = Assert.Single(findings, finding => finding.ProposedChange is not null);
        Assert.Equal(new DropIndex(Dbo("Customers"), "IX_Customers_EmailLookup"), drop.ProposedChange);
        Assert.Equal("duplicate-index", drop.RuleId);
        var merged = Assert.Single(findings, finding => finding.RuleId == "unused-index");
        Assert.Contains(new Evidence("resolvedBy", "IX_Customers_EmailLookup"), merged.Evidence);
    }

    [Fact]
    public void OrdersFindingsBySeverityThenTableThenRule()
    {
        var snapshot = Snapshot()
            .WithWindow(TimeSpan.FromHours(1))
            .WithIndex("Orders", "IX_Orders_Status", keys: ["Status"], writes: 500_000)
            .WithForeignKey("FK_OrderItems_Products", "OrderItems", ["ProductId"], "Products")
            .WithMissingIndex("Orders", equality: ["CustomerId"], impactPercent: 96)
            .Build();

        var findings = RulesEngine.Default.Evaluate(snapshot, new RuleOptions { MinimumMissingIndexSeeks = 1 });

        Assert.Equal([Severity.High, Severity.Low, Severity.Info], findings.Select(finding => finding.Severity));
    }
}
