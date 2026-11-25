using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

using SqlIndexAdvisor.Tests.Rules;

using static SqlIndexAdvisor.Tests.Rules.SnapshotBuilder;

namespace SqlIndexAdvisor.Tests.Validation;

public class ProposalValidatorTests
{
    private static readonly ProposalValidator Validator = new();

    private static SnapshotBuilder OrdersTable() =>
        Snapshot()
            .WithColumn("Orders", "OrderId")
            .WithColumn("Orders", "CustomerId")
            .WithColumn("Orders", "OrderDate", type: "datetime2", maxLengthBytes: 6)
            .WithColumn("Orders", "Status", type: "varchar", maxLengthBytes: 20)
            .WithIndex("Orders", "PK_Orders", keys: ["OrderId"], isClustered: true, isPrimaryKey: true)
            .WithIndex("Orders", "IX_Orders_Status", keys: ["Status"]);

    [Fact]
    public void AcceptsValidCreateAndDrop()
    {
        var results = Validate(
            OrdersTable().Build(),
            Create("Orders", "IX_Orders_CustomerId_OrderDate", ["CustomerId", "OrderDate"]),
            new DropIndex(Dbo("Orders"), "IX_Orders_Status"));

        Assert.All(results, result =>
        {
            Assert.True(result.IsAccepted);
            Assert.Empty(result.Issues);
        });
    }

    [Fact]
    public void ValidatesOnlyFindingsThatProposeAChange()
    {
        var info = new Finding("test", Severity.Info, Dbo("Orders"), "Nothing", "Nothing to do.", [], ProposedChange: null);

        Assert.Empty(Validator.Validate(OrdersTable().Build(), [info]));
    }

    [Fact]
    public void RejectsUnknownTableWithASingleIssue()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Invoices", "IX_Invoices_Total", ["Total"])));

