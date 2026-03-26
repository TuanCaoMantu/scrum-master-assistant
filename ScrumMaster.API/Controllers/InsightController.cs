using Azure;
using Microsoft.AspNetCore.Mvc;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;
using System.Text.Json;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("insight")]
public class InsightController(
    IApplicationInsightsService appInsights,
    IConfiguration config,
    ILogger<InsightController> logger) : ControllerBase
{
    private readonly string _subscriptionId  = config["ApplicationInsights:SubscriptionId"]  ?? "";
    private readonly string _resourceGroup   = config["ApplicationInsights:ResourceGroup"]   ?? "";
    private readonly string _appInsightsName = config["ApplicationInsights:AppInsightsName"] ?? "";

    // GET /insight/report?timeRange=24h&roles=Candidate.API,Need.Api
    [HttpGet("report")]
    public async Task<IActionResult> GetReport(
        [FromQuery] string timeRange = "24h",
        [FromQuery] string roles = "Candidate.API,Need.Api,JobOffers.API,Candidates-SPA,IMP-SPA,SMARTX",
        [FromQuery] ReportType reportType = ReportType.FailedRequests,
        CancellationToken ct = default)
    {
        var roleList = string.IsNullOrWhiteSpace(roles)
            ? []
            : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        logger.LogInformation("Insight report: timeRange={TimeRange}, roles=[{Roles}]",
            timeRange, string.Join(",", roleList));

        switch (reportType)
        {
            case ReportType.FailedRequests:
                var failedRequests = await SafeFetch(() => appInsights.GetFailedRequestsAsync(timeRange, roleList, ct), "failed requests");
                return Ok(failedRequests.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new FailedRequestItem(
                    Operation: r.Operation,
                    TotalCount: r.TotalCount,
                    FailedCount: r.FailedCount,
                    FailPct: r.FailPct,
                    Users: r.Users,
                    RootCause: BuildTransactionUrl(BuildResourceId(_subscriptionId, _resourceGroup, _appInsightsName), r.EventId, ParseTimestamp(r.LastFailedTime), "requests")
                )));

            case ReportType.FailedDependencies:
                var failedDependencies = await SafeFetch(() => appInsights.GetFailedDependenciesAsync(timeRange, roleList, ct), "failed dependencies");
                return Ok(failedDependencies.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new FailedDependencyItem(
                    Name: r.Name,
                    DependencyType: r.DependencyType,
                    TotalCount: r.TotalCount,
                    FailedCount: r.FailedCount,
                    AvgDurationMs: r.AvgDurationMs,
                    RootCause: BuildTransactionUrl(BuildResourceId(_subscriptionId, _resourceGroup, _appInsightsName), r.EventId, ParseTimestamp(r.LastFailedTime), "dependencies")
                )));

            case ReportType.Exceptions:
                var exceptions = await SafeFetch(() => appInsights.GetExceptionsAsync(timeRange, roleList, ct), "exceptions");
                return Ok(exceptions.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new ExceptionItem(
                    ExceptionType: r.ExceptionType,
                    OuterMessage: r.OuterMessage,
                    ProblemId: r.ProblemId,
                    Count: r.Count,
                    AffectedUsers: r.AffectedUsers,
                    RootCause: BuildTransactionUrl(BuildResourceId(_subscriptionId, _resourceGroup, _appInsightsName), r.EventId, ParseTimestamp(r.LastFailedTime), "exceptions")
                )));

            default:
                return BadRequest("Invalid report type.");
        }
    }

    // GET /insight/health?timeRange=4h&roles=Candidate.API,Need.Api&type=AppDependencies
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(
        [FromQuery] string timeRange = "4h",
        [FromQuery] string roles = "Candidate.API,Candidate.Worker.Service,Need.Api,JobOffers.API,Candidates-SPA,IMP-SPA,SMARTX,InternalJobOffer,JobOffers.Worker.Service,JobOffersV2",
        [FromQuery] AppTableType? type = null,
        [FromQuery] int take = 500,
        CancellationToken ct = default)
    {
        var roleList = string.IsNullOrWhiteSpace(roles)
            ? []
            : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        logger.LogInformation("Health check: timeRange={TimeRange}, roles=[{Roles}], type={Type}",
            timeRange, string.Join(",", roleList), type?.ToString() ?? "all");

        var items = await SafeFetch(() => appInsights.GetHealthCheckAsync(timeRange, roleList, type, take, ct), "health check");
        
        return Ok(items.Select(r => new HealthCheckItem(
            Id:                  r.Id,
            TimeGenerated:       r.TimeGenerated,
            AppRoleName:         r.AppRoleName,
            ResourceId:          r.ResourceId,
            Type:                r.Type,
            Name:                r.Name,
            ResultCode:          r.ResultCode,
            OperationName:       r.OperationName,
            OperationId:         r.OperationId,
            UserId:              r.UserId,
            UserAuthenticatedId: r.UserAuthenticatedId,
            ItemId:              r.ItemId,
            Count:               r.Count,
            AffectedUsers:       r.AffectedUsers,
            TransactionUrl:      string.IsNullOrEmpty(r.ItemId) ? "" :
                BuildTransactionUrl(r.ResourceId, r.ItemId,
                    ParseTimestamp(r.TimeGenerated),
                    ToEventTable(r.Type))
        )));
    }

    // ── Safe fetch ────────────────────────────────────────────────────────────

    private async Task<List<T>> SafeFetch<T>(Func<Task<List<T>>> fetch, string table)
    {
        try { return await fetch(); }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            logger.LogWarning("KQL error on table '{Table}': {Msg}", table, ex.Message);
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch '{Table}'", table);
            return [];
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Parses an Azure resource ID of the form:
    // /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Insights/components/{name}
    // Returns the three components needed for BuildTransactionUrl, or falls back to the
    // configured values when the resource ID is absent or malformed.
    private (string Sub, string Rg, string Name) ParseResourceId(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 8
            && parts[0].Equals("subscriptions",  StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase)
            && parts[6].Equals("components",     StringComparison.OrdinalIgnoreCase))
        {
            return (parts[1], parts[3], parts[7]);
        }
        return (_subscriptionId, _resourceGroup, _appInsightsName);
    }

    private static string BuildResourceId(string subscriptionId, string resourceGroup, string appInsightsName) =>
        $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Insights/components/{appInsightsName}";

    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : DateTime.UtcNow;

    // Maps the KQL table name (as returned in the Type column of a union) to the
    // portal's eventTable identifier used in the DetailsV2Blade deep-link.
    private static string ToEventTable(string appTableType) => appTableType switch
    {
        "AppDependencies" => "dependencies",
        "AppExceptions"   => "exceptions",
        _                 => "requests"
    };

    private string BuildTransactionUrl(string resource, string eventId, DateTime timestamp, string eventTable = "requests")
    {
        var (subscriptionId, resourceGroup, appInsightsName) = ParseResourceId(resource);

        var componentId = JsonSerializer.Serialize(new
        {
            SubscriptionId = subscriptionId,
            ResourceGroup = resourceGroup,
            Name = appInsightsName,
            LinkedApplicationType = 0,
            ResourceId = Uri.EscapeDataString(resource),
            ResourceType = "microsoft.insights/components",
            IsAzureFirst = false
        });

        var dataModel = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            eventTable
        });

        return $"https://portal.azure.com/#view/AppInsightsExtension/DetailsV2Blade/ComponentId~/{Uri.EscapeDataString(componentId)}/DataModel~/{Uri.EscapeDataString(dataModel)}";
    }
}
