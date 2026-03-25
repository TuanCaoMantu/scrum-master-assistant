namespace ScrumMaster.API.Models;

// ── Request ──────────────────────────────────────────────────────────────────

public record InsightReportRequest(
    string TimeRange,    // "30m" | "1h" | "4h" | "6h" | "12h" | "24h" | "3d" | "7d" | "30d"
    List<string> Roles   // e.g. ["SMARTX", "Need.Api", "Candidate.API"]
);

// ── Response ──────────────────────────────────────────────────────────────────
public interface IReportResult {}

public record InsightReport(
    string TimeRange,
    string GeneratedAt,
    List<FailedRequestItem>    FailedRequests,
    List<FailedDependencyItem> FailedDependencies,
    List<ExceptionItem>        Exceptions
);

public record FailedRequestItem(
    string Operation,
    int    TotalCount,
    int    FailedCount,
    double FailPct,
    int    Users,
    string RootCause
) : IReportResult;

public record FailedDependencyItem(
    string Name,
    string DependencyType,
    int    TotalCount,
    int    FailedCount,
    double AvgDurationMs,
    string RootCause
) : IReportResult;

public record ExceptionItem(
    string ExceptionType,
    string OuterMessage,
    string ProblemId,
    int    Count,
    int    AffectedUsers,
    string RootCause
) : IReportResult;
