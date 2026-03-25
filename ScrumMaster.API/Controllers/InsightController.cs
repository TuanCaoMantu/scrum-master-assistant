using Azure;
using Microsoft.AspNetCore.Mvc;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;
using System.Text;
using System.Text.Json;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("insight")]
public class InsightController(
    IApplicationInsightsService appInsights,
    IGeminiService ai,
    ILogger<InsightController> logger) : ControllerBase
{
    private const int MaxAiBatch = 10;

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
                // var enrichedRequests = await EnrichRequestsAsync(failedRequests, ct);
                var result = failedRequests.Where(r => !string.IsNullOrEmpty(r.EventId)).Select(r => new FailedRequestItem(
                    Operation: r.Operation,
                    TotalCount: r.TotalCount,
                    FailedCount: r.FailedCount,
                    FailPct: r.FailPct,
                    Users: r.Users,
                    RootCause: BuildTransactionUrl("9b66da88-8ca2-491f-8b05-411f59b60aac", "rg-erp", "appi-marketplace-qa", r.EventId, DateTime.Parse(r.LastFailedTime))
                )).ToList();
                return Ok(result);

            case ReportType.FailedDependencies:
                var failedDependencies = await SafeFetch(() => appInsights.GetFailedDependenciesAsync(timeRange, roleList, ct), "failed dependencies");
                var enrichedDependencies = await EnrichDependenciesAsync(failedDependencies, ct);
                return Ok(enrichedDependencies);

            case ReportType.Exceptions:
                var exceptions = await SafeFetch(() => appInsights.GetExceptionsAsync(timeRange, roleList, ct), "exceptions");
                var enrichedExceptions = await EnrichExceptionsAsync(exceptions, ct);
                return Ok(enrichedExceptions);

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

    // ── Enrich ────────────────────────────────────────────────────────────────

    private async Task<List<FailedRequestItem>> EnrichRequestsAsync(
        List<RawFailedRequest> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var prompt = BuildPrompt("Failed Requests", items.Take(MaxAiBatch)
            .Select(r => $"{r.Operation} — {r.FailedCount}/{r.TotalCount} failed ({r.FailPct}%) — {r.Users} users affected")
            .ToList());

        var rootCauses = ParseRootCauses(await ai.AnalyzeAsync(prompt, ct), items.Count);

        return items.Select((r, i) => new FailedRequestItem(
            Operation: r.Operation,
            TotalCount: r.TotalCount,
            FailedCount: r.FailedCount,
            FailPct: r.FailPct,
            Users: r.Users,
            RootCause: i < rootCauses.Count ? rootCauses[i] : "Check service logs for this operation."
        )).ToList();
    }

    private async Task<List<FailedDependencyItem>> EnrichDependenciesAsync(
        List<RawFailedDependency> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var prompt = BuildPrompt("Failed Dependencies", items.Take(MaxAiBatch)
            .Select(r => $"{r.Name} [{r.DependencyType}] — {r.FailedCount}/{r.TotalCount} failed — avg {r.AvgDurationMs:F0}ms")
            .ToList());

        var rootCauses = ParseRootCauses(await ai.AnalyzeAsync(prompt, ct), items.Count);

        return items.Select((r, i) => new FailedDependencyItem(
            Name: r.Name,
            DependencyType: r.DependencyType,
            TotalCount: r.TotalCount,
            FailedCount: r.FailedCount,
            AvgDurationMs: r.AvgDurationMs,
            RootCause: i < rootCauses.Count ? rootCauses[i] : "Check downstream service availability."
        )).ToList();
    }

    private async Task<List<ExceptionItem>> EnrichExceptionsAsync(
        List<RawException> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var prompt = BuildPrompt("Exceptions", items.Take(MaxAiBatch)
            .Select(r => $"{r.ExceptionType} — {r.Count}x — {r.AffectedUsers} users — {r.OuterMessage}")
            .ToList());

        var rootCauses = ParseRootCauses(await ai.AnalyzeAsync(prompt, ct), items.Count);

        return items.Select((r, i) => new ExceptionItem(
            ExceptionType: r.ExceptionType,
            OuterMessage: r.OuterMessage,
            ProblemId: r.ProblemId,
            Count: r.Count,
            AffectedUsers: r.AffectedUsers,
            RootCause: i < rootCauses.Count ? rootCauses[i] : "Review stack trace for this exception type."
        )).ToList();
    }

    // ── Prompt helpers ────────────────────────────────────────────────────────

    private static string BuildPrompt(string category, List<string> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a senior DevOps engineer analyzing Application Insights telemetry.");
        sb.AppendLine($"For each {category} item below, provide a concise root cause (1 sentence max).");
        sb.AppendLine($"Focus on actionable insights: what likely caused it and what to check.\n");
        sb.AppendLine($"Category: {category}");
        for (int i = 0; i < items.Count; i++)
            sb.AppendLine($"{i + 1}. {items[i]}");
        sb.AppendLine("\nRespond ONLY with a numbered list. Format: \"1. <root cause>\"");
        return sb.ToString();
    }

    private static List<string> ParseRootCauses(string aiResponse, int expectedCount)
    {
        var result = aiResponse
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l =>
            {
                var dot = l.IndexOfAny(['.', ')']);
                return dot > 0 && int.TryParse(l[..dot], out _)
                    ? l[(dot + 1)..].Trim()
                    : null;
            })
            .Where(l => l != null)
            .Select(l => l!)
            .ToList();

        while (result.Count < expectedCount)
            result.Add("Check service logs and recent deployments.");

        return result;
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

        var result = $"https://portal.azure.com/#view/AppInsightsExtension/DetailsV2Blade/ComponentId~/{Uri.EscapeDataString(componentId)}/DataModel~/{Uri.EscapeDataString(dataModel)}";
        return result;
    }
}

