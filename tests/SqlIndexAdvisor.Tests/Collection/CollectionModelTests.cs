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

    [Fact]
    public void DatabaseSnapshot_StatisticsWindow_IsTimeSinceServerStart()
    {
        var started = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var collected = started.AddDays(3).AddHours(2);

        var snapshot = new DatabaseSnapshot("Shop", collected, started, [], [], []);

        Assert.Equal(TimeSpan.FromHours(74), snapshot.StatisticsWindow);
    }
}
