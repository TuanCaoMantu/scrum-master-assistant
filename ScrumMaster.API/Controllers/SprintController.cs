using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("sprint")]
public class SprintController(
    IAzureDevOpsMcpService ado,
    IGeminiService ai,
    ILogger<SprintController> logger) : ControllerBase
{
    [HttpGet("tools")]
    public async Task<ActionResult<IEnumerable<string>>> ListTools(CancellationToken ct)
    {
        var tools = await ado.ListToolsAsync(ct);
        return Ok(tools);
    }

    [HttpGet("analyze")]
    public async Task<ActionResult<SprintAnalysis>> Analyze(
        [FromQuery] string project,
        [FromQuery] string team,
        CancellationToken ct)
    {
        logger.LogInformation("Analyzing sprint for {Project}/{Team}", project, team);

        var sprintDataJson = await ado.GetCurrentSprintItemsAsync(project, team, ct);
        using var sprintDoc = JsonDocument.Parse(sprintDataJson);
        var sprintData      = sprintDoc.RootElement;
        var sprintName      = sprintData.GetProperty("sprintName").GetString() ?? "Unknown Sprint";

        // Parse work items
        var workItems = ParseWorkItems(sprintData);

        // Tính toán metrics
        var totalPoints     = workItems.Sum(w => w.StoryPoints);
        var donePoints      = workItems
            .Where(w => w.Status is "Resolved" or "Closed" or "Done")
            .Sum(w => w.StoryPoints);
        var progressPct = totalPoints > 0
            ? Math.Round(donePoints / totalPoints * 100, 1) : 0;

        // Build warnings
        var warnings = new List<string>();

        var unassigned = workItems.Where(w => w.Owner == "Unassigned").ToList();
        if (unassigned.Any())
            warnings.Add($"⚠️ {unassigned.Count} items chưa có owner: {string.Join(", ", unassigned.Select(w => $"#{w.Id}"))}");

        var highNew = workItems.Where(w => w.StoryPoints >= 5 && w.Status == "New").ToList();
        if (highNew.Any())
            warnings.Add($"🔴 {highNew.Count} US điểm cao (≥5sp) vẫn chưa start: {string.Join(", ", highNew.Select(w => $"#{w.Id} — {w.Title}"))}");

        // Tính sprint health dựa trên ngày (nếu có)
        var health = progressPct >= 60 ? "On Track"
                   : progressPct >= 30 ? "At Risk"
                   : "Off Track";

        // Build prompt cho AI
        var prompt = BuildSprintPrompt(sprintName, team, workItems, progressPct, donePoints, totalPoints);
        var analysis = await ai.AnalyzeAsync(prompt, ct);

        return Ok(new SprintAnalysis(
            SprintName      : sprintName,
            Summary         : analysis,
            SprintHealth    : health,
            ProgressPercent : progressPct,
            Warnings        : warnings,
            Suggestions     : []
        ));
    }

    private static List<WorkItemSummary> ParseWorkItems(JsonElement sprintData)
    {
        var result = new List<WorkItemSummary>();

        if (!sprintData.TryGetProperty("workItems", out var workItemsEl))
            return result;

        // MCP có thể trả về { value: [...] } hoặc array thẳng
        JsonElement arr;
        if (workItemsEl.ValueKind == JsonValueKind.Array)
            arr = workItemsEl;
        else if (workItemsEl.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.Array)
            arr = valueEl;
        else
            return result;

        foreach (var item in arr.EnumerateArray())
        {
            var fields     = item.TryGetProperty("fields", out var f) ? f : item;
            var assignedTo = fields.TryGetProperty("System.AssignedTo", out var a)
                ? (a.ValueKind == JsonValueKind.Object
                    ? a.GetProperty("displayName").GetString()
                    : a.GetString()) ?? "Unassigned"
                : "Unassigned";

            result.Add(new WorkItemSummary(
                Id          : item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                Title       : fields.TryGetProperty("System.Title", out var t) ? t.GetString() ?? "" : "",
                Status      : fields.TryGetProperty("System.State", out var s) ? s.GetString() ?? "New" : "New",
                Owner       : assignedTo,
                StoryPoints : fields.TryGetProperty("Microsoft.VSTS.Scheduling.StoryPoints", out var sp)
                    ? sp.ValueKind == JsonValueKind.Number ? sp.GetDouble() : 0 : 0,
                WorkItemType: fields.TryGetProperty("System.WorkItemType", out var wt) ? wt.GetString() ?? "" : ""
            ));
        }

        return result;
    }

    private static string BuildSprintPrompt(
        string sprintName, string team,
        List<WorkItemSummary> items,
        double progressPct, double donePts, double totalPts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""
            You are an AI Scrum Master for team "{team}".
            Analyze the current sprint status and provide professional insights.

            **Sprint:** {sprintName}
            **Progress:** {donePts}/{totalPts} story points ({progressPct}%)

            **Work Items:**
            """);

        foreach (var w in items.OrderBy(w => w.Status))
            sb.AppendLine($"- [{w.Status}] #{w.Id} {w.Title} | Owner: {w.Owner} | {w.StoryPoints}sp | {w.WorkItemType}");

        sb.AppendLine("""

            Please analyze and respond in English:

            **📊 Sprint Status** — Overall progress summary
            **👥 Team Workload** — Who is overloaded/underloaded?
            **⚠️ Risks** — Key concerns to watch
            **💡 Suggestions** — Specific actions for the team

            IMPORTANT: Do not use ### headers. Only use **bold** and line breaks.
            """);

        return sb.ToString();
    }
}

// Internal model chỉ dùng trong controller
internal record WorkItemSummary(
    int Id, string Title, string Status,
    string Owner, double StoryPoints, string WorkItemType);
