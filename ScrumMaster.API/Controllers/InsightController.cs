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
    ILogger<InsightController> logger) : ControllerBase
{
    private const string SubscriptionId   = "3e45e1dd-7bc1-4750-85fc-1827620be83a";
    private const string ResourceGroup    = "rg-erp";
    private const string AppInsightsName  = "appi-marketplace-prod";

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
                    RootCause: BuildTransactionUrl(SubscriptionId, ResourceGroup, AppInsightsName, r.EventId, DateTime.Parse(r.LastFailedTime))
                )));

            case ReportType.FailedDependencies:
                var failedDependencies = await SafeFetch(() => appInsights.GetFailedDependenciesAsync(timeRange, roleList, ct), "failed dependencies");
                return Ok(failedDependencies.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new FailedDependencyItem(
                    Name: r.Name,
                    DependencyType: r.DependencyType,
                    TotalCount: r.TotalCount,
                    FailedCount: r.FailedCount,
                    AvgDurationMs: r.AvgDurationMs,
                    RootCause: BuildTransactionUrl(SubscriptionId, ResourceGroup, AppInsightsName, r.EventId, DateTime.Parse(r.LastFailedTime))
                )));

            case ReportType.Exceptions:
                var exceptions = await SafeFetch(() => appInsights.GetExceptionsAsync(timeRange, roleList, ct), "exceptions");
                return Ok(exceptions.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new ExceptionItem(
                    ExceptionType: r.ExceptionType,
                    OuterMessage: r.OuterMessage,
                    ProblemId: r.ProblemId,
                    Count: r.Count,
                    AffectedUsers: r.AffectedUsers,
                    RootCause: BuildTransactionUrl(SubscriptionId, ResourceGroup, AppInsightsName, r.EventId, DateTime.Parse(r.LastFailedTime))
                )));

            default:
                return BadRequest("Invalid report type.");
        }
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
    private static string BuildTransactionUrl(string subscriptionId, string resourceGroup, string appInsightsName, string eventId, DateTime timestamp)
    {
        var resourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Insights/components/{appInsightsName}";

        var componentId = JsonSerializer.Serialize(new
        {
            SubscriptionId = subscriptionId,
            ResourceGroup = resourceGroup,
            Name = appInsightsName,
            LinkedApplicationType = 0,
            ResourceId = Uri.EscapeDataString(resourceId),
            ResourceType = "microsoft.insights/components",
            IsAzureFirst = false
        });

        var dataModel = JsonSerializer.Serialize(new
        {
            eventId,
            timestamp = timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            eventTable = "requests"
        });

        return $"https://portal.azure.com/#view/AppInsightsExtension/DetailsV2Blade/ComponentId~/{Uri.EscapeDataString(componentId)}/DataModel~/{Uri.EscapeDataString(dataModel)}";
    }
}
