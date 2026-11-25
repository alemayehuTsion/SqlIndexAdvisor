namespace SqlIndexAdvisor.Core.Validation;

public enum ValidationLevel
{
    Warning,
    Error,
}

public sealed record ValidationIssue(string Code, ValidationLevel Level, string Message)
{
    public static ValidationIssue Error(string code, string message) => new(code, ValidationLevel.Error, message);

    public static ValidationIssue Warning(string code, string message) => new(code, ValidationLevel.Warning, message);
}
