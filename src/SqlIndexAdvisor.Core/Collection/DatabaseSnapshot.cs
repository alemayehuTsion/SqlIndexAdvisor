namespace SqlIndexAdvisor.Core.Collection;

public sealed record DatabaseSnapshot(
    string DatabaseName,
    DateTimeOffset CollectedAt,
    DateTimeOffset ServerStartTime,
    DateTimeOffset DatabaseCreatedAt,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<MissingIndex> MissingIndexes,
    IReadOnlyList<ForeignKey> ForeignKeys,
    IReadOnlyList<TableColumn> Columns)
{
    // Usage counters reset on restart and start empty for a new database, so they only cover the later of the two.
    public DateTimeOffset StatisticsSince =>
        ServerStartTime > DatabaseCreatedAt ? ServerStartTime : DatabaseCreatedAt;

    public TimeSpan StatisticsWindow => CollectedAt - StatisticsSince;
}
