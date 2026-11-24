using Microsoft.Data.SqlClient;

using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Collection;

public sealed class ShopDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "SQLINDEXADVISOR_ConnectionStrings__Shop";

    private DatabaseSnapshot? _snapshot;

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        _snapshot = await new DatabaseCollector(connection).CollectSnapshotAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public DatabaseSnapshot RequireSnapshot()
    {
        if (_snapshot is null)
        {
            Assert.Skip($"Set {ConnectionStringVariable} to a SqlIndexAdvisorShop database built with database/run-all.ps1.");
        }

        return _snapshot;
    }

    public IReadOnlyList<IndexInfo> RequireIndexes() => RequireSnapshot().Indexes;

    public IReadOnlyList<MissingIndex> RequireMissingIndexes() => RequireSnapshot().MissingIndexes;

    public IReadOnlyList<ForeignKey> RequireForeignKeys() => RequireSnapshot().ForeignKeys;
}
