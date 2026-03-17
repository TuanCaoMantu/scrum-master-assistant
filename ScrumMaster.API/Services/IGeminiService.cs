using System;

namespace ScrumMaster.API.Services;

public interface IGeminiService
{
    Task<string> AnalyzeStandupAsync(string prompt, CancellationToken ct = default);
}
