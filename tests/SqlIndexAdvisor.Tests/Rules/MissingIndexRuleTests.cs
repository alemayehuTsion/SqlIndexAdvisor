using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Rules;

public class MissingIndexRuleTests
{
    private static readonly MissingIndexRule Rule = new();
    private static readonly RuleOptions Options = new() { MinimumMissingIndexSeeks = 10 };

    [Fact]
    public void ProposesIndexWithEqualityColumnsBeforeInequalityColumns()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"], seeks: 120, impactPercent: 96)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.High, finding.Severity);
        var change = Assert.IsType<CreateIndex>(finding.ProposedChange);
        Assert.Equal("IX_Orders_CustomerId_OrderDate", change.IndexName);
        Assert.Equal(["CustomerId", "OrderDate"], change.KeyColumns.Select(key => key.Name));
        Assert.Equal(
            "SQL Server recorded 120 seeks that would use an index on [dbo].[Orders] (CustomerId, OrderDate), " +
            "with an estimated 96% lower query cost.",
            finding.Summary);
    }

    [Fact]
    public void CarriesIncludedColumnsAndRatesModestImpactAsMedium()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], includes: ["Status"], impactPercent: 30)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Equal(["Status"], Assert.IsType<CreateIndex>(finding.ProposedChange).IncludedColumns);
    }

    [Fact]
    public void ReportsInfoWithoutProposalBelowMinimumSeeks()
    {
        var snapshot = Snapshot()
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"], seeks: 1)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.ProposedChange);
        Assert.Contains(new Evidence("minimumSeeks", "10"), finding.Evidence);
    }

    [Theory]
    [InlineData(new[] { "CustomerId", "OrderDate" }, true)]
    [InlineData(new[] { "CustomerId", "OrderDate", "Status" }, true)]
    [InlineData(new[] { "CustomerId" }, false)]
    [InlineData(new[] { "OrderDate", "CustomerId" }, false)]
    [InlineData(new[] { "CustomerId", "Status", "OrderDate" }, false)]
    public void SkipsSuggestionThatAnExistingIndexAlreadyServes(string[] existingKeys, bool covered)
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Existing", keys: existingKeys)
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"])
            .Build();

        Assert.Equal(covered, !Rule.Evaluate(snapshot, Options).Any());
    }

    [Fact]
    public void TreatsEqualityColumnsAsUnordered()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Existing", keys: ["Status", "CustomerId"])
            .WithMissingIndex("Orders", equality: ["CustomerId", "Status"])
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void IgnoresIndexesOnOtherTables()
    {
        var snapshot = Snapshot()
            .WithIndex("Invoices", "IX_Invoices_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"])
            .WithMissingIndex("Orders", equality: ["CustomerId"], inequality: ["OrderDate"])
            .Build();

        Assert.Single(Rule.Evaluate(snapshot, Options));
    }
}
