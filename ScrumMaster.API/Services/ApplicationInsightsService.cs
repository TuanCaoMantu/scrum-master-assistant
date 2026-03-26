using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace ScrumMaster.API.Services;

public interface IApplicationInsightsService
{
    Task<List<RawFailedRequest>> GetFailedRequestsAsync(string timespan, List<string> roles, CancellationToken ct = default);
    Task<List<RawFailedDependency>> GetFailedDependenciesAsync(string timespan, List<string> roles, CancellationToken ct = default);
    Task<List<RawException>> GetExceptionsAsync(string timespan, List<string> roles, CancellationToken ct = default);
    Task<List<RawHealthCheckItem>> GetHealthCheckAsync(string timespan, List<string> roles, AppTableType? type = null, int take = 500, CancellationToken ct = default);
}

public class ApplicationInsightsService : IApplicationInsightsService
{
    private readonly LogsQueryClient _client;
    private readonly string _workspaceId;
    private readonly ILogger<ApplicationInsightsService> _logger;

    public ApplicationInsightsService(IConfiguration config, ILogger<ApplicationInsightsService> logger)
    {
        _logger = logger;
        _workspaceId = config["APPINSIGHTS_WORKSPACE_ID"]
                    ?? throw new InvalidOperationException(
                        "APPINSIGHTS_WORKSPACE_ID is not configured.");

        _client = new LogsQueryClient(new DefaultAzureCredential());
    }

    public async Task<List<RawFailedRequest>> GetFailedRequestsAsync(
        string timespan, List<string> roles, CancellationToken ct = default)
    {
        var roleFilter = BuildRoleFilter(roles);
        var ago = ValidateAgo(timespan);
        var query = $"""
        let LatestFailed =
        AppRequests
        | where TimeGenerated > ago({ago})
        {roleFilter}
        | where Success == false
        | summarize arg_max(TimeGenerated, OperationId, _ItemId) by Name
        | project Name, LatestOperationId = OperationId, EventId = _ItemId, LatestFailedTime = TimeGenerated;
        AppRequests
        | where TimeGenerated > ago({ago})
        {roleFilter}
        | summarize
            Count       = count(),
            CountFailed = countif(Success == false),
            Users       = dcount(UserId)
        by Name
        | extend FailPct = round(todouble(CountFailed) / Count * 100, 1)
        | join kind=leftouter LatestFailed on Name
        | project-away Name1
        | order by CountFailed desc;
        """;

        var rows = await ExecuteQueryAsync(query, timespan, ct);
        return rows.ConvertAll(r => new RawFailedRequest(
            Operation: GetStr(r, "Name"),
            TotalCount: GetInt(r, "Count"),
            FailedCount: GetInt(r, "CountFailed"),
            FailPct: GetDouble(r, "FailPct"),
            EventId: GetStr(r, "EventId"),
            Users: GetInt(r, "Users"),
            LastFailedTime: GetStr(r, "LatestFailedTime")
        ));
    }

    public async Task<List<RawFailedDependency>> GetFailedDependenciesAsync(
        string timespan, List<string> roles, CancellationToken ct = default)
    {
        var roleFilter = BuildRoleFilter(roles);
        var ago = ValidateAgo(timespan);
        var query = $"""
        let LatestFailed =
            AppDependencies
            | where TimeGenerated > ago({ago})
            {roleFilter}
            | where Success == false
            | summarize arg_max(TimeGenerated, OperationId, _ItemId) by Name, DependencyType
            | project Name, DependencyType, LatestOperationId = OperationId, EventId = _ItemId, LatestFailedTime = TimeGenerated;
        AppDependencies
        | where TimeGenerated > ago({ago})
        {roleFilter}
        | summarize
            Count       = count(),
            CountFailed = countif(Success == false),
            AvgDuration = round(avg(DurationMs), 0)
        by Name, DependencyType
        | join kind=leftouter LatestFailed on Name, DependencyType
        | order by CountFailed desc;
        """;

        var rows = await ExecuteQueryAsync(query, timespan, ct);
        return rows.ConvertAll(r => new RawFailedDependency(
            Name: GetStr(r, "Name"),
            DependencyType: GetStr(r, "DependencyType"),
            TotalCount: GetInt(r, "Count"),
            FailedCount: GetInt(r, "CountFailed"),
            AvgDurationMs: GetDouble(r, "AvgDuration"),
            EventId: GetStr(r, "EventId"),
            LastFailedTime: GetStr(r, "LatestFailedTime")
        ));
    }

