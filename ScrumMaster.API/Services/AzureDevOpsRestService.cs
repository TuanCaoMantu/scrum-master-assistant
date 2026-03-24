using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScrumMaster.API.Services;

/// <summary>
/// Replaces AzureDevOpsMcpService — calls ADO REST API directly (no Node.js / npx required).
/// </summary>
public class AzureDevOpsRestService : IAzureDevOpsMcpService
{
    private readonly HttpClient _http;
    private readonly string _org;
    private readonly ILogger<AzureDevOpsRestService> _logger;

    public AzureDevOpsRestService(IHttpClientFactory factory, ILogger<AzureDevOpsRestService> logger)
    {
        _logger = logger;
        _org    = Environment.GetEnvironmentVariable("ADO_ORG")
            ?? "Mantu"; // Default org for testing - replace with your org or set ADO_ORG env var

        var pat     = Environment.GetEnvironmentVariable("ADO_PAT")
            ?? throw new InvalidOperationException("ADO_PAT is not configured");
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));

        _http = factory.CreateClient("ado");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", encoded);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // Not applicable for REST — returns empty list
    public Task<IEnumerable<string>> ListToolsAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<string>>(["ADO REST API (no MCP tools)"]);

    // Not applicable for REST
    public Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default)
        => throw new NotSupportedException("CallToolAsync is not supported in REST mode. Use GetCurrentSprintItemsAsync directly.");

    public async Task<string> GetCurrentSprintItemsAsync(
        string project, string team, CancellationToken ct = default)
    {
        // Step 1: Get all iterations
        // ADO requires spaces in team names to remain as-is (not %20).
        // Use HttpRequestMessage with a pre-escaped Uri to bypass HttpClient re-encoding.
        var iterUrl  = $"https://dev.azure.com/{_org}/{project}/{team}/_apis/work/teamsettings/iterations?api-version=7.1";
        var iterUri  = new Uri(iterUrl.Replace(" ", "%20"));

        _logger.LogInformation("Fetching iterations from ADO: {Url}", iterUrl);

        using var iterReq  = new HttpRequestMessage(HttpMethod.Get, iterUri);
        var iterResp = await _http.SendAsync(iterReq, ct);

        if (!iterResp.IsSuccessStatusCode)
        {
            var body = await iterResp.Content.ReadAsStringAsync(ct);
            _logger.LogError("ADO iterations request failed {Status}: {Body}", iterResp.StatusCode, body);
            iterResp.EnsureSuccessStatusCode();
        }

        var iterJson = await iterResp.Content.ReadAsStringAsync(ct);
        using var iterDoc = JsonDocument.Parse(iterJson);

        string? sprintId   = null;
        string? sprintName = null;
        var today          = DateTime.UtcNow.Date;

        var iterations = iterDoc.RootElement.GetProperty("value");

        // Find current sprint by date range
        foreach (var iter in iterations.EnumerateArray())
        {
            if (!iter.TryGetProperty("attributes", out var attrs)) continue;

            if (!attrs.TryGetProperty("startDate",  out var startEl)  || startEl.ValueKind == JsonValueKind.Null) continue;
            if (!attrs.TryGetProperty("finishDate",  out var finishEl) || finishEl.ValueKind == JsonValueKind.Null) continue;

            if (DateTime.TryParse(startEl.GetString(),  out var start) &&
                DateTime.TryParse(finishEl.GetString(), out var finish) &&
                today >= start.Date && today <= finish.Date)
            {
                sprintId   = iter.GetProperty("id").GetString();
                sprintName = iter.GetProperty("name").GetString();
                _logger.LogInformation("Found active sprint: {Name} ({Id})", sprintName, sprintId);
                break;
            }
        }

        // Fallback: use last iteration
        if (sprintId == null && iterations.GetArrayLength() > 0)
        {
            var last   = iterations[iterations.GetArrayLength() - 1];
            sprintId   = last.GetProperty("id").GetString();
            sprintName = last.GetProperty("name").GetString();
            _logger.LogWarning("No date-matched sprint, using last: {Name}", sprintName);
        }

        if (sprintId == null)
            return JsonSerializer.Serialize(new { sprintName = "No active sprint", sprintId = (string?)null, workItems = Array.Empty<object>() });

        // Step 2: Get work item IDs in sprint
        var wiUrl  = $"https://dev.azure.com/{_org}/{project}/{team}/_apis/work/teamsettings/iterations/{sprintId}/workitems?api-version=7.1";
        var wiUri  = new Uri(wiUrl.Replace(" ", "%20"));

        _logger.LogInformation("Fetching sprint work items: {Url}", wiUrl);

        using var wiReq  = new HttpRequestMessage(HttpMethod.Get, wiUri);
        var wiResp = await _http.SendAsync(wiReq, ct);
        wiResp.EnsureSuccessStatusCode();

        var wiJson = await wiResp.Content.ReadAsStringAsync(ct);
        using var wiDoc = JsonDocument.Parse(wiJson);

        var ids = wiDoc.RootElement
            .GetProperty("workItemRelations")
            .EnumerateArray()
            .Where(r => r.TryGetProperty("target", out _))
            .Select(r => r.GetProperty("target").GetProperty("id").GetInt32())
            .ToList();

        _logger.LogInformation("Found {Count} work item IDs in sprint", ids.Count);

        if (ids.Count == 0)
            return JsonSerializer.Serialize(new { sprintName, sprintId, workItems = Array.Empty<object>() });

        // Step 3: Batch fetch work item details
        var idsParam = string.Join(",", ids);
        var detailUrl = $"https://dev.azure.com/{_org}/{project}/_apis/wit/workitems" +
                        $"?ids={idsParam}" +
                        $"&fields=System.Title,System.State,System.AssignedTo," +
                        $"Microsoft.VSTS.Scheduling.StoryPoints,System.WorkItemType" +
                        $"&api-version=7.1";

        _logger.LogInformation("Fetching work item details (batch {Count} items)", ids.Count);

        var detailResp = await _http.GetAsync(detailUrl, ct);
        detailResp.EnsureSuccessStatusCode();

        var detailJson = await detailResp.Content.ReadAsStringAsync(ct);
        using var detailDoc = JsonDocument.Parse(detailJson);
        var workItemsRaw    = detailDoc.RootElement.GetProperty("value").GetRawText();

        return $"{{\"sprintName\":{JsonSerializer.Serialize(sprintName)},\"sprintId\":{JsonSerializer.Serialize(sprintId)},\"workItems\":{workItemsRaw}}}";
    }
}
