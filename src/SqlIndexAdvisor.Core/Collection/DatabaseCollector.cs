using System.Data.Common;

using Dapper;

namespace SqlIndexAdvisor.Core.Collection;

public sealed class DatabaseCollector(DbConnection connection)
{
    public async Task<IReadOnlyList<IndexInfo>> CollectIndexesAsync(CancellationToken cancellationToken = default)
    {
        var indexes = await connection.QueryAsync<IndexRow>(
            new CommandDefinition(IndexesSql, cancellationToken: cancellationToken));

        var columnsByIndex = (await connection.QueryAsync<IndexColumnRow>(
                new CommandDefinition(IndexColumnsSql, cancellationToken: cancellationToken)))
            .ToLookup(column => (column.ObjectId, column.IndexId));

        return indexes
            .Select(index => ToIndexInfo(index, columnsByIndex[(index.ObjectId, index.IndexId)]))
            .ToList();
    }

    private static IndexInfo ToIndexInfo(IndexRow index, IEnumerable<IndexColumnRow> columns) =>
        new(
            new QualifiedName(index.SchemaName, index.TableName),
            index.IndexName,
            index.IsClustered,
            index.IsUnique,
            index.IsPrimaryKey,
            columns
                .Where(column => !column.IsIncluded)
                .OrderBy(column => column.KeyOrdinal)
                .Select(column => new IndexKeyColumn(column.ColumnName, column.IsDescending))
                .ToList(),
            columns
                .Where(column => column.IsIncluded)
                .Select(column => column.ColumnName)
                .ToList(),
            new IndexUsage(index.UserSeeks, index.UserScans, index.UserLookups, index.UserUpdates, index.LeafWrites));

    // Dapper maps rows onto these records by constructor position, so the column order and SQL types in the
    // queries below must match the parameters exactly.
    private sealed record IndexRow(
        int ObjectId,
        int IndexId,
        string SchemaName,
        string TableName,
        string IndexName,
        bool IsClustered,
        bool IsUnique,
        bool IsPrimaryKey,
        long UserSeeks,
        long UserScans,
        long UserLookups,
        long UserUpdates,
        long LeafWrites);

    private sealed record IndexColumnRow(
        int ObjectId,
        int IndexId,
        string ColumnName,
        int KeyOrdinal,
        bool IsDescending,
        bool IsIncluded);

    // Rowstore indexes only (type 1 clustered, 2 nonclustered): heaps, columnstore, XML and spatial indexes are out
    // of scope. The usage DMV is LEFT JOINed because an index never touched since restart has no row there.
    private const string IndexesSql =
        """
        SELECT
            i.object_id                              AS ObjectId,
            i.index_id                               AS IndexId,
            s.name                                   AS SchemaName,
            t.name                                   AS TableName,
            i.name                                   AS IndexName,
            CAST(IIF(i.type = 1, 1, 0) AS bit)       AS IsClustered,
            i.is_unique                              AS IsUnique,
            i.is_primary_key                         AS IsPrimaryKey,
            ISNULL(u.user_seeks, 0)                  AS UserSeeks,
            ISNULL(u.user_scans, 0)                  AS UserScans,
            ISNULL(u.user_lookups, 0)                AS UserLookups,
            ISNULL(u.user_updates, 0)                AS UserUpdates,
            ISNULL(o.LeafWrites, 0)                  AS LeafWrites
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.dm_db_index_usage_stats u
            ON u.database_id = DB_ID() AND u.object_id = i.object_id AND u.index_id = i.index_id
        OUTER APPLY
        (
            SELECT SUM(op.leaf_insert_count + op.leaf_update_count + op.leaf_delete_count) AS LeafWrites
            FROM sys.dm_db_index_operational_stats(DB_ID(), i.object_id, i.index_id, NULL) op
        ) o
        WHERE i.type IN (1, 2)
          AND i.is_hypothetical = 0
          AND t.is_ms_shipped = 0
        ORDER BY s.name, t.name, i.index_id;
        """;

    private const string IndexColumnsSql =
        """
        SELECT
            ic.object_id                    AS ObjectId,
            ic.index_id                     AS IndexId,
            c.name                          AS ColumnName,
            CAST(ic.key_ordinal AS int)     AS KeyOrdinal,
            ic.is_descending_key            AS IsDescending,
            ic.is_included_column           AS IsIncluded
        FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.tables t ON t.object_id = ic.object_id
        WHERE i.type IN (1, 2)
          AND i.is_hypothetical = 0
          AND t.is_ms_shipped = 0
          AND (ic.key_ordinal > 0 OR ic.is_included_column = 1)
        ORDER BY ic.object_id, ic.index_id, ic.key_ordinal;
        """;
}
