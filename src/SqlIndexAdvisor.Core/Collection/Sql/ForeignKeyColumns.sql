SELECT
    fkc.constraint_object_id    AS ForeignKeyId,
    c.name                      AS ColumnName
FROM sys.foreign_key_columns fkc
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
ORDER BY fkc.constraint_object_id, fkc.constraint_column_id;
