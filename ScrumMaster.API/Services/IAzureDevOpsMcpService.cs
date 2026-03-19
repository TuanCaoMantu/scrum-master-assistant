namespace ScrumMaster.API.Services;

public interface IAzureDevOpsMcpService
{
    Task<IEnumerable<string>> ListToolsAsync(CancellationToken ct = default);
    Task<string> GetCurrentSprintItemsAsync(string project, string team, CancellationToken ct = default);
    Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default);
}
