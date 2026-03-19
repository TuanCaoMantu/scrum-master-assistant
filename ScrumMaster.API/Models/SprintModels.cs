namespace ScrumMaster.API.Models;

public record SprintAnalysisRequest(
    string Project,
    string Team
);

public record SprintAnalysis(
    string SprintName,
    string Summary,
    string SprintHealth,        // "On Track" | "At Risk" | "Off Track"
    double ProgressPercent,
    List<string> Warnings,
    List<string> Suggestions
);
