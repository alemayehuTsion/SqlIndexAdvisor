using Dapper;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

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
