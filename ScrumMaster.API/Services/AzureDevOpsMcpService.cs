using System.Text.Json;
using ModelContextProtocol.Client;

namespace ScrumMaster.API.Services;

public class AzureDevOpsMcpService : IAzureDevOpsMcpService, IAsyncDisposable
{
    private readonly ILogger<AzureDevOpsMcpService> _logger;
    private readonly string _org;
    private readonly string _domain;
    private IMcpClient? _client;
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AzureDevOpsMcpService(ILogger<AzureDevOpsMcpService> logger)
    {
        _logger = logger;
        _org    = Environment.GetEnvironmentVariable("ADO_ORG") ?? "Mantu";
        _domain = Environment.GetEnvironmentVariable("ADO_DOMAIN") ?? "dev.azure.com";
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _logger.LogInformation("Starting ADO MCP server (org={Org}, domain={Domain})", _org, _domain);

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name    = "azure-devops-mcp",
                Command = "npx",
                Arguments = ["-y", "@azure-devops/mcp@latest", _org, "-d", _domain]
            });

            _client = await McpClientFactory.CreateAsync(transport, cancellationToken: ct);
            _initialized = true;

            _logger.LogInformation("ADO MCP server started successfully");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IEnumerable<string>> ListToolsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var tools = await _client!.ListToolsAsync(cancellationToken: ct);
        return tools.Select(t => t.Name);
    }

    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        _logger.LogInformation("Calling MCP tool: {Tool}", toolName);

        var result = await _client!.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: ct);

        var text = result.Content
            .Where(c => c.Type == "text")
            .Select(c => c.Text)
            .FirstOrDefault() ?? "{}";

        return text;
    }

    public async Task<string> GetCurrentSprintItemsAsync(
        string project, string team, CancellationToken ct = default)
    {
        // Bước 1: Lấy tất cả iterations rồi tự filter current theo ngày
        var iterationsResult = await CallToolAsync("work_list_team_iterations", new Dictionary<string, object?>
        {
            ["project"] = project,
            ["team"]    = team
        }, ct);

        _logger.LogDebug("Iterations raw: {Raw}", iterationsResult);

        string? sprintId   = null;
        string? sprintName = null;

        try
        {
            using var doc = JsonDocument.Parse(iterationsResult);
            var root = doc.RootElement;

            var arr = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("value", out var v) ? v : root;

            if (arr.ValueKind == JsonValueKind.Array)
            {
                var today = DateTime.UtcNow.Date;

                // Tìm iteration có startDate <= today <= finishDate
                foreach (var iter in arr.EnumerateArray())
                {
                    var attrs = iter.TryGetProperty("attributes", out var a) ? a : (JsonElement?)null;
                    if (attrs == null) continue;

                    var hasStart  = attrs.Value.TryGetProperty("startDate",  out var startEl)  && startEl.ValueKind != JsonValueKind.Null;
                    var hasFinish = attrs.Value.TryGetProperty("finishDate", out var finishEl) && finishEl.ValueKind != JsonValueKind.Null;

                    if (!hasStart || !hasFinish) continue;

                    if (DateTime.TryParse(startEl.GetString(),  out var start)  &&
                        DateTime.TryParse(finishEl.GetString(), out var finish) &&
                        today >= start.Date && today <= finish.Date)
                    {
                        sprintId   = iter.TryGetProperty("id",   out var idEl)   ? idEl.GetString()   : null;
                        sprintName = iter.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "Unknown Sprint";
                        _logger.LogInformation("Found active sprint: {Name} (id={Id})", sprintName, sprintId);
                        break;
                    }
                }

                // Fallback: lấy iteration cuối cùng nếu không tìm được by date
                if (sprintId == null && arr.GetArrayLength() > 0)
                {
                    var last   = arr[arr.GetArrayLength() - 1];
                    sprintId   = last.TryGetProperty("id",   out var idEl)   ? idEl.GetString()   : null;
                    sprintName = last.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "Unknown Sprint";
                    _logger.LogWarning("No date-matched sprint found, using last iteration: {Name}", sprintName);
                }
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Non-JSON from work_list_team_iterations: {Raw}", iterationsResult);
        }

        if (sprintId == null)
            return JsonSerializer.Serialize(new { sprintName = "No active sprint", sprintId = (string?)null, workItems = Array.Empty<object>() });

        // Bước 2: Lấy IDs từ sprint iteration
        var iterItemsResult = await CallToolAsync("wit_get_work_items_for_iteration", new Dictionary<string, object?>
        {
            ["project"]     = project,
            ["team"]        = team,
            ["iterationId"] = sprintId
        }, ct);

        _logger.LogDebug("Iteration work items raw: {Raw}", iterItemsResult);

        // Extract IDs từ response
        var ids = new List<int>();
        try
        {
            using var doc  = JsonDocument.Parse(iterItemsResult);
            var root = doc.RootElement;

            // Response có thể là { workItemRelations: [...] } hoặc { value: [...] } hoặc array
            JsonElement arr;
            if (root.TryGetProperty("workItemRelations", out var relEl))
                arr = relEl;
            else if (root.TryGetProperty("value", out var valEl))
                arr = valEl;
            else
                arr = root;

            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    // { target: { id: 123 } } hoặc { id: 123 }
                    JsonElement idEl;
                    if (item.TryGetProperty("target", out var target) &&
                        target.TryGetProperty("id", out idEl))
                        ids.Add(idEl.GetInt32());
                    else if (item.TryGetProperty("id", out idEl))
                        ids.Add(idEl.GetInt32());
                }
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Non-JSON from wit_get_work_items_for_iteration: {Raw}", iterItemsResult);
        }

        _logger.LogInformation("Found {Count} work item IDs in sprint", ids.Count);

        if (ids.Count == 0)
            return JsonSerializer.Serialize(new { sprintName, sprintId, workItems = Array.Empty<object>() });

        // Bước 3: Lấy full details qua batch
        var batchResult = await CallToolAsync("wit_get_work_items_batch_by_ids", new Dictionary<string, object?>
        {
            ["project"] = project,
            ["ids"]     = ids
        }, ct);

        _logger.LogDebug("Batch work items raw: {Raw}", batchResult);

        string workItemsRaw;
        try
        {
            using var batchDoc = JsonDocument.Parse(batchResult);
            var root           = batchDoc.RootElement;
            // Batch returns a plain array or { value: [...] }
            var el = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("value", out var v) ? v : root;
            workItemsRaw = el.GetRawText();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Non-JSON from wit_get_work_items_batch_by_ids: {Raw}", batchResult);
            workItemsRaw = "[]";
        }

        return $"{{\"sprintName\":{JsonSerializer.Serialize(sprintName)},\"sprintId\":{JsonSerializer.Serialize(sprintId)},\"workItems\":{workItemsRaw}}}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}
