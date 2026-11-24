using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Core.Rules;

public abstract record IndexChange(QualifiedName Table, string IndexName);

public sealed record CreateIndex(
    QualifiedName Table,
    string IndexName,
    IReadOnlyList<IndexKeyColumn> KeyColumns,
    IReadOnlyList<string> IncludedColumns)
    : IndexChange(Table, IndexName);

public sealed record DropIndex(QualifiedName Table, string IndexName)
    : IndexChange(Table, IndexName);
