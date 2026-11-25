using SqlIndexAdvisor.Core.Collection;

namespace SqlIndexAdvisor.Tests.Rules;

public sealed class SnapshotBuilder
{
    private static readonly DateTimeOffset CollectedAt = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly List<IndexInfo> _indexes = [];
    private readonly List<ForeignKey> _foreignKeys = [];
    private readonly List<MissingIndex> _missingIndexes = [];
    private readonly List<TableColumn> _columns = [];
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
        string[]? includes = null,
        bool isDisabled = false,
        string? filter = null)
    {
        _indexes.Add(new IndexInfo(
            Dbo(table),
            name,
            isClustered,
            isUnique || isPrimaryKey,
            isPrimaryKey,
            (keys ?? ["Id"]).Select(ParseKeyColumn).ToList(),
            includes ?? [],
            new IndexUsage(UserSeeks: reads, UserScans: 0, UserLookups: 0, UserUpdates: writes > 0 ? 1 : 0, LeafWrites: writes),
            isDisabled,
            filter));
        return this;
    }

    public SnapshotBuilder WithForeignKey(string name, string table, string[] columns, string referencedTable)
    {
        _foreignKeys.Add(new ForeignKey(name, Dbo(table), columns, Dbo(referencedTable)));
        return this;
    }

    public SnapshotBuilder WithMissingIndex(
        string table,
        string[] equality,
        string[]? inequality = null,
        string[]? includes = null,
        long seeks = 100,
        double impactPercent = 90)
    {
        _missingIndexes.Add(new MissingIndex(
            Dbo(table), equality, inequality ?? [], includes ?? [], seeks, AverageTotalUserCost: 5.9, impactPercent));
        return this;
    }

    public SnapshotBuilder WithColumn(
        string table,
        string name,
        string type = "int",
        int maxLengthBytes = 4,
        bool isNullable = false,
        bool isComputed = false)
    {
        _columns.Add(new TableColumn(Dbo(table), name, type, maxLengthBytes, isNullable, isComputed));
        return this;
    }

    public DatabaseSnapshot Build() =>
        new(
            "TestDb",
            CollectedAt,
            ServerStartTime: CollectedAt - _window,
            DatabaseCreatedAt: CollectedAt - _window - TimeSpan.FromDays(1),
            _indexes,
            _missingIndexes,
            _foreignKeys,
            _columns);

    public static QualifiedName Dbo(string table) => new("dbo", table);

    private static IndexKeyColumn ParseKeyColumn(string key) =>
        key.EndsWith(" DESC", StringComparison.Ordinal)
            ? new IndexKeyColumn(key[..^5], IsDescending: true)
            : new IndexKeyColumn(key, IsDescending: false);
}
