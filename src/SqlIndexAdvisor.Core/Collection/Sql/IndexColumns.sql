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
