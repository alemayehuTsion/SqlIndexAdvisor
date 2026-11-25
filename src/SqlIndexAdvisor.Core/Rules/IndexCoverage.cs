using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

internal static class IndexCoverage
{
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
