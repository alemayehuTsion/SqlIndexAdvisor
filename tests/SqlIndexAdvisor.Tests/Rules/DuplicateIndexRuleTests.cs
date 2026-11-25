using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Rules;

public class DuplicateIndexRuleTests
{
    private static readonly DuplicateIndexRule Rule = new();
    private static readonly RuleOptions Options = new();

    [Fact]
    public void ProposesDroppingAlphabeticallyLaterIndexWhenReadsAreEqual()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"], writes: 50_000)
            .WithIndex("Customers", "IX_Customers_EmailLookup", keys: ["Email"], writes: 50_000)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Equal(new DropIndex(Dbo("Customers"), "IX_Customers_EmailLookup"), finding.ProposedChange);
        Assert.Contains(new Evidence("duplicateOf", "IX_Customers_Email"), finding.Evidence);
    }

    [Fact]
    public void ProposesDroppingTheIndexWithFewerReads()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"], reads: 0)
            .WithIndex("Customers", "IX_Customers_EmailLookup", keys: ["Email"], reads: 40)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(new DropIndex(Dbo("Customers"), "IX_Customers_Email"), finding.ProposedChange);
    }

    [Fact]
    public void KeepsUniqueIndexEvenWhenItHasFewerReads()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "UQ_Customers_Email", keys: ["Email"], reads: 0, isUnique: true)
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"], reads: 40)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(new DropIndex(Dbo("Customers"), "IX_Customers_Email"), finding.ProposedChange);
    }

    [Fact]
    public void ProposesDroppingNonclusteredIndexThatRepeatsTheClusteredKey()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "PK_Orders", keys: ["OrderId"], isClustered: true, isPrimaryKey: true)
            .WithIndex("Orders", "IX_Orders_OrderId", keys: ["OrderId"])
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(new DropIndex(Dbo("Orders"), "IX_Orders_OrderId"), finding.ProposedChange);
    }

    [Fact]
    public void ReportsInfoWithoutProposalWhenBothDuplicatesEnforceUniqueness()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "UQ_Customers_Email", keys: ["Email"], isUnique: true)
            .WithIndex("Customers", "UQ_Customers_EmailCopy", keys: ["Email"], isUnique: true)
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.ProposedChange);
    }

    [Fact]
    public void TreatsIncludedColumnsInAnyOrderAsDuplicates()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_A", keys: ["CustomerId"], includes: ["Status", "OrderDate"])
            .WithIndex("Orders", "IX_B", keys: ["CustomerId"], includes: ["OrderDate", "Status"])
            .Build();

        Assert.Single(Rule.Evaluate(snapshot, Options));
    }

    [Theory]
    [InlineData(new[] { "Email" }, new[] { "Email DESC" })]
    [InlineData(new[] { "LastName", "FirstName" }, new[] { "FirstName", "LastName" })]
    [InlineData(new[] { "Email" }, new[] { "Email", "LastName" })]
    public void IgnoresIndexesWhoseKeysDifferInDirectionOrderOrLength(string[] firstKeys, string[] secondKeys)
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_A", keys: firstKeys)
            .WithIndex("Customers", "IX_B", keys: secondKeys)
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void IgnoresIndexesWithDifferentIncludedColumns()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_A", keys: ["CustomerId"], includes: ["Status"])
            .WithIndex("Orders", "IX_B", keys: ["CustomerId"], includes: ["OrderDate"])
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void FullIndexIsNotADuplicateOfAFilteredIndexWithTheSameKeys()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"], reads: 0)
            .WithIndex("Orders", "IX_Orders_CustomerId_Open", keys: ["CustomerId"], reads: 50, filter: "([Status]='Pending')")
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void FilteredIndexesWithTheSameFilterAreDuplicates()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_A", keys: ["CustomerId"], filter: "([Status]='Pending')")
            .WithIndex("Orders", "IX_B", keys: ["CustomerId"], filter: "([Status]='Pending')")
            .WithIndex("Orders", "IX_C", keys: ["CustomerId"], filter: "([Status]='Paid')")
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(new DropIndex(Dbo("Orders"), "IX_B"), finding.ProposedChange);
    }

    [Fact]
    public void IgnoresDisabledIndexes()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"], reads: 40)
            .WithIndex("Customers", "IX_Customers_EmailOld", keys: ["Email"], isDisabled: true)
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void IgnoresIdenticalKeysOnDifferentTables()
    {
        var snapshot = Snapshot()
            .WithIndex("Customers", "IX_Customers_Email", keys: ["Email"])
            .WithIndex("Suppliers", "IX_Suppliers_Email", keys: ["Email"])
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }
}
