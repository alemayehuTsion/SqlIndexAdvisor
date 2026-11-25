using System.Globalization;

using Dapper;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

var options = ParseRuleOptions(args);

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

var server = await connection.QuerySingleAsync<(string Version, string Edition)>(
    """
    SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
           CAST(SERVERPROPERTY('Edition') AS nvarchar(128));
    """);

var snapshot = await new DatabaseCollector(connection).CollectSnapshotAsync();

var windowStart = snapshot.StatisticsSince == snapshot.ServerStartTime ? "server start" : "database creation";
Console.WriteLine($"{snapshot.DatabaseName} on SQL Server {server.Version} ({server.Edition})");
Console.WriteLine(
    $"Collected {snapshot.Indexes.Count} indexes, {snapshot.MissingIndexes.Count} missing-index suggestions, " +
    $"{snapshot.ForeignKeys.Count} foreign keys, {snapshot.Columns.Count} columns");
Console.WriteLine(
    $"Statistics cover {snapshot.StatisticsWindow.TotalHours:N1} hours " +
    $"(since {windowStart}, {snapshot.StatisticsSince:yyyy-MM-dd HH:mm zzz})");

if (options != new RuleOptions())
{
    Console.WriteLine(
        $"Thresholds overridden: minimum window {options.MinimumStatisticsWindow.TotalHours:N1} hours, " +
        $"minimum seeks {options.MinimumMissingIndexSeeks:N0}");
}

var findings = RulesEngine.Default.Evaluate(snapshot, options);
var validations = new ProposalValidator().Validate(snapshot, findings);
var validationByFinding = validations.ToDictionary(validation => validation.Finding);

Console.WriteLine();
Console.WriteLine(
    $"{findings.Count} findings, {validations.Count} proposed changes, " +
    $"{validations.Count(validation => validation.IsAccepted)} accepted by validation:");
foreach (var finding in findings)
{
    Console.WriteLine();
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId} on {finding.Table}");
    Console.WriteLine($"  {finding.Summary}");
    if (finding.ProposedChange is null)
    {
        continue;
    }

    var validation = validationByFinding[finding];
    Console.WriteLine($"  Proposes: {Describe(finding.ProposedChange)} [{(validation.IsAccepted ? "accepted" : "REJECTED")}]");
    foreach (var issue in validation.Issues)
    {
        Console.WriteLine($"    {issue.Level}: {issue.Code} - {issue.Message}");
    }
}

static string Describe(IndexChange change) =>
    change switch
    {
        CreateIndex create =>
            $"create {create.IndexName} ({string.Join(", ", create.KeyColumns.Select(key => key.Name))})" +
            (create.IncludedColumns.Count > 0 ? $" INCLUDE ({string.Join(", ", create.IncludedColumns)})" : ""),
        DropIndex drop => $"drop {drop.IndexName}",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown index change."),
    };

static RuleOptions ParseRuleOptions(string[] arguments)
{
    var options = new RuleOptions();

    for (var i = 0; i < arguments.Length; i++)
    {
        options = arguments[i] switch
        {
            "--min-window-hours" => options with { MinimumStatisticsWindow = TimeSpan.FromHours(ParseNumber(arguments, ++i)) },
            "--min-seeks" => options with { MinimumMissingIndexSeeks = (long)ParseNumber(arguments, ++i) },
            _ => throw new ArgumentException($"Unknown option '{arguments[i]}'. Use --min-window-hours <n> or --min-seeks <n>."),
        };
    }

    return options;
}

static double ParseNumber(string[] arguments, int position) =>
    position < arguments.Length && double.TryParse(arguments[position], CultureInfo.InvariantCulture, out var value) && value >= 0
        ? value
        : throw new ArgumentException($"Option '{arguments[position - 1]}' needs a non-negative number.");
