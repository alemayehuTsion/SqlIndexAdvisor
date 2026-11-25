using System.Globalization;

using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed class UnusedIndexRule : IIndexRule
{
    public string Id => "unused-index";

    public IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options)
    {
        var hasEnoughHistory = snapshot.StatisticsWindow >= options.MinimumStatisticsWindow;

        foreach (var index in snapshot.Indexes.Where(IsUnusedCandidate))
        {
            yield return hasEnoughHistory
                ? UnusedFinding(index, snapshot.StatisticsWindow)
                : NotEnoughHistoryFinding(index, snapshot.StatisticsWindow, options.MinimumStatisticsWindow);
        }
    }

    // Primary keys and unique indexes enforce correctness even when nothing reads them, so they are never candidates.
    private static bool IsUnusedCandidate(IndexInfo index) =>
        !index.IsClustered
        && !index.IsPrimaryKey
        && !index.IsUnique
        && index.Usage.TotalReads == 0
        && index.Usage.LeafWrites > 0;

    private Finding UnusedFinding(IndexInfo index, TimeSpan window) =>
        new(
            Id,
            Severity.Medium,
            index.Table,
            index.Name,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{index.Name} has not been read in {Describe(window)} but absorbed {index.Usage.LeafWrites:N0} row writes."),
            UsageEvidence(index, window),
            new DropIndex(index.Table, index.Name));

    private Finding NotEnoughHistoryFinding(IndexInfo index, TimeSpan window, TimeSpan minimum) =>
        new(
            Id,
            Severity.Info,
            index.Table,
            index.Name,
            $"{index.Name} has no reads, but statistics cover only {Describe(window)} " +
            $"(minimum {Describe(minimum)}), which is not enough history to call it unused.",
            [.. UsageEvidence(index, window), Evidence.From("minimumWindowHours", minimum.TotalHours)],
            ProposedChange: null);

    private static IReadOnlyList<Evidence> UsageEvidence(IndexInfo index, TimeSpan window) =>
    [
        Evidence.From("reads", index.Usage.TotalReads),
        Evidence.From("rowWrites", index.Usage.LeafWrites),
        Evidence.From("writeStatements", index.Usage.UserUpdates),
        Evidence.From("statisticsWindowHours", window.TotalHours),
    ];

    private static string Describe(TimeSpan span) =>
        span >= TimeSpan.FromDays(2)
            ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalDays:0.#} days")
            : string.Create(CultureInfo.InvariantCulture, $"{span.TotalHours:0.#} hours");
}
