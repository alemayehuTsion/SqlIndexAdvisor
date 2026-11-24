SELECT
    d.index_handle      AS IndexHandle,
    c.column_name       AS ColumnName,
    c.column_usage      AS ColumnUsage
FROM sys.dm_db_missing_index_details d
CROSS APPLY sys.dm_db_missing_index_columns(d.index_handle) c
WHERE d.database_id = DB_ID()
ORDER BY d.index_handle, c.column_id;
