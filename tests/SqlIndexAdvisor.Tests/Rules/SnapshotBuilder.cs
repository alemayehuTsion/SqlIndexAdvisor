using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Rules;

public sealed class SnapshotBuilder
{
    private static readonly DateTimeOffset CollectedAt = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly List<IndexInfo> _indexes = [];
    private TimeSpan _window = TimeSpan.FromDays(30);

    public static SnapshotBuilder Snapshot() => new();

    public SnapshotBuilder WithWindow(TimeSpan window)
    {
        _window = window;
        return this;
    }

    public SnapshotBuilder WithIndex(
        string table,
        string name,
        string[]? keys = null,
        long reads = 0,
        long writes = 0,
        bool isClustered = false,
        bool isPrimaryKey = false,
        bool isUnique = false,
        string[]? includes = null)
    {
        _indexes.Add(new IndexInfo(
            Dbo(table),
            name,
            isClustered,
            isUnique || isPrimaryKey,
            isPrimaryKey,
            (keys ?? ["Id"]).Select(ParseKeyColumn).ToList(),
            includes ?? [],
            new IndexUsage(UserSeeks: reads, UserScans: 0, UserLookups: 0, UserUpdates: writes > 0 ? 1 : 0, LeafWrites: writes)));
        return this;
    }

    public DatabaseSnapshot Build() =>
        new(
            "TestDb",
            CollectedAt,
            ServerStartTime: CollectedAt - _window,
            DatabaseCreatedAt: CollectedAt - _window - TimeSpan.FromDays(1),
            _indexes,
            MissingIndexes: [],
            ForeignKeys: []);

    public static QualifiedName Dbo(string table) => new("dbo", table);

    private static IndexKeyColumn ParseKeyColumn(string key) =>
        key.EndsWith(" DESC", StringComparison.Ordinal)
            ? new IndexKeyColumn(key[..^5], IsDescending: true)
            : new IndexKeyColumn(key, IsDescending: false);
}
