using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed class DuplicateIndexRule : IIndexRule
{
    public string Id => "duplicate-index";

    public IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options)
    {
        var duplicateGroups = snapshot.Indexes
            .GroupBy(index => (index.Table, Signature(index)))
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var ranked = group.OrderBy(KeepPriority).ToList();
            var keeper = ranked[0];

            foreach (var duplicate in ranked.Skip(1))
            {
                yield return IsProtected(duplicate)
                    ? ProtectedDuplicateFinding(duplicate, keeper)
                    : DuplicateFinding(duplicate, keeper);
            }
        }
    }

    // Key column order and direction matter for seeks; included columns are just stored, so their order does not.
    private static string Signature(IndexInfo index)
    {
        var keys = index.KeyColumns.Select(key => $"{key.Name}{(key.IsDescending ? " DESC" : "")}");
        var includes = index.IncludedColumns.Order(StringComparer.Ordinal);
        return $"{string.Join("\u001f", keys)}\u001e{string.Join("\u001f", includes)}";
    }

    private static (bool, bool, long, string) KeepPriority(IndexInfo index) =>
        (!IsProtected(index), !index.IsClustered, -index.Usage.TotalReads, index.Name);

    private static bool IsProtected(IndexInfo index) => index.IsClustered || index.IsPrimaryKey || index.IsUnique;

    private Finding DuplicateFinding(IndexInfo duplicate, IndexInfo keeper) =>
        new(
            Id,
            Severity.Medium,
            duplicate.Table,
            duplicate.Name,
            $"{duplicate.Name} duplicates {keeper.Name}: same key columns ({DescribeKeys(keeper)}) " +
            "and same included columns.",
            DuplicateEvidence(duplicate, keeper),
            new DropIndex(duplicate.Table, duplicate.Name));

    private Finding ProtectedDuplicateFinding(IndexInfo duplicate, IndexInfo keeper) =>
        new(
            Id,
            Severity.Info,
            duplicate.Table,
            duplicate.Name,
            $"{duplicate.Name} duplicates {keeper.Name}, but both enforce a constraint " +
            "(primary key, unique or clustered), so dropping either needs a manual review.",
            DuplicateEvidence(duplicate, keeper),
            ProposedChange: null);

    private static IReadOnlyList<Evidence> DuplicateEvidence(IndexInfo duplicate, IndexInfo keeper) =>
    [
        Evidence.From("duplicateOf", keeper.Name),
        Evidence.From("keyColumns", DescribeKeys(keeper)),
        Evidence.From("reads", duplicate.Usage.TotalReads),
        Evidence.From("keeperReads", keeper.Usage.TotalReads),
        Evidence.From("rowWrites", duplicate.Usage.LeafWrites),
    ];

    private static string DescribeKeys(IndexInfo index) =>
        string.Join(", ", index.KeyColumns.Select(key => key.IsDescending ? $"{key.Name} DESC" : key.Name));
}
