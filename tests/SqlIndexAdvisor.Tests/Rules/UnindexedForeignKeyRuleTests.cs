using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Rules;

public class UnindexedForeignKeyRuleTests
{
    private static readonly UnindexedForeignKeyRule Rule = new();
    private static readonly RuleOptions Options = new();

    [Fact]
    public void ProposesIndexOnForeignKeyColumnsWhenNoIndexSupportsThem()
    {
        var snapshot = Snapshot()
            .WithIndex("OrderItems", "PK_OrderItems", keys: ["OrderId", "LineNumber"], isClustered: true, isPrimaryKey: true)
            .WithForeignKey("FK_OrderItems_Products", "OrderItems", ["ProductId"], "Products")
            .Build();

        var finding = Assert.Single(Rule.Evaluate(snapshot, Options));

        Assert.Equal(Severity.Low, finding.Severity);
        var change = Assert.IsType<CreateIndex>(finding.ProposedChange);
        Assert.Equal("IX_OrderItems_ProductId", change.IndexName);
        Assert.Equal([new IndexKeyColumn("ProductId", IsDescending: false)], change.KeyColumns);
        Assert.Empty(change.IncludedColumns);
    }

    [Fact]
    public void AcceptsLeadingColumnOfCompositePrimaryKeyAsSupport()
    {
        var snapshot = Snapshot()
            .WithIndex("OrderItems", "PK_OrderItems", keys: ["OrderId", "LineNumber"], isClustered: true, isPrimaryKey: true)
            .WithForeignKey("FK_OrderItems_Orders", "OrderItems", ["OrderId"], "Orders")
            .Build();

        Assert.Empty(Rule.Evaluate(snapshot, Options));
    }

    [Fact]
    public void DoesNotAcceptForeignKeyColumnInSecondPosition()
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Orders_Status_CustomerId", keys: ["Status", "CustomerId"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        Assert.Single(Rule.Evaluate(snapshot, Options));
    }

    [Theory]
    [InlineData(new[] { "TenantId", "CustomerId" }, true)]
    [InlineData(new[] { "CustomerId", "TenantId", "OrderDate" }, true)]
    [InlineData(new[] { "TenantId", "OrderDate", "CustomerId" }, false)]
    [InlineData(new[] { "TenantId" }, false)]
    public void MultiColumnForeignKeyNeedsAllItsColumnsLeadingInAnyOrder(string[] indexKeys, bool supported)
    {
        var snapshot = Snapshot()
            .WithIndex("Orders", "IX_Candidate", keys: indexKeys)
            .WithForeignKey("FK_Orders_Customers", "Orders", ["TenantId", "CustomerId"], "Customers")
            .Build();

        Assert.Equal(supported, !Rule.Evaluate(snapshot, Options).Any());
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "([Quantity]>(10))")]
    public void DisabledOrFilteredIndexDoesNotSupportTheForeignKey(bool isDisabled, string? filter)
    {
        var snapshot = Snapshot()
            .WithIndex("OrderItems", "IX_OrderItems_ProductId", keys: ["ProductId"], isDisabled: isDisabled, filter: filter)
            .WithForeignKey("FK_OrderItems_Products", "OrderItems", ["ProductId"], "Products")
            .Build();

        var change = Assert.IsType<CreateIndex>(Assert.Single(Rule.Evaluate(snapshot, Options)).ProposedChange);

        Assert.Equal("IX_OrderItems_ProductId_2", change.IndexName);
    }

    [Fact]
    public void AppendsSuffixWhenProposedNameIsAlreadyTaken()
    {
        var snapshot = Snapshot()
            .WithIndex("OrderItems", "IX_OrderItems_ProductId", keys: ["Quantity", "ProductId"])
            .WithForeignKey("FK_OrderItems_Products", "OrderItems", ["ProductId"], "Products")
            .Build();

        var change = Assert.IsType<CreateIndex>(Assert.Single(Rule.Evaluate(snapshot, Options)).ProposedChange);

        Assert.Equal("IX_OrderItems_ProductId_2", change.IndexName);
    }
}
