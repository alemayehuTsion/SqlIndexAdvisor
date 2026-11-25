namespace SqlIndexAdvisor.Core.Validation;

public sealed record ValidatorOptions
{
    public int MaxNonclusteredIndexesPerTable { get; init; } = 10;
}
