-- The missing-index DMVs are server-wide, so this and MissingIndexColumns.sql filter on the current database.
SELECT
    d.index_handle                      AS IndexHandle,
    OBJECT_SCHEMA_NAME(d.object_id)     AS SchemaName,
    OBJECT_NAME(d.object_id)            AS TableName,
    s.user_seeks                        AS UserSeeks,
    s.avg_total_user_cost               AS AverageTotalUserCost,
    s.avg_user_impact                   AS AverageUserImpactPercent
FROM sys.dm_db_missing_index_details d
JOIN sys.dm_db_missing_index_groups g ON g.index_handle = d.index_handle
JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle = g.index_group_handle
WHERE d.database_id = DB_ID()
ORDER BY d.index_handle;
