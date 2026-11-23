namespace SqlIndexAdvisor.Core.Collection;

public sealed record ForeignKey(
    string Name,
    QualifiedName Table,
    IReadOnlyList<string> Columns,
    QualifiedName ReferencedTable);
