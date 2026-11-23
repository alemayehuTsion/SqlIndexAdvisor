namespace SqlIndexAdvisor.Core.Collection;

public sealed record IndexInfo(
    QualifiedName Table,
    string Name,
    bool IsClustered,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<IndexKeyColumn> KeyColumns,
    IReadOnlyList<string> IncludedColumns,
    IndexUsage Usage);
