using System.Globalization;

using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed class MissingIndexRule : IIndexRule
{
    private const double HighImpactPercent = 50;

    public string Id => "missing-index";

    public IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options)
    {
        foreach (var suggestion in snapshot.MissingIndexes.Where(suggestion => !IsAlreadyCovered(snapshot, suggestion)))
        {
            yield return suggestion.UserSeeks >= options.MinimumMissingIndexSeeks
                ? MissingFinding(snapshot, suggestion)
                : TooFewSeeksFinding(suggestion, options.MinimumMissingIndexSeeks);
        }
    }

    // Covered when an index seeks on all equality columns and then ranges on one of the inequality columns.
    // Included columns are ignored: a matching index that lacks them should be extended, not duplicated.
    private static bool IsAlreadyCovered(DatabaseSnapshot snapshot, MissingIndex suggestion) =>
        snapshot.Indexes
            .Where(index => index.Table == suggestion.Table
                && IndexCoverage.ServesEveryRow(index)
                && IndexCoverage.LeadsWith(index.KeyColumns, [.. suggestion.EqualityColumns]))
            .Any(index => suggestion.InequalityColumns.Count == 0 || RangesOnInequalityColumn(index, suggestion));

    private static bool RangesOnInequalityColumn(IndexInfo index, MissingIndex suggestion)
    {
        var nextKeyPosition = suggestion.EqualityColumns.Count;

        return index.KeyColumns.Count > nextKeyPosition
            && suggestion.InequalityColumns.Contains(index.KeyColumns[nextKeyPosition].Name, StringComparer.OrdinalIgnoreCase);
    }

    private Finding MissingFinding(DatabaseSnapshot snapshot, MissingIndex suggestion)
    {
        var keyColumns = suggestion.EqualityColumns.Concat(suggestion.InequalityColumns).ToList();

        return new(
            Id,
            suggestion.AverageUserImpactPercent >= HighImpactPercent ? Severity.High : Severity.Medium,
            suggestion.Table,
            Subject(suggestion),
            string.Create(
                CultureInfo.InvariantCulture,
                $"SQL Server recorded {suggestion.UserSeeks:N0} seeks that would use an index on {Describe(suggestion)}, " +
                $"with an estimated {suggestion.AverageUserImpactPercent:0.#}% lower query cost."),
            SuggestionEvidence(suggestion),
            new CreateIndex(
                suggestion.Table,
                IndexNaming.ProposeName(snapshot, suggestion.Table, keyColumns),
                keyColumns.Select(column => new IndexKeyColumn(column, IsDescending: false)).ToList(),
                suggestion.IncludedColumns));
    }

    private Finding TooFewSeeksFinding(MissingIndex suggestion, long minimumSeeks) =>
        new(
            Id,
            Severity.Info,
            suggestion.Table,
            Subject(suggestion),
            string.Create(
                CultureInfo.InvariantCulture,
                $"SQL Server suggests an index on {Describe(suggestion)}, but only {suggestion.UserSeeks:N0} seeks " +
                $"(minimum {minimumSeeks:N0}) would have used it, which may be a one-off query."),
            [.. SuggestionEvidence(suggestion), Evidence.From("minimumSeeks", minimumSeeks)],
            ProposedChange: null);

    private static IReadOnlyList<Evidence> SuggestionEvidence(MissingIndex suggestion) =>
    [
        Evidence.From("userSeeks", suggestion.UserSeeks),
        Evidence.From("averageImpactPercent", suggestion.AverageUserImpactPercent),
        Evidence.From("averageQueryCost", suggestion.AverageTotalUserCost),
        Evidence.From("equalityColumns", string.Join(", ", suggestion.EqualityColumns)),
        Evidence.From("inequalityColumns", string.Join(", ", suggestion.InequalityColumns)),
        Evidence.From("includedColumns", string.Join(", ", suggestion.IncludedColumns)),
    ];

    private static string Subject(MissingIndex suggestion)
    {
        var keys = string.Join(",", suggestion.EqualityColumns.Concat(suggestion.InequalityColumns));
        return suggestion.IncludedColumns.Count > 0 ? $"{keys}+{string.Join(",", suggestion.IncludedColumns)}" : keys;
    }

    private static string Describe(MissingIndex suggestion)
    {
        var keys = string.Join(", ", suggestion.EqualityColumns.Concat(suggestion.InequalityColumns));
        var includes = suggestion.IncludedColumns.Count > 0 ? $" INCLUDE ({string.Join(", ", suggestion.IncludedColumns)})" : "";
        return $"{suggestion.Table} ({keys}){includes}";
    }
}
