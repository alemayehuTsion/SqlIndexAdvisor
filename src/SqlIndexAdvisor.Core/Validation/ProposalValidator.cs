using SqlIndexAdvisor.Core.Collection;
using SqlIndexAdvisor.Core.Rules;

namespace SqlIndexAdvisor.Core.Validation;

// Re-checks every proposed change against the snapshot without reusing rule logic, so a bug in a rule cannot
// switch off the check that should catch it.
public sealed class ProposalValidator(ValidatorOptions? options = null)
{
    private readonly ValidatorOptions _options = options ?? new();

    // Errors depend only on the snapshot, so they are decided first. Warnings judge the index set that results from
    // applying every accepted change together; rejected changes never count towards that result.
    public IReadOnlyList<ProposalValidation> Validate(DatabaseSnapshot snapshot, IReadOnlyList<Finding> findings)
    {
        var proposals = findings.Where(finding => finding.ProposedChange is not null).ToList();
        var checkedProposals = proposals
            .Select(finding => (Finding: finding, Errors: CheckErrors(snapshot, proposals, finding.ProposedChange!).ToList()))
            .ToList();

        var acceptedChanges = checkedProposals
            .Where(proposal => proposal.Errors.Count == 0)
            .Select(proposal => proposal.Finding.ProposedChange!)
            .ToList();
        var projectedIndexes = ProjectIndexes(snapshot, acceptedChanges);

        return checkedProposals
            .Select(proposal => new ProposalValidation(
                proposal.Finding,
                proposal.Errors.Count > 0
                    ? proposal.Errors
                    : CheckWarnings(snapshot, projectedIndexes, proposal.Finding.ProposedChange!).ToList()))
            .ToList();
    }

    private static IEnumerable<ValidationIssue> CheckErrors(
        DatabaseSnapshot snapshot, IReadOnlyList<Finding> proposals, IndexChange change)
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

    private static IReadOnlyList<IndexInfo> ProjectIndexes(DatabaseSnapshot snapshot, IReadOnlyList<IndexChange> acceptedChanges)
    {
        var drops = acceptedChanges.OfType<DropIndex>().ToList();
        var remaining = snapshot.Indexes
            .Where(index => !drops.Any(drop => drop.Table == index.Table && SameName(drop.IndexName, index.Name)));
        var created = acceptedChanges.OfType<CreateIndex>()
            .Select(create => new IndexInfo(
                create.Table, create.IndexName, IsClustered: false, IsUnique: false, IsPrimaryKey: false,
                create.KeyColumns, create.IncludedColumns, new IndexUsage(0, 0, 0, 0, 0), IsDisabled: false, FilterDefinition: null));

        return [.. remaining, .. created];
    }

    private IEnumerable<ValidationIssue> CheckWarnings(
        DatabaseSnapshot snapshot, IReadOnlyList<IndexInfo> projectedIndexes, IndexChange change) =>
        change switch
        {
            CreateIndex create => CheckIndexCount(projectedIndexes, create),
            DropIndex drop => CheckForeignKeysStaySupported(snapshot, projectedIndexes, drop),
            _ => [],
        };

    private IEnumerable<ValidationIssue> CheckIndexCount(IReadOnlyList<IndexInfo> projectedIndexes, CreateIndex create)
    {
        var count = projectedIndexes.Count(index => index.Table == create.Table && !index.IsClustered && !index.IsDisabled);

        if (count > _options.MaxNonclusteredIndexesPerTable)
        {
            yield return ValidationIssue.Warning(
                "too-many-indexes",
                $"{create.Table} would have {count} nonclustered indexes " +
                $"(guideline {_options.MaxNonclusteredIndexesPerTable}); every index slows down writes.");
        }
    }

    private static IEnumerable<ValidationIssue> CheckForeignKeysStaySupported(
        DatabaseSnapshot snapshot, IReadOnlyList<IndexInfo> projectedIndexes, DropIndex drop)
    {
        var dropped = snapshot.Indexes.Single(index => index.Table == drop.Table && SameName(index.Name, drop.IndexName));

        var foreignKeysLosingSupport = snapshot.ForeignKeys
            .Where(foreignKey => foreignKey.Table == drop.Table && SupportsForeignKey(dropped, foreignKey))
            .Where(foreignKey => !projectedIndexes.Any(index => index.Table == drop.Table && SupportsForeignKey(index, foreignKey)));

        foreach (var foreignKey in foreignKeysLosingSupport)
        {
            yield return ValidationIssue.Warning(
                "drop-unindexes-foreign-key",
                $"After the proposed changes no index supports foreign key {foreignKey.Name}; " +
                $"changes to {foreignKey.ReferencedTable} would have to scan {foreignKey.Table}.");
        }
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
        var index = snapshot.Indexes.FirstOrDefault(index => index.Table == drop.Table && SameName(index.Name, drop.IndexName));
        if (index is null)
        {
            yield return ValidationIssue.Error("index-not-found", $"There is no index {drop.IndexName} on {drop.Table} to drop.");
            yield break;
        }

        if (index.IsPrimaryKey || index.IsUnique || index.IsClustered)
        {
            yield return ValidationIssue.Error(
                "drop-protected",
                $"{index.Name} is a primary key, unique or clustered index; dropping it changes constraints or the table's storage.");
        }
    }

    private static bool SupportsForeignKey(IndexInfo index, ForeignKey foreignKey) =>
        !index.IsDisabled
        && index.FilterDefinition is null
        && index.KeyColumns.Count >= foreignKey.Columns.Count
        && index.KeyColumns
            .Take(foreignKey.Columns.Count)
            .Select(key => key.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(foreignKey.Columns);

    private static IEnumerable<string> ColumnsOf(CreateIndex create) =>
        create.KeyColumns.Select(key => key.Name).Concat(create.IncludedColumns);

    private static bool SameName(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
