namespace SqlIndexAdvisor.Core.Advice;

public sealed record Assessment(string FindingId, int Rank, string Explanation, string Risks);
