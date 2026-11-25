using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

internal static class IndexCoverage
{
    // A disabled index is neither maintained nor usable, and a filtered one only holds some rows, so neither can
    // support a foreign key or make a missing-index suggestion unnecessary.
    public static bool ServesEveryRow(IndexInfo index) => !index.IsDisabled && !index.IsFiltered;

    // True when the first key columns are exactly these columns, in any order: a seek on (A, B) can use an index on
    // (B, A, C) but not one on (A, C, B).
    public static bool LeadsWith(IReadOnlyList<IndexKeyColumn> keyColumns, IReadOnlyCollection<string> columns) =>
        keyColumns.Count >= columns.Count
        && keyColumns
            .Take(columns.Count)
            .Select(key => key.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(columns);
}
