using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScrumMaster.API.Services;

public interface IAzureDevOpsMcpService
{
    Task<IEnumerable<string>> ListToolsAsync(CancellationToken ct = default);
    Task<string> GetCurrentSprintItemsAsync(string project = "Marketplace", string team = "Recruitment Activities", CancellationToken ct = default);
    Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default);
}
/// <summary>
/// Replaces the MCP stdio transport with direct ADO REST API calls.
/// Keeps the same interface so SprintController needs no changes.
/// </summary>
public class AzureDevOpsMcpService : IAzureDevOpsMcpService
{
    private readonly HttpClient _http;
    private readonly string _org;
    private readonly string _project;
    private readonly ILogger<AzureDevOpsMcpService> _logger;

    private static readonly string[] WorkItemFields =
    [
        "System.Id",
        "System.Title",
        "System.State",
        "System.AssignedTo",
        "System.WorkItemType",
        "Microsoft.VSTS.Scheduling.StoryPoints"
    ];

    public AzureDevOpsMcpService(IHttpClientFactory factory, ILogger<AzureDevOpsMcpService> logger)
    {
        _logger = logger;
        _org    = Environment.GetEnvironmentVariable("ADO_ORG")
            ?? throw new InvalidOperationException("Missing env var: ADO_ORG");
        _project = Environment.GetEnvironmentVariable("ADO_PROJECT")
            ?? throw new InvalidOperationException("Missing env var: ADO_PROJECT");

        var pat     = Environment.GetEnvironmentVariable("ADO_PAT")
            ?? throw new InvalidOperationException("Missing env var: ADO_PAT");
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));

        _http = factory.CreateClient("ado");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", encoded);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Interface stub (not used in REST mode) ────────────────────────────────
    public Task<IEnumerable<string>> ListToolsAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<string>>(["ADO REST API (no MCP)"]);

    public Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default)
        => Task.FromResult("{}");

    // ── Main method ────────────────────────────────────────────────────────────
    public async Task<string> GetCurrentSprintItemsAsync(
        string project, string team, CancellationToken ct = default)
    {
        // Use project/team from query params; fallback to env if not provided
        var proj = string.IsNullOrWhiteSpace(project) ? _project : project;
        var teamEncoded = Uri.EscapeDataString(team);

        // Step 1: Get current iteration
        var iterUrl = $"https://dev.azure.com/{_org}/{Uri.EscapeDataString(proj)}/{teamEncoded}" +
                      $"/_apis/work/teamsettings/iterations?$timeframe=current&api-version=7.1";

        _logger.LogInformation("Fetching current iteration: {Url}", iterUrl);
        var iterResp = await _http.GetAsync(iterUrl, ct);
        iterResp.EnsureSuccessStatusCode();

        using var iterDoc  = JsonDocument.Parse(await iterResp.Content.ReadAsStringAsync(ct));
        var iterations     = iterDoc.RootElement.GetProperty("value");

        if (iterations.GetArrayLength() == 0)
        {
            _logger.LogWarning("No current iteration found for {Project}/{Team}", proj, team);
            return JsonSerializer.Serialize(new { sprintName = "No active sprint", sprintId = (string?)null, workItems = Array.Empty<object>() });
        }

        var sprint     = iterations[0];
        var sprintId   = sprint.GetProperty("id").GetString()!;
        var sprintName = sprint.GetProperty("name").GetString()!;
        _logger.LogInformation("Active sprint: {Name} ({Id})", sprintName, sprintId);

        // Step 2: Get work item IDs in sprint
        var wiUrl = $"https://dev.azure.com/{_org}/{Uri.EscapeDataString(proj)}/{teamEncoded}" +
                    $"/_apis/work/teamsettings/iterations/{sprintId}/workitems?api-version=7.1";

        var wiResp = await _http.GetAsync(wiUrl, ct);
        wiResp.EnsureSuccessStatusCode();

        using var wiDoc   = JsonDocument.Parse(await wiResp.Content.ReadAsStringAsync(ct));
        var relations     = wiDoc.RootElement.GetProperty("workItemRelations");

        var ids = relations.EnumerateArray()
            .Select(r => r.GetProperty("target").GetProperty("id").GetInt32())
            .ToList();

        _logger.LogInformation("Found {Count} work items in sprint", ids.Count);

        if (ids.Count == 0)
            return JsonSerializer.Serialize(new { sprintName, sprintId, workItems = Array.Empty<object>() });

        // Step 3: Get full details (batch, max 200 per call)
        var fields   = string.Join(",", WorkItemFields);
        var idsStr   = string.Join(",", ids.Take(200));
        var detailUrl = $"https://dev.azure.com/{_org}/{Uri.EscapeDataString(proj)}" +
                        $"/_apis/wit/workitems?ids={idsStr}&fields={fields}&api-version=7.1";

        var detailResp = await _http.GetAsync(detailUrl, ct);
        detailResp.EnsureSuccessStatusCode();

        using var detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync(ct));
        var workItemsRaw    = detailDoc.RootElement.GetProperty("value").GetRawText();

        return $"{{\"sprintName\":{JsonSerializer.Serialize(sprintName)},\"sprintId\":{JsonSerializer.Serialize(sprintId)},\"workItems\":{workItemsRaw}}}";
    }
}
