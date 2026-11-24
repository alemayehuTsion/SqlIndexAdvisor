using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

internal static class IndexCoverage
{
    // True when the index's first key columns are exactly these columns, in any order: a seek on (A, B) can use an
    // index on (B, A, C) but not one on (A, C, B).
    public static bool LeadsWith(IndexInfo index, IReadOnlyCollection<string> columns) =>
        index.KeyColumns.Count >= columns.Count
        && index.KeyColumns
            .Take(columns.Count)
            .Select(key => key.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(columns);
}