        Assert.False(result.IsAccepted);
        Assert.Equal(["table-not-found"], Codes(result));
    }

    [Fact]
    public void RejectsColumnsThatDoNotExist()
    {
        var result = Assert.Single(Validate(
            OrdersTable().Build(),
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId", "Region"], includes: ["Discount"])));

        Assert.Equal(["column-not-found", "column-not-found"], Codes(result));
        Assert.Contains(result.Issues, issue => issue.Message.Contains("Region"));
        Assert.Contains(result.Issues, issue => issue.Message.Contains("Discount"));
    }

    [Fact]
    public void MatchesColumnAndIndexNamesCaseInsensitively()
    {
        var results = Validate(
            OrdersTable().Build(),
            Create("Orders", "IX_Orders_CustomerId", ["customerid"]),
            new DropIndex(Dbo("Orders"), "ix_orders_status"));

        Assert.All(results, result => Assert.True(result.IsAccepted));
    }

    [Fact]
    public void RejectsNameLongerThanSqlServerAllows()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Orders", new string('X', 129), ["CustomerId"])));

        Assert.Equal(["name-too-long"], Codes(result));
    }

    [Fact]
    public void RejectsNameAlreadyUsedOnTheTable()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Orders", "IX_Orders_Status", ["CustomerId"])));

        Assert.Equal(["name-taken"], Codes(result));
    }

    [Fact]
    public void RejectsTwoProposalsCreatingTheSameName()
    {
        var results = Validate(
            OrdersTable().Build(),
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId"]),
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId", "OrderDate"]));

        Assert.All(results, result => Assert.Equal(["name-not-unique"], Codes(result)));
    }

    [Fact]
    public void RejectsCreateIdenticalToAnExistingIndexUnderAnotherName()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Orders", "IX_Orders_Status_Copy", ["Status"])));

        Assert.False(result.IsAccepted);
        Assert.Equal(["index-exists"], Codes(result));
    }

    [Fact]
    public void AcceptsSameColumnsInADifferentKeyOrder()
    {
        var snapshot = OrdersTable().WithIndex("Orders", "IX_Orders_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"]).Build();

        var result = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_OrderDate_CustomerId", ["OrderDate", "CustomerId"])));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void WarnsButAcceptsWhenAWiderExistingIndexServesTheSameQueries()
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"], includes: ["Status"])
            .Build();

        var result = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_CustomerId", ["CustomerId"], includes: ["Status"])));

        Assert.True(result.IsAccepted);
        Assert.Equal(["redundant-with-existing"], Codes(result));
    }

    [Fact]
    public void WarnsWhenTheClusteredIndexAlreadyLeadsWithTheSameKey()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Orders", "IX_Orders_OrderId", ["OrderId"], includes: ["Status"])));

        Assert.True(result.IsAccepted);
        Assert.Equal(["redundant-with-existing"], Codes(result));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "([Status]='Pending')")]
    public void DisabledOrFilteredExistingIndexNeitherBlocksNorMakesTheNewIndexRedundant(bool isDisabled, string? filter)
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId_Old", keys: ["CustomerId"], isDisabled: isDisabled, filter: filter)
            .Build();

        var result = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_CustomerId", ["CustomerId"])));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DoesNotWarnWhenTheWiderIndexIsDroppedInTheSameBatch()
    {
        var snapshot = OrdersTable().WithIndex("Orders", "IX_Orders_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"]).Build();

        var results = Validate(
            snapshot,
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId"]),
            new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId_OrderDate"));

        Assert.All(results, result => Assert.Empty(result.Issues));
    }

    [Fact]
    public void RejectsDropOfIndexThatDoesNotExist()
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), new DropIndex(Dbo("Orders"), "IX_Orders_Gone")));

        Assert.Equal(["index-not-found"], Codes(result));
    }

    [Theory]
    [InlineData(new[] { "CustomerId", "CustomerId" }, new string[0])]
    [InlineData(new[] { "CustomerId" }, new[] { "OrderDate", "orderdate" })]
    [InlineData(new[] { "CustomerId", "OrderDate" }, new[] { "OrderDate" })]
    public void RejectsColumnUsedTwice(string[] keys, string[] includes)
    {
        var result = Assert.Single(Validate(OrdersTable().Build(), Create("Orders", "IX_Orders_Test", keys, includes)));

        Assert.Equal(["column-repeated"], Codes(result));
    }

    [Theory]
    [InlineData("nvarchar", -1)]
    [InlineData("varbinary", -1)]
    [InlineData("xml", -1)]
    [InlineData("text", 16)]
    [InlineData("geography", -1)]
    public void RejectsLargeObjectTypesAsKeyColumns(string type, int maxLengthBytes)
    {
        var snapshot = OrdersTable().WithColumn("Orders", "Notes", type, maxLengthBytes).Build();

        var result = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_Notes", ["Notes"])));

        Assert.Equal(["key-type-not-allowed"], Codes(result));
    }

    [Fact]
    public void AllowsMaxTypesAsIncludedColumnsButNotLegacyLargeObjects()
    {
        var snapshot = OrdersTable()
            .WithColumn("Orders", "Notes", "nvarchar", -1)
            .WithColumn("Orders", "LegacyNotes", "ntext", 16)
            .Build();

        var results = Validate(
            snapshot,
            Create("Orders", "IX_A", ["CustomerId"], includes: ["Notes"]),
            Create("Orders", "IX_B", ["CustomerId"], includes: ["LegacyNotes"]));

        Assert.Empty(results[0].Issues);
        Assert.Equal(["include-type-not-allowed"], Codes(results[1]));
    }

    [Fact]
    public void RejectsKeyWiderThan1700Bytes()
    {
        var snapshot = OrdersTable()
            .WithColumn("Orders", "Street", "nvarchar", 900)
            .WithColumn("Orders", "City", "nvarchar", 802)
            .Build();

        var tooWide = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_Address", ["Street", "City"])));
        var justFits = Assert.Single(Validate(snapshot, Create("Orders", "IX_Orders_Street", ["Street", "CustomerId", "OrderDate"])));

        Assert.Equal(["key-too-wide"], Codes(tooWide));
        Assert.Contains("1702 bytes", tooWide.Issues[0].Message);
        Assert.Empty(justFits.Issues);
    }

    [Fact]
    public void RejectsMoreThan32KeyColumns()
    {
        var builder = Snapshot();
        var columns = Enumerable.Range(1, 33).Select(i => $"C{i}").ToArray();
        foreach (var column in columns)
        {
            builder.WithColumn("Wide", column, "tinyint", 1);
        }

        var result = Assert.Single(Validate(builder.Build(), Create("Wide", "IX_Wide_All", columns)));

        Assert.Equal(["too-many-key-columns"], Codes(result));
    }

    [Theory]
    [InlineData("PK_Orders")]
    [InlineData("UQ_Orders_OrderNumber")]
    public void RejectsDropOfPrimaryKeyOrUniqueIndex(string indexName)
    {
        var snapshot = OrdersTable().WithIndex("Orders", "UQ_Orders_OrderNumber", keys: ["CustomerId"], isUnique: true).Build();

        var result = Assert.Single(Validate(snapshot, new DropIndex(Dbo("Orders"), indexName)));

        Assert.False(result.IsAccepted);
        Assert.Equal(["drop-protected"], Codes(result));
    }

    [Fact]
    public void WarnsButAcceptsDropOfOnlyIndexSupportingAForeignKey()
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var result = Assert.Single(Validate(snapshot, new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId")));

        Assert.True(result.IsAccepted);
        var warning = Assert.Single(result.Issues);
        Assert.Equal(("drop-unindexes-foreign-key", ValidationLevel.Warning), (warning.Code, warning.Level));
    }

    [Fact]
    public void DoesNotWarnWhenAnotherIndexStillSupportsTheForeignKey()
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"])
            .WithIndex("Orders", "IX_Orders_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var result = Assert.Single(Validate(snapshot, new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId")));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void WarnsWhenTableWouldExceedTheIndexGuidelineAfterAllChanges()
    {
        var validator = new ProposalValidator(new ValidatorOptions { MaxNonclusteredIndexesPerTable = 2 });
        IndexChange[] changes =
        [
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId"]),
            Create("Orders", "IX_Orders_OrderDate", ["OrderDate"]),
        ];

        var withoutDrop = validator.Validate(OrdersTable().Build(), ToFindings(changes));
        var withDrop = validator.Validate(OrdersTable().Build(), ToFindings([.. changes, new DropIndex(Dbo("Orders"), "IX_Orders_Status")]));

        Assert.All(withoutDrop, result =>
        {
            Assert.True(result.IsAccepted);
            Assert.Equal(["too-many-indexes"], Codes(result));
        });
        Assert.All(withDrop, result => Assert.Empty(result.Issues));
    }

    [Fact]
    public void WarnsOnEveryDropWhenTheBatchRemovesAllSupportForAForeignKey()
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"])
            .WithIndex("Orders", "IX_Orders_CustomerId_OrderDate", keys: ["CustomerId", "OrderDate"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var results = Validate(
            snapshot,
            new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId"),
            new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId_OrderDate"));

        Assert.All(results, result =>
        {
            Assert.True(result.IsAccepted);
            Assert.Equal(["drop-unindexes-foreign-key"], Codes(result));
        });
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "([OrderDate]>'2025-01-01')")]
    public void DisabledOrFilteredIndexDoesNotCountAsRemainingForeignKeySupport(bool isDisabled, string? filter)
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"])
            .WithIndex("Orders", "IX_Orders_CustomerId_Partial", keys: ["CustomerId"], isDisabled: isDisabled, filter: filter)
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var result = Assert.Single(Validate(snapshot, new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId")));

        Assert.Equal(["drop-unindexes-foreign-key"], Codes(result));
    }

    [Fact]
    public void DisabledIndexesDoNotCountTowardsTheIndexGuideline()
    {
        var validator = new ProposalValidator(new ValidatorOptions { MaxNonclusteredIndexesPerTable = 2 });
        var snapshot = OrdersTable().WithIndex("Orders", "IX_Orders_Old", keys: ["OrderDate"], isDisabled: true).Build();

        var result = Assert.Single(validator.Validate(snapshot, ToFindings([Create("Orders", "IX_Orders_CustomerId", ["CustomerId"])])));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DoesNotWarnWhenACreateInTheSameBatchKeepsTheForeignKeySupported()
    {
        var snapshot = OrdersTable()
            .WithIndex("Orders", "IX_Orders_CustomerId", keys: ["CustomerId"])
            .WithForeignKey("FK_Orders_Customers", "Orders", ["CustomerId"], "Customers")
            .Build();

        var results = Validate(
            snapshot,
            new DropIndex(Dbo("Orders"), "IX_Orders_CustomerId"),
            Create("Orders", "IX_Orders_CustomerId_OrderDate", ["CustomerId", "OrderDate"]));

        Assert.All(results, result => Assert.Empty(result.Issues));
    }

    [Fact]
    public void RejectedDropsDoNotLowerTheProjectedIndexCount()
    {
        var validator = new ProposalValidator(new ValidatorOptions { MaxNonclusteredIndexesPerTable = 2 });

        var results = validator.Validate(OrdersTable().Build(), ToFindings(
        [
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId"]),
            Create("Orders", "IX_Orders_OrderDate", ["OrderDate"]),
            new DropIndex(Dbo("Orders"), "PK_Orders"),
            new DropIndex(Dbo("Orders"), "IX_Orders_Gone"),
        ]));

        Assert.Equal(["too-many-indexes"], Codes(results[0]));
        Assert.Equal(["too-many-indexes"], Codes(results[1]));
        Assert.Equal(["drop-protected"], Codes(results[2]));
        Assert.Equal(["index-not-found"], Codes(results[3]));
    }

    [Fact]
    public void RejectedCreatesDoNotRaiseTheProjectedIndexCount()
    {
        var validator = new ProposalValidator(new ValidatorOptions { MaxNonclusteredIndexesPerTable = 2 });

        var results = validator.Validate(OrdersTable().Build(), ToFindings(
        [
            Create("Orders", "IX_Orders_CustomerId", ["CustomerId"]),
            Create("Orders", "IX_Orders_Region", ["Region"]),
        ]));

        Assert.Empty(results[0].Issues);
        Assert.Equal(["column-not-found"], Codes(results[1]));
    }

    private static IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, params IndexChange[] changes) =>
        Validator.Validate(snapshot, ToFindings(changes));

    private static IReadOnlyList<Finding> ToFindings(IEnumerable<IndexChange> changes) =>
        changes.Select(change => new Finding("test", Severity.Medium, change.Table, change.IndexName, "Test proposal.", [], change)).ToList();

    private static CreateIndex Create(string table, string name, string[] keys, string[]? includes = null) =>
        new(Dbo(table), name, keys.Select(key => new IndexKeyColumn(key, IsDescending: false)).ToList(), includes ?? []);

    private static IEnumerable<string> Codes(ProposalValidation result) => result.Issues.Select(issue => issue.Code);
}
