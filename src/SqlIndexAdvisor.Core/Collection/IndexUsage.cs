namespace SqlIndexAdvisor.Core.Collection;

public sealed record IndexUsage(
    long UserSeeks,
    long UserScans,
    long UserLookups,
    long UserUpdates,
    long LeafWrites)
{
    public long TotalReads => UserSeeks + UserScans + UserLookups;
}
