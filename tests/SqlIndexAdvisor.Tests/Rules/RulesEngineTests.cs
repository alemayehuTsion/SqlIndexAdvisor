using SqlIndexAdvisor.Core.Collection;
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
    public void BuildsReadableIdsFromRuleTableAndSubject()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Orders_Status", keys: ["Status"], writes: 500_000)
            .WithForeignKey("FK_OrderItems_Products", "OrderItems", ["ProductId"], "Products")
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"], includes: ["Status"])
            .Build();

        var ids = RulesEngine.Default.Evaluate(snapshot, Options).Select(finding => finding.Id).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "missing-index:dbo.Orders:CustomerId,OrderDate+Status",
                "unindexed-foreign-key:dbo.OrderItems:FK_OrderItems_Products",
                "unused-index:dbo.Orders:IX_Orders_Status",
            ],
            ids);
    }

    [Fact]
    public void SuffixesClashingIdsSoEveryFindingInARunIsAddressable()
    {
        var finding = new Finding("faulty-rule", Severity.Low, Dbo("Orders"), "Same", "Twice.", [], ProposedChange: null);
        var engine = new RulesEngine([new FixedRule([finding, finding, finding])]);

        var ids = engine.Evaluate(Snapshot().Build(), Options).Select(result => result.Id);

        Assert.Equal(
            ["faulty-rule:dbo.Orders:Same", "faulty-rule:dbo.Orders:Same#2", "faulty-rule:dbo.Orders:Same#3"],
            ids);
    }

    [Fact]
    public void KeepsProposalsWhoseKeysAreTheSameColumnsInADifferentOrder()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"])
            .WithMissingIndex("Orders", equality: ["OrderDate"], inequality: ["CustomerId"])
            .Build();

        var proposals = RulesEngine.Default.Evaluate(snapshot, Options)
            .Select(finding => finding.ProposedChange)
            .OfType<CreateIndex>()
            .Select(create => string.Join(",", create.KeyColumns.Select(key => key.Name)))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["CustomerId,OrderDate", "OrderDate,CustomerId"], proposals);
    }

    [Fact]
    public void NeverEmitsTheSameIdTwiceEvenWhenASubjectLooksLikeAGeneratedSuffix()
    {
        static Finding Make(string subject) => new("faulty-rule", Severity.Low, Dbo("Orders"), subject, "Clash.", [], ProposedChange: null);
        var engine = new RulesEngine([new FixedRule([Make("CustomerId"), Make("CustomerId"), Make("CustomerId#2")])]);

        var ids = engine.Evaluate(Snapshot().Build(), Options).Select(result => result.Id).ToList();

        Assert.Equal(3, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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

    private sealed class FixedRule(IReadOnlyList<Finding> findings) : IIndexRule
    {
        public string Id => "faulty-rule";

        public IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options) => findings;
    }
}
