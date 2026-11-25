using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Tests.Collection;

[Trait("Category", "Integration")]
public class DatabaseCollectorTests(ShopDatabaseFixture database) : IClassFixture<ShopDatabaseFixture>
{
    [Fact]
    public void UnusedTrapIndex_HasWritesButNoReads()
    {
        var index = FindIndex("Orders", "IX_Orders_Status");

        if (index.Usage.LeafWrites == 0)
        {
            Assert.Skip("Usage counters are empty, so SQL Server restarted after the database was built. " +
                        "Rebuild it with database/run-all.ps1 -Recreate.");
        }

        Assert.Equal(0, index.Usage.TotalReads);
        Assert.Equal(500_000, index.Usage.LeafWrites);
    }

    [Fact]
    public void DuplicateTrapIndexes_HaveIdenticalKeys()
    {
        var email = FindIndex("Customers", "IX_Customers_Email");
        var emailLookup = FindIndex("Customers", "IX_Customers_EmailLookup");

        Assert.Equal([new IndexKeyColumn("Email", IsDescending: false)], email.KeyColumns);
        Assert.Equal(email.KeyColumns, emailLookup.KeyColumns);
    }

    [Fact]
    public void PrimaryKeys_AreUniqueClusteredPrimaryKeys()
    {
        var primaryKeys = database.RequireIndexes().Where(index => index.Name.StartsWith("PK_")).ToList();

        Assert.Equal(4, primaryKeys.Count);
        Assert.All(primaryKeys, index =>
        {
            Assert.True(index.IsPrimaryKey);
            Assert.True(index.IsClustered);
            Assert.True(index.IsUnique);
        });
    }

    [Fact]
    public void CompositePrimaryKey_KeepsKeyColumnOrder()
    {
        var index = FindIndex("OrderItems", "PK_OrderItems");

        Assert.Equal(["OrderId", "LineNumber"], index.KeyColumns.Select(column => column.Name));
    }

    [Fact]
    public void SlowQuery_ProducesMissingIndexOnCustomerIdAndOrderDate()
    {
        var suggestions = database.RequireMissingIndexes()
            .Where(suggestion => suggestion.Table == new QualifiedName("dbo", "Orders"))
            .ToList();

        if (suggestions.Count == 0)
        {
            Assert.Skip("No missing-index suggestions, so SQL Server restarted after the slow query ran. " +
                        "Rebuild with database/run-all.ps1 -Recreate.");
        }

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(["CustomerId"], suggestion.EqualityColumns);
        Assert.Equal(["OrderDate"], suggestion.InequalityColumns);
        Assert.Empty(suggestion.IncludedColumns);
        Assert.True(suggestion.UserSeeks >= 1);
    }

    [Fact]
    public void ForeignKeys_AreCollectedWithColumnsAndReferencedTables()
    {
        var foreignKeys = database.RequireForeignKeys();

        Assert.Equal(
            [
                "FK_OrderItems_Orders: [dbo].[OrderItems] (OrderId) -> [dbo].[Orders]",
                "FK_OrderItems_Products: [dbo].[OrderItems] (ProductId) -> [dbo].[Products]",
                "FK_Orders_Customers: [dbo].[Orders] (CustomerId) -> [dbo].[Customers]",
            ],
            foreignKeys.Select(fk => $"{fk.Name}: {fk.Table} ({string.Join(", ", fk.Columns)}) -> {fk.ReferencedTable}"));
    }

    [Fact]
    public void Snapshot_RecordsDatabaseAndServerClock()
    {
        var snapshot = database.RequireSnapshot();

        Assert.Equal("SqlIndexAdvisorShop", snapshot.DatabaseName);
        Assert.True(snapshot.StatisticsWindow > TimeSpan.Zero);
        Assert.True(snapshot.StatisticsSince >= snapshot.DatabaseCreatedAt);
        Assert.True(snapshot.StatisticsSince >= snapshot.ServerStartTime);
        Assert.InRange(DateTimeOffset.UtcNow - snapshot.CollectedAt, TimeSpan.FromMinutes(-5), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RulesEngine_FindsEveryProblemPlantedInTheShopDatabase()
    {
        var snapshot = database.RequireSnapshot();
        if (FindIndex("Orders", "IX_Orders_Status").Usage.LeafWrites == 0)
        {
            Assert.Skip("Usage counters are empty, so SQL Server restarted after the database was built. " +
                        "Rebuild it with database/run-all.ps1 -Recreate.");
        }

        var demoOptions = new RuleOptions { MinimumStatisticsWindow = TimeSpan.Zero, MinimumMissingIndexSeeks = 1 };
        var findings = RulesEngine.Default.Evaluate(snapshot, demoOptions);

        Assert.Equal(
            [
                "create [dbo].[OrderItems].IX_OrderItems_ProductId",
                "create [dbo].[Orders].IX_Orders_CustomerId_OrderDate",
                "drop [dbo].[Customers].IX_Customers_Email",
                "drop [dbo].[Customers].IX_Customers_EmailLookup",
                "drop [dbo].[Orders].IX_Orders_Status",
            ],
            findings
                .Select(finding => finding.ProposedChange)
                .OfType<IndexChange>()
                .Select(change => $"{(change is CreateIndex ? "create" : "drop")} {change.Table}.{change.IndexName}")
                .Order(StringComparer.Ordinal));

        var customerForeignKey = Assert.Single(findings, finding =>
            finding.Evidence.Contains(new Evidence("foreignKey", "FK_Orders_Customers")));
        Assert.Contains(new Evidence("resolvedBy", "IX_Orders_CustomerId_OrderDate"), customerForeignKey.Evidence);
    }

    [Fact]
    public void Columns_AreCollectedInTableOrderWithSizesInBytes()
    {
        var columns = database.RequireSnapshot().Columns;

        Assert.Equal(
            ["OrderId", "CustomerId", "OrderDate", "Status", "TotalAmount"],
            columns.Where(column => column.Table == new QualifiedName("dbo", "Orders")).Select(column => column.Name));

        var email = Assert.Single(columns, column => column.Table == new QualifiedName("dbo", "Customers") && column.Name == "Email");
        Assert.Equal(new TableColumn(new QualifiedName("dbo", "Customers"), "Email", "nvarchar", 512, false, false), email);
    }

    private IndexInfo FindIndex(string table, string name) =>
        database.RequireIndexes().Single(index => index.Table == new QualifiedName("dbo", table) && index.Name == name);
}
