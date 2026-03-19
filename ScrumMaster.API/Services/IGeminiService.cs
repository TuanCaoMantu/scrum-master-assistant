namespace ScrumMaster.API.Services;

public interface IGeminiService
{
    Task<string> AnalyzeAsync(string prompt, CancellationToken ct = default);
}
