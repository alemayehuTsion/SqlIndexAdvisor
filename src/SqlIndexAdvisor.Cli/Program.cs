using Dapper;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using SqlIndexAdvisor.Core;
using SqlIndexAdvisor.Core.Collection;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables(prefix: "SQLINDEXADVISOR_")
    .Build();

var connectionString = configuration.GetConnectionString("Shop")
    ?? throw new InvalidOperationException(
        "Connection string 'Shop' is not configured. Set it with " +
        "'dotnet user-secrets set ConnectionStrings:Shop <value>' or the environment variable " +
        "SQLINDEXADVISOR_ConnectionStrings__Shop.");

await using var connection = new SqlConnection(connectionString);

var server = await connection.QuerySingleAsync<(string Version, string Edition, string Database)>(
    """
    SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
           CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
           DB_NAME();
    """);

Console.WriteLine($"Connected to {server.Database} on SQL Server {server.Version} ({server.Edition})");
Console.WriteLine();

var collector = new DatabaseCollector(connection);
var indexes = await collector.CollectIndexesAsync();

Console.WriteLine($"{"Index",-50} {"Keys",-28} {"Reads",8} {"Writes",10}");
foreach (var index in indexes)
{
    var name = $"{index.Table}.{SqlIdentifier.Quote(index.Name)}";
    var keys = string.Join(", ", index.KeyColumns.Select(key => key.IsDescending ? $"{key.Name} DESC" : key.Name));
    Console.WriteLine($"{name,-50} {keys,-28} {index.Usage.TotalReads,8:N0} {index.Usage.LeafWrites,10:N0}");
}

var missingIndexes = await collector.CollectMissingIndexesAsync();

Console.WriteLine();
Console.WriteLine($"{"Missing index on",-24} {"Equality",-16} {"Inequality",-16} {"Include",-12} {"Seeks",6} {"Impact",7}");
foreach (var suggestion in missingIndexes)
{
    Console.WriteLine(
        $"{suggestion.Table,-24} {string.Join(", ", suggestion.EqualityColumns),-16} " +
        $"{string.Join(", ", suggestion.InequalityColumns),-16} {string.Join(", ", suggestion.IncludedColumns),-12} " +
        $"{suggestion.UserSeeks,6:N0} {suggestion.AverageUserImpactPercent,6:N1}%");
}

var foreignKeys = await collector.CollectForeignKeysAsync();

Console.WriteLine();
Console.WriteLine($"{"Foreign key",-24} {"Table",-20} {"Columns",-12} References");
foreach (var foreignKey in foreignKeys)
{
    Console.WriteLine(
        $"{foreignKey.Name,-24} {foreignKey.Table,-20} {string.Join(", ", foreignKey.Columns),-12} {foreignKey.ReferencedTable}");
}
