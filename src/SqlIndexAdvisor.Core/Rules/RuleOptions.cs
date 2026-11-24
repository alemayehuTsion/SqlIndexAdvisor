namespace SqlIndexAdvisor.Core.Rules;

public sealed record RuleOptions
{
    public TimeSpan MinimumStatisticsWindow { get; init; } = TimeSpan.FromDays(7);

    public long MinimumMissingIndexSeeks { get; init; } = 10;
}
