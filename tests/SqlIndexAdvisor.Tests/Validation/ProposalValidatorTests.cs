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
        var info = new Finding("test", Severity.Info, Dbo("Orders"), "Nothing to do.", [], ProposedChange: null);

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

    private static IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, params IndexChange[] changes) =>
        Validator.Validate(
            snapshot,
            changes.Select(change => new Finding("test", Severity.Medium, change.Table, "Test proposal.", [], change)).ToList());

    private static CreateIndex Create(string table, string name, string[] keys, string[]? includes = null) =>
        new(Dbo(table), name, keys.Select(key => new IndexKeyColumn(key, IsDescending: false)).ToList(), includes ?? []);

    private static IEnumerable<string> Codes(ProposalValidation result) => result.Issues.Select(issue => issue.Code);
}
