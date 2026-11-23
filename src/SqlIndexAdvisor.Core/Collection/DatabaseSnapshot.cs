namespace SqlIndexAdvisor.Core.Collection;

public sealed record DatabaseSnapshot(
    string DatabaseName,
    DateTimeOffset CollectedAt,
    DateTimeOffset ServerStartTime,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<MissingIndex> MissingIndexes,
    IReadOnlyList<ForeignKey> ForeignKeys)
{
    public TimeSpan StatisticsWindow => CollectedAt - ServerStartTime;
}
