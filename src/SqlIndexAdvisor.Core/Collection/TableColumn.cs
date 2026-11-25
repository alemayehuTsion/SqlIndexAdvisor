namespace SqlIndexAdvisor.Core.Collection;

public sealed record TableColumn(
    QualifiedName Table,
    string Name,
    string TypeName,
    int MaxLengthBytes,
    bool IsNullable,
    bool IsComputed)
{
    public bool IsMaxLength => MaxLengthBytes == -1;
}
