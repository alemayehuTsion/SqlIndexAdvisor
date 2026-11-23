namespace SqlIndexAdvisor.Core.Collection;

public sealed record QualifiedName(string Schema, string Name)
{
    public override string ToString() => $"{SqlIdentifier.Quote(Schema)}.{SqlIdentifier.Quote(Name)}";
}
