using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public sealed class UnindexedForeignKeyRule : IIndexRule
{
    public string Id => "unindexed-foreign-key";

    public IEnumerable<Finding> Evaluate(DatabaseSnapshot snapshot, RuleOptions options) =>
        snapshot.ForeignKeys
            .Where(foreignKey => !HasSupportingIndex(snapshot, foreignKey))
            .Select(foreignKey => UnindexedFinding(snapshot, foreignKey));

    private static bool HasSupportingIndex(DatabaseSnapshot snapshot, ForeignKey foreignKey) =>
        snapshot.Indexes.Any(index =>
            index.Table == foreignKey.Table && IndexCoverage.LeadsWith(index, [.. foreignKey.Columns]));

    private Finding UnindexedFinding(DatabaseSnapshot snapshot, ForeignKey foreignKey)
    {
        var columns = string.Join(", ", foreignKey.Columns);

        return new(
            Id,
            Severity.Low,
            foreignKey.Table,
            $"Foreign key {foreignKey.Name} ({columns}) has no supporting index: deletes and key updates on " +
            $"{foreignKey.ReferencedTable} must scan {foreignKey.Table} to check it, and joins on {columns} cannot seek.",
            [
                Evidence.From("foreignKey", foreignKey.Name),
                Evidence.From("columns", columns),
                Evidence.From("referencedTable", foreignKey.ReferencedTable.ToString()),
            ],
            new CreateIndex(
                foreignKey.Table,
                IndexNaming.ProposeName(snapshot, foreignKey.Table, foreignKey.Columns),
                foreignKey.Columns.Select(column => new IndexKeyColumn(column, IsDescending: false)).ToList(),
                IncludedColumns: []));
    }
}
