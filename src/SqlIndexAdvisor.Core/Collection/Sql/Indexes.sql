-- Rowstore indexes only (type 1 clustered, 2 nonclustered): heaps, columnstore, XML and spatial indexes are out
-- of scope. The usage DMV is LEFT JOINed because an index never touched since restart has no row there.
-- Disabled and filtered indexes are collected with those flags: neither can stand in for a full, usable index.
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
    ISNULL(o.LeafWrites, 0)                  AS LeafWrites,
    i.is_disabled                            AS IsDisabled,
    IIF(i.has_filter = 1, i.filter_definition, NULL) AS FilterDefinition
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