    public async Task<List<RawException>> GetExceptionsAsync(
        string timespan, List<string> roles, CancellationToken ct = default)
    {
        var roleFilter = BuildRoleFilter(roles);
        var ago = ValidateAgo(timespan);
        var query = $"""
        let LatestFailed =
            AppExceptions
            | where TimeGenerated > ago({ago})
            {roleFilter}
            | summarize arg_max(TimeGenerated, OperationId, _ItemId) by ExceptionType, OuterMessage, ProblemId
            | project ExceptionType, OuterMessage, ProblemId, LatestOperationId = OperationId, EventId = _ItemId, LatestFailedTime = TimeGenerated;
        AppExceptions
        | where TimeGenerated > ago({ago})
        {roleFilter}
        | summarize
            Count         = count(),
            AffectedUsers = dcount(UserId)
        by ExceptionType, OuterMessage, ProblemId
        | join kind=leftouter LatestFailed on ExceptionType, OuterMessage, ProblemId
        | order by Count desc;
        """;

        var rows = await ExecuteQueryAsync(query, timespan, ct);
        return rows.ConvertAll(r => new RawException(
            ExceptionType: GetStr(r, "ExceptionType"),
            OuterMessage: GetStr(r, "OuterMessage"),
            ProblemId: GetStr(r, "ProblemId"),
            Count: GetInt(r, "Count"),
            AffectedUsers: GetInt(r, "AffectedUsers"),
            EventId: GetStr(r, "EventId"),
            LastFailedTime: GetStr(r, "LatestFailedTime")
        ));
    }

