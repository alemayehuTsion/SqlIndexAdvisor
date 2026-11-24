using Microsoft.Data.SqlClient;

using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Collection;

public sealed class ShopDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "SQLINDEXADVISOR_ConnectionStrings__Shop";

    private IReadOnlyList<IndexInfo>? _indexes;
    private IReadOnlyList<MissingIndex>? _missingIndexes;
    private IReadOnlyList<ForeignKey>? _foreignKeys;

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        var collector = new DatabaseCollector(connection);
        var cancellationToken = TestContext.Current.CancellationToken;

        _indexes = await collector.CollectIndexesAsync(cancellationToken);
        _missingIndexes = await collector.CollectMissingIndexesAsync(cancellationToken);
        _foreignKeys = await collector.CollectForeignKeysAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<IndexInfo> RequireIndexes() => _indexes ?? SkipWithoutDatabase<IndexInfo>();

    public IReadOnlyList<MissingIndex> RequireMissingIndexes() => _missingIndexes ?? SkipWithoutDatabase<MissingIndex>();

    public IReadOnlyList<ForeignKey> RequireForeignKeys() => _foreignKeys ?? SkipWithoutDatabase<ForeignKey>();

    private static IReadOnlyList<T> SkipWithoutDatabase<T>()
    {
        Assert.Skip($"Set {ConnectionStringVariable} to a SqlIndexAdvisorShop database built with database/run-all.ps1.");
        return [];
    }
}
