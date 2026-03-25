namespace ScrumMaster.API.Models;

public record FailedRequestItem(
    string Operation,
    int    TotalCount,
    int    FailedCount,
    double FailPct,
    int    Users,
    string RootCause
);

public record FailedDependencyItem(
    string Name,
    string DependencyType,
    int    TotalCount,
    int    FailedCount,
    double AvgDurationMs,
    string RootCause
);

public record ExceptionItem(
    string ExceptionType,
    string OuterMessage,
    string ProblemId,
    int    Count,
    int    AffectedUsers,
    string RootCause
);
