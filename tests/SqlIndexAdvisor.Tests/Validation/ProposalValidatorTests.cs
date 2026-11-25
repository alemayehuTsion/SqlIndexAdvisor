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

    private static IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, params IndexChange[] changes) =>
        Validator.Validate(
            snapshot,
            changes.Select(change => new Finding("test", Severity.Medium, change.Table, "Test proposal.", [], change)).ToList());

    private static CreateIndex Create(string table, string name, string[] keys, string[]? includes = null) =>
        new(Dbo(table), name, keys.Select(key => new IndexKeyColumn(key, IsDescending: false)).ToList(), includes ?? []);

    private static IEnumerable<string> Codes(ProposalValidation result) => result.Issues.Select(issue => issue.Code);
}
