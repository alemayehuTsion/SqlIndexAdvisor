using System.Data;
using System.Data.Common;

using Dapper;

namespace SqlIndexAdvisor.Core.Collection;

public sealed class DatabaseCollector(DbConnection connection)
{
    private static readonly string ServerContextSql = SqlScript.Load("ServerContext");
    private static readonly string IndexesSql = SqlScript.Load("Indexes");
    private static readonly string IndexColumnsSql = SqlScript.Load("IndexColumns");
    private static readonly string MissingIndexesSql = SqlScript.Load("MissingIndexes");
    private static readonly string MissingIndexColumnsSql = SqlScript.Load("MissingIndexColumns");
    private static readonly string ForeignKeysSql = SqlScript.Load("ForeignKeys");
    private static readonly string ForeignKeyColumnsSql = SqlScript.Load("ForeignKeyColumns");
    private static readonly string ColumnsSql = SqlScript.Load("Columns");

    public async Task<DatabaseSnapshot> CollectSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var openedHere = connection.State == ConnectionState.Closed;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var context = await connection.QuerySingleAsync<ServerContextRow>(
                new CommandDefinition(ServerContextSql, cancellationToken: cancellationToken));

            return new DatabaseSnapshot(
                context.DatabaseName,
                context.CollectedAt,
                context.ServerStartTime,
                context.DatabaseCreatedAt,
                await CollectIndexesAsync(cancellationToken),
                await CollectMissingIndexesAsync(cancellationToken),
                await CollectForeignKeysAsync(cancellationToken),
                await CollectColumnsAsync(cancellationToken));
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<IReadOnlyList<IndexInfo>> CollectIndexesAsync(CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<MissingIndex>> CollectMissingIndexesAsync(CancellationToken cancellationToken)
    {
        var suggestions = await connection.QueryAsync<MissingIndexRow>(
            new CommandDefinition(MissingIndexesSql, cancellationToken: cancellationToken));

        var columnsBySuggestion = (await connection.QueryAsync<MissingIndexColumnRow>(
                new CommandDefinition(MissingIndexColumnsSql, cancellationToken: cancellationToken)))
            .ToLookup(column => column.IndexHandle);

        return suggestions
            .Select(suggestion => ToMissingIndex(suggestion, columnsBySuggestion[suggestion.IndexHandle]))
            .ToList();
    }

    private async Task<IReadOnlyList<ForeignKey>> CollectForeignKeysAsync(CancellationToken cancellationToken)
    {
        var foreignKeys = await connection.QueryAsync<ForeignKeyRow>(
            new CommandDefinition(ForeignKeysSql, cancellationToken: cancellationToken));

        var columnsByForeignKey = (await connection.QueryAsync<ForeignKeyColumnRow>(
                new CommandDefinition(ForeignKeyColumnsSql, cancellationToken: cancellationToken)))
            .ToLookup(column => column.ForeignKeyId);

        return foreignKeys
            .Select(foreignKey => new ForeignKey(
                foreignKey.Name,
                new QualifiedName(foreignKey.SchemaName, foreignKey.TableName),
                columnsByForeignKey[foreignKey.ForeignKeyId].Select(column => column.ColumnName).ToList(),
                new QualifiedName(foreignKey.ReferencedSchemaName, foreignKey.ReferencedTableName)))
            .ToList();
    }

    private async Task<IReadOnlyList<TableColumn>> CollectColumnsAsync(CancellationToken cancellationToken)
    {
        var columns = await connection.QueryAsync<ColumnRow>(
            new CommandDefinition(ColumnsSql, cancellationToken: cancellationToken));

        return columns
            .Select(column => new TableColumn(
                new QualifiedName(column.SchemaName, column.TableName),
                column.ColumnName,
                column.TypeName,
                column.MaxLengthBytes,
                column.IsNullable,
                column.IsComputed))
            .ToList();
    }

    private static MissingIndex ToMissingIndex(MissingIndexRow suggestion, IEnumerable<MissingIndexColumnRow> columns)
    {
        IReadOnlyList<string> ColumnsUsedAs(string usage) =>
            columns.Where(column => column.ColumnUsage == usage).Select(column => column.ColumnName).ToList();

        return new(
            new QualifiedName(suggestion.SchemaName, suggestion.TableName),
            ColumnsUsedAs("EQUALITY"),
            ColumnsUsedAs("INEQUALITY"),
            ColumnsUsedAs("INCLUDE"),
            suggestion.UserSeeks,
            suggestion.AverageTotalUserCost,
            suggestion.AverageUserImpactPercent);
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
            new IndexUsage(index.UserSeeks, index.UserScans, index.UserLookups, index.UserUpdates, index.LeafWrites),
            index.IsDisabled,
            index.FilterDefinition);

    // Dapper maps rows onto these records by constructor position, so the column order and SQL types in
    // Collection/Sql/*.sql must match the parameters exactly.
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
        long LeafWrites,
        bool IsDisabled,
        string? FilterDefinition);

    private sealed record IndexColumnRow(
        int ObjectId,
        int IndexId,
        string ColumnName,
        int KeyOrdinal,
        bool IsDescending,
        bool IsIncluded);

    private sealed record MissingIndexRow(
        int IndexHandle,
        string SchemaName,
        string TableName,
        long UserSeeks,
        double AverageTotalUserCost,
        double AverageUserImpactPercent);

    private sealed record MissingIndexColumnRow(
        int IndexHandle,
        string ColumnName,
        string ColumnUsage);

    private sealed record ServerContextRow(
        string DatabaseName,
        DateTimeOffset CollectedAt,
        DateTimeOffset ServerStartTime,
        DateTimeOffset DatabaseCreatedAt);

    private sealed record ForeignKeyRow(
        int ForeignKeyId,
        string Name,
        string SchemaName,
        string TableName,
        string ReferencedSchemaName,
        string ReferencedTableName);

    private sealed record ForeignKeyColumnRow(
        int ForeignKeyId,
        string ColumnName);

    private sealed record ColumnRow(
        string SchemaName,
        string TableName,
        string ColumnName,
        string TypeName,
        int MaxLengthBytes,
        bool IsNullable,
        bool IsComputed);
}
