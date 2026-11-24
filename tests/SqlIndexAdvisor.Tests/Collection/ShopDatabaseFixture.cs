using Microsoft.Data.SqlClient;

using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Collection;

public sealed class ShopDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "SQLINDEXADVISOR_ConnectionStrings__Shop";

    private IReadOnlyList<IndexInfo>? _indexes;

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        _indexes = await new DatabaseCollector(connection).CollectIndexesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<IndexInfo> RequireIndexes()
    {
        if (_indexes is null)
        {
            Assert.Skip(
                $"Set {ConnectionStringVariable} to a SqlIndexAdvisorShop database built with database/run-all.ps1.");
        }

        return _indexes;
    }
}
