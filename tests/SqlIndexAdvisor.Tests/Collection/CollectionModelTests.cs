using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Collection;

public class CollectionModelTests
{
    [Fact]
    public void QualifiedName_ToString_QuotesSchemaAndName()
    {
        var table = new QualifiedName("dbo", "Order]Items");

        Assert.Equal("[dbo].[Order]]Items]", table.ToString());
    }

    [Fact]
    public void IndexUsage_TotalReads_SumsSeeksScansAndLookupsButNotWrites()
    {
        var usage = new IndexUsage(UserSeeks: 3, UserScans: 2, UserLookups: 1, UserUpdates: 100, LeafWrites: 5000);

        Assert.Equal(6, usage.TotalReads);
    }

    private static readonly DateTimeOffset Earlier = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddDays(2);
    private static readonly DateTimeOffset Collected = Later.AddHours(5);

    [Fact]
    public void DatabaseSnapshot_StatisticsWindow_StartsAtServerStartWhenDatabaseIsOlder()
    {
        var snapshot = new DatabaseSnapshot("Shop", Collected, ServerStartTime: Later, DatabaseCreatedAt: Earlier, [], [], []);

        Assert.Equal(Later, snapshot.StatisticsSince);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.StatisticsWindow);
    }

    [Fact]
    public void DatabaseSnapshot_StatisticsWindow_StartsAtDatabaseCreationWhenServerIsOlder()
    {
        var snapshot = new DatabaseSnapshot("Shop", Collected, ServerStartTime: Earlier, DatabaseCreatedAt: Later, [], [], []);

        Assert.Equal(Later, snapshot.StatisticsSince);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.StatisticsWindow);
    }
}
