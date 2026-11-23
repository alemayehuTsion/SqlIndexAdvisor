namespace SqlIndexAdvisor.Core.Collection;

public sealed record MissingIndex(
    QualifiedName Table,
    IReadOnlyList<string> EqualityColumns,
    IReadOnlyList<string> InequalityColumns,
    IReadOnlyList<string> IncludedColumns,
    long UserSeeks,
    double AverageTotalUserCost,
    double AverageUserImpactPercent);
