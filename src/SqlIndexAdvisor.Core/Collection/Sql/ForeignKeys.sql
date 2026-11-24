SELECT
    fk.object_id                                    AS ForeignKeyId,
    fk.name                                         AS Name,
    OBJECT_SCHEMA_NAME(fk.parent_object_id)         AS SchemaName,
    OBJECT_NAME(fk.parent_object_id)                AS TableName,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id)     AS ReferencedSchemaName,
    OBJECT_NAME(fk.referenced_object_id)            AS ReferencedTableName
FROM sys.foreign_keys fk
WHERE fk.is_ms_shipped = 0
ORDER BY SchemaName, TableName, Name;
