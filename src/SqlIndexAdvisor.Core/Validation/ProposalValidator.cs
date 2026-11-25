using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Core.Validation;

// Re-checks every proposed change against the snapshot without reusing rule logic, so a bug in a rule cannot
// switch off the check that should catch it.
public sealed class ProposalValidator
{
    public IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var proposals = findings.Where(finding => finding.ProposedChange is not null).ToList();

        return proposals
            .Select(finding => new ProposalValidation(finding, Check(snapshot, proposals, finding.ProposedChange!).ToList()))
            .ToList();
    }

    private static IEnumerable<ValidationIssue> Check(DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, IndexChange change)
    {
        if (!snapshot.Columns.Any(column => column.Table == change.Table))
        {
            return [ValidationIssue.Error("table-not-found", $"Table {change.Table} does not exist in {snapshot.DatabaseName}.")];
        }

        return change switch
        {
            CreateIndex create => CheckCreate(snapshot, proposals, create),
            DropIndex drop => CheckDrop(snapshot, drop),
            _ => [ValidationIssue.Error("unknown-change", $"Unsupported change type {change.GetType().Name}.")],
        };
    }

    private const int MaxKeyColumns = 32;
    private const int MaxNonclusteredKeyBytes = 1700;

    private static readonly HashSet<string> TypesNotAllowedInKey =
        new(["text", "ntext", "image", "xml", "geography", "geometry"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TypesNotAllowedInInclude =
        new(["text", "ntext", "image"], StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<ValidationIssue> CheckCreate(
        DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, CreateIndex create)
    {
        var tableColumns = snapshot.Columns
            .Where(column => column.Table == create.Table)
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. CheckColumnsExist(create, tableColumns),
            .. CheckNoColumnRepeated(create),
            .. CheckKeyColumns(create, tableColumns),
            .. CheckIncludedColumns(create, tableColumns),
            .. CheckName(snapshot, proposals, create),
        ];
    }

    private static IEnumerable<ValidationIssue> CheckColumnsExist(
        CreateIndex create, IReadOnlyDictionary<string, TableColumn> tableColumns) =>
        ColumnsOf(create)
            .Where(column => !tableColumns.ContainsKey(column))
            .Select(column => ValidationIssue.Error("column-not-found", $"Column {column} does not exist on {create.Table}."));

    private static IEnumerable<ValidationIssue> CheckNoColumnRepeated(CreateIndex create) =>
        ColumnsOf(create)
            .GroupBy(column => column, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => ValidationIssue.Error(
                "column-repeated", $"Column {group.Key} appears {group.Count()} times in the proposed index."));

    private static IEnumerable<ValidationIssue> CheckKeyColumns(
        CreateIndex create, IReadOnlyDictionary<string, TableColumn> tableColumns)
    {
        if (create.KeyColumns.Count > MaxKeyColumns)
        {
            yield return ValidationIssue.Error(
                "too-many-key-columns", $"{create.KeyColumns.Count} key columns; SQL Server allows {MaxKeyColumns}.");
        }

        var keyColumns = KnownColumns(create.KeyColumns.Select(key => key.Name), tableColumns).ToList();

        foreach (var column in keyColumns.Where(column => column.IsMaxLength || TypesNotAllowedInKey.Contains(column.TypeName)))
        {
            yield return ValidationIssue.Error(
                "key-type-not-allowed", $"Column {column.Name} ({Describe(column)}) cannot be an index key.");
        }

        var keyBytes = keyColumns.Where(column => !column.IsMaxLength).Sum(column => column.MaxLengthBytes);
        if (keyBytes > MaxNonclusteredKeyBytes)
        {
            yield return ValidationIssue.Error(
                "key-too-wide",
                $"Key columns add up to {keyBytes} bytes; SQL Server allows {MaxNonclusteredKeyBytes} for a nonclustered index.");
        }
    }

    private static IEnumerable<ValidationIssue> CheckIncludedColumns(
        CreateIndex create, IReadOnlyDictionary<string, TableColumn> tableColumns) =>
        KnownColumns(create.IncludedColumns, tableColumns)
            .Where(column => TypesNotAllowedInInclude.Contains(column.TypeName))
            .Select(column => ValidationIssue.Error(
                "include-type-not-allowed", $"Column {column.Name} ({Describe(column)}) cannot be an included column."));

    private static IEnumerable<TableColumn> KnownColumns(
        IEnumerable<string> names, IReadOnlyDictionary<string, TableColumn> tableColumns) =>
        names.Where(tableColumns.ContainsKey).Select(name => tableColumns[name]);

    private static string Describe(TableColumn column) =>
        column.IsMaxLength ? $"{column.TypeName}(max)" : column.TypeName;

    private static IEnumerable<ValidationIssue> CheckName(
        DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, CreateIndex create)
    {
        if (string.IsNullOrWhiteSpace(create.IndexName))
        {
            yield return ValidationIssue.Error("name-empty", "The proposed index has no name.");
            yield break;
        }

        if (create.IndexName.Length > SqlIdentifier.MaxLength)
        {
            yield return ValidationIssue.Error(
                "name-too-long",
                $"{create.IndexName} is {create.IndexName.Length} characters; SQL Server allows {SqlIdentifier.MaxLength}.");
        }

        if (snapshot.Indexes.Any(index => index.Table == create.Table && SameName(index.Name, create.IndexName)))
        {
            yield return ValidationIssue.Error("name-taken", $"An index named {create.IndexName} already exists on {create.Table}.");
        }

        var proposalsWithSameName = proposals
            .Select(finding => finding.ProposedChange)
            .OfType<CreateIndex>()
            .Count(other => other.Table == create.Table && SameName(other.IndexName, create.IndexName));
        if (proposalsWithSameName > 1)
        {
            yield return ValidationIssue.Error(
                "name-not-unique", $"{proposalsWithSameName} proposals would create an index named {create.IndexName} on {create.Table}.");
        }
    }

    private static IEnumerable<ValidationIssue> CheckDrop(DatabaseSnapshot snapshot, DropIndex drop)
    {
        if (!snapshot.Indexes.Any(index => index.Table == drop.Table && SameName(index.Name, drop.IndexName)))
        {
            yield return ValidationIssue.Error("index-not-found", $"There is no index {drop.IndexName} on {drop.Table} to drop.");
        }
    }

    private static IEnumerable<string> ColumnsOf(CreateIndex create) =>
        create.KeyColumns.Select(key => key.Name).Concat(create.IncludedColumns);

    private static bool SameName(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
