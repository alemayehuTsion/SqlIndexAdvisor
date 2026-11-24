using SqlIndexAdvisor.Core.Collection;

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

    private IndexInfo FindIndex(string table, string name) =>
        database.RequireIndexes().Single(index => index.Table == new QualifiedName("dbo", table) && index.Name == name);
}