    public async Task<List<RawHealthCheckItem>> GetHealthCheckAsync(
        string timespan, List<string> roles, AppTableType? type = null, int take = 500, CancellationToken ct = default)
    {
        var roleFilter = BuildRoleFilter(roles);
        var ago = ValidateAgo(timespan);
        var typeFilter = type.HasValue ? $"| where Type == \"{type.Value}\"" : "| where Type in (\"AppRequests\", \"AppDependencies\", \"AppExceptions\")";
        var query = $"""
        let Stats =
            union AppRequests, AppDependencies, AppExceptions
            | where TimeGenerated > ago({ago})
            {roleFilter}
            | where Success == false
            {typeFilter}
            | summarize Count = count(), AffectedUsers = dcount(UserId) by Name, Type;
        union AppRequests, AppDependencies, AppExceptions
        | where TimeGenerated > ago({ago})
        {roleFilter}
        | where Success == false
        {typeFilter}
        | project
            Id,
            TimeGenerated,
            AppRoleName,
            ResourceId = _ResourceId,
            Type,
            Name,
            ResultCode = tostring(ResultCode),
            OperationName,
            OperationId,
            UserId,
            UserAuthenticatedId,
            ItemId = tostring(_ItemId)
        | join kind=leftouter Stats on Name, Type
        | project-away Name1, Type1
        | order by TimeGenerated desc
        | take {take}
        """;

        var rows = await ExecuteQueryAsync(query, timespan, ct);
        return rows.ConvertAll(r => new RawHealthCheckItem(
            Id:                  GetStr(r, "Id"),
            TimeGenerated:       GetStr(r, "TimeGenerated"),
            AppRoleName:         GetStr(r, "AppRoleName"),
            ResourceId:          GetStr(r, "ResourceId"),
            Type:                GetStr(r, "Type"),
            Name:                GetStr(r, "Name"),
            ResultCode:          GetStr(r, "ResultCode"),
            OperationName:       GetStr(r, "OperationName"),
            OperationId:         GetStr(r, "OperationId"),
            UserId:              GetStr(r, "UserId"),
            UserAuthenticatedId: GetStr(r, "UserAuthenticatedId"),
            ItemId:              GetStr(r, "ItemId"),
            Count:               GetInt(r, "Count"),
            AffectedUsers:       GetInt(r, "AffectedUsers")
        ));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task<List<LogsTableRow>> ExecuteQueryAsync(
        string query, string timespan, CancellationToken ct)
    {
        var duration = ToTimeSpan(timespan);
        _logger.LogDebug("KQL query:\n{Query}", query);

        var response = await _client.QueryWorkspaceAsync(
            _workspaceId, query,
            new QueryTimeRange(duration),
            cancellationToken: ct);

        var table = response?.Value?.Table;
        if (table == null)
        {
            _logger.LogWarning("Log Analytics returned null table");
            return [];
        }

        _logger.LogInformation("Log Analytics returned {Count} rows", table.Rows.Count);
        return [.. table.Rows];
    }

    private static TimeSpan ToTimeSpan(string timeRange) => timeRange switch
    {
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "4h" => TimeSpan.FromHours(4),
        "6h" => TimeSpan.FromHours(6),
        "12h" => TimeSpan.FromHours(12),
        "24h" => TimeSpan.FromHours(24),
        "3d" => TimeSpan.FromDays(3),
        "7d" => TimeSpan.FromDays(7),
        "30d" => TimeSpan.FromDays(30),
        _ => TimeSpan.FromHours(24)
    };

    // Valid KQL ago() args match accepted timespan strings; default to "24h" for unknown values.
    private static string ValidateAgo(string timespan) =>
        timespan is "30m" or "1h" or "4h" or "6h" or "12h" or "24h" or "3d" or "7d" or "30d"
            ? timespan : "24h";

    private static string BuildRoleFilter(List<string> roles)
    {
        if (roles.Count == 0) return "";
        var list = string.Join(", ", roles.Select(r => $"\"{r}\""));
        return $"| where AppRoleName in ({list})";
    }

    private static string GetStr(LogsTableRow row, string col)
    {
        try { return row[col]?.ToString() ?? ""; }
        catch { return ""; }
    }

    private static int GetInt(LogsTableRow row, string col)
    {
        try
        {
            return row[col] switch
            {
                long l => (int)l,
                int i => i,
                string s => int.TryParse(s, out var n) ? n : 0,
                _ => 0
            };
        }
        catch { return 0; }
    }

    private static double GetDouble(LogsTableRow row, string col)
    {
        try
        {
            return row[col] switch
            {
                double d => d,
                float f => f,
                long l => l,
                int i => i,
                string s => double.TryParse(s, out var n) ? n : 0,
                _ => 0
            };
        }
        catch { return 0; }
    }
}

// ── Raw DTOs ──────────────────────────────────────────────────────────────────
public record RawFailedRequest(string Operation, int TotalCount, int FailedCount, double FailPct, string EventId, int Users, string LastFailedTime);
public record RawFailedDependency(string Name, string DependencyType, int TotalCount, int FailedCount, double AvgDurationMs, string EventId, string LastFailedTime);
public record RawException(string ExceptionType, string OuterMessage, string ProblemId, int Count, int AffectedUsers, string EventId, string LastFailedTime);
public record RawHealthCheckItem(string Id, string TimeGenerated, string AppRoleName, string ResourceId, string Type, string Name, string ResultCode, string OperationName, string OperationId, string UserId, string UserAuthenticatedId, string ItemId, int Count, int AffectedUsers);

public enum ReportType
{
    FailedRequests,
    FailedDependencies,
    Exceptions
}

public enum AppTableType
{
    AppRequests,
    AppDependencies,
    AppExceptions
}
