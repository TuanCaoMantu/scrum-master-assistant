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

public record HealthCheckItem(
    string Id,
    string TimeGenerated,
    string AppRoleName,
    string ResourceId,
    string Type,
    string Name,
    string ResultCode,
    string OperationName,
    string OperationId,
    string UserId,
    string UserAuthenticatedId,
    string ItemId,
    int    Count,
    int    AffectedUsers,
    string TransactionUrl
);
