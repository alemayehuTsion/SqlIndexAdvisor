-- max_length is in bytes (nvarchar(256) reports 512) and -1 for (max) types. Alias types such as sysname are
-- resolved to their base type; CLR types (geography, hierarchyid) keep their own name because they share a
-- base type id.
SELECT
    s.name                                                  AS SchemaName,
    t.name                                                  AS TableName,
    c.name                                                  AS ColumnName,
    CASE
        WHEN ty.is_user_defined = 1 AND ty.is_assembly_type = 0 THEN TYPE_NAME(c.system_type_id)
        ELSE ty.name
    END                                                     AS TypeName,
    CAST(c.max_length AS int)                               AS MaxLengthBytes,
    c.is_nullable                                           AS IsNullable,
    c.is_computed                                           AS IsComputed
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name, c.column_id;
