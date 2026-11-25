using Dapper;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using SqlIndexAdvisor.Cli;
using SqlIndexAdvisor.Core.Advice;
using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;
using SqlIndexAdvisor.Core.Validation;

var arguments = CliArguments.Parse(args);
var options = arguments.RuleOptions;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables(prefix: "SQLINDEXADVISOR_")
    .Build();

var connectionString = configuration.GetConnectionString("Shop")
    ?? throw new InvalidOperationException(
        "Connection string 'Shop' is not configured. Set it with " +
        "'dotnet user-secrets set ConnectionStrings:Shop <value>' or the environment variable " +
        "SQLINDEXADVISOR_ConnectionStrings__Shop.");

var aiProvider = arguments.AiProvider ?? configuration["Ai:Provider"] ?? "None";
using var chatClient = ChatClientFactory.Create(aiProvider, configuration);

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

var advice = chatClient is null
    ? DeterministicAdvice.For(findings, validations, [new AdviceIssue("ai-disabled", "AI provider is None.")])
    : await new AiAdvisor(chatClient).AdviseAsync(findings, validations);

Console.WriteLine();
Console.WriteLine(
    $"{findings.Count} findings, {validations.Count} proposed changes, " +
    $"{validations.Count(validation => validation.IsAccepted)} accepted by validation");
Console.WriteLine(advice.Source == AdviceSource.Model
    ? $"Ranked and explained by {advice.ModelId ?? ChatClientFactory.ModelFor(aiProvider, configuration)} ({aiProvider})"
    : $"Ranked deterministically: {string.Join(" ", advice.Issues.Select(issue => $"{issue.Code} - {issue.Message}"))}");

foreach (var assessment in advice.Assessments)
{
    var finding = findings.Single(candidate => candidate.Id == assessment.FindingId);

    Console.WriteLine();
    Console.WriteLine($"#{assessment.Rank} [{finding.Severity}] {finding.Id}");
    Console.WriteLine($"  {finding.Summary}");
    if (finding.Evidence.Count > 0)
    {
        Console.WriteLine($"  Evidence: {string.Join(", ", finding.Evidence.Select(evidence => $"{evidence.Name}={evidence.Value}"))}");
    }

    var validation = validations.FirstOrDefault(candidate => candidate.Finding.Id == finding.Id);
    if (finding.ProposedChange is not null && validation is not null)
    {
        Console.WriteLine($"  Proposes: {Describe(finding.ProposedChange)} [{(validation.IsAccepted ? "accepted" : "REJECTED")}]");
        foreach (var issue in validation.Issues)
        {
            Console.WriteLine($"    {issue.Level}: {issue.Code} - {issue.Message}");
        }
    }

    if (advice.Source == AdviceSource.Model)
    {
        Console.WriteLine($"  AI explanation: {assessment.Explanation}");
        if (assessment.Risks.Length > 0)
        {
            Console.WriteLine($"  AI risks: {assessment.Risks}");
        }
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
