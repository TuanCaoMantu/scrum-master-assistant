using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumMaster.API.Data;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("standup")]
public class StandupController(IGeminiService gemini, AppDbContext db) : ControllerBase
{
    private static readonly SemaphoreSlim SubmissionsLock = new(1, 1);

    private readonly IGeminiService _gemini = gemini;

    [HttpPost("analyze")]
    public async Task<ActionResult<StandupSummary>> Analyze(
        CancellationToken ct)
    {
        var filePath = GetSubmissionsFilePath();
        await SubmissionsLock.WaitAsync(ct);
        try
        {
            var submissions = await ReadSubmissionsAsync(filePath, ct);
            var prompt = BuildStandupPrompt(submissions);
            var analysis = await _gemini.AnalyzeAsync(prompt, ct);

            var blockerSubmissions = submissions
            .Where(s => !string.IsNullOrWhiteSpace(s.Blockers) &&
                !s.Blockers.Equals("none", StringComparison.OrdinalIgnoreCase))
            .ToList();

            // Auto-create blockers in DB — single batch existence check to avoid N+1
            var today    = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var existingTitles = (await db.Blockers
                .Where(b => b.CreatedAt >= today && b.CreatedAt < tomorrow &&
                            b.Status != BlockerStatus.Resolved)
                .Select(b => b.Title)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var blockers = new List<string>();
            foreach (var s in blockerSubmissions)
            {
                var title = $"{s.MemberName}: {s.Blockers}";
                if (title.Length > 500) title = title[..500];
                blockers.Add(title);

                if (!existingTitles.Contains(title))
                {
                    db.Blockers.Add(new Blocker
                    {
                        Title          = title,
                        Description    = s.Blockers,
                        Reporter       = s.MemberName,
                        AssignedTo     = s.MemberName,
                        CreatedAt      = DateTime.UtcNow,
                        LastFollowUpAt = DateTime.UtcNow
                    });
                }
            }
            await db.SaveChangesAsync(ct);

            return Ok(new StandupSummary(
            Summary: analysis,
            Blockers: blockers,
            CreatedTasks: []
            ));
        }
        finally
        {
            SubmissionsLock.Release();
        }
    }

    [HttpPost("submit")]
    public async Task<ActionResult> Submit([FromBody] StandupSubmission submission)
    {
        var filePath = GetSubmissionsFilePath();
        await SubmissionsLock.WaitAsync();
        try
        {
            var submissions = await ReadSubmissionsAsync(filePath);

            var normalizedMemberName = submission.MemberName?.Trim() ?? string.Empty;
            submissions.RemoveAll(s =>
                string.Equals(s.MemberName?.Trim(), normalizedMemberName, StringComparison.OrdinalIgnoreCase));
            submissions.Add(submission);

            var updatedJson = JsonSerializer.Serialize(submissions, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(filePath, updatedJson);
            return Ok(new { Message = "Submission saved successfully" });
        }
        finally
        {
            SubmissionsLock.Release();
        }
    }

    [HttpGet("submissions")]
    public async Task<ActionResult<List<StandupSubmission>>> GetSubmissions()
    {
        var filePath = GetSubmissionsFilePath();
        await SubmissionsLock.WaitAsync();
        try
        {
            var submissions = await ReadSubmissionsAsync(filePath);
            return Ok(submissions);
        }
        finally
        {
            SubmissionsLock.Release();
        }
    }

    [HttpDelete("submissions")]
    public async Task<ActionResult> ClearSubmissions(CancellationToken ct)
    {
        var filePath = GetSubmissionsFilePath();
        await SubmissionsLock.WaitAsync(ct);
        try
        {
            await System.IO.File.WriteAllTextAsync(filePath, "[]", ct);
            return Ok(new { Message = "All submissions cleared successfully" });
        }
        finally
        {
            SubmissionsLock.Release();
        }
    }

    private static string GetSubmissionsFilePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "submissions.json");
    }

    private static async Task<List<StandupSubmission>> ReadSubmissionsAsync(string filePath, CancellationToken ct = default)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return [];
        }

        await using var stream = System.IO.File.OpenRead(filePath);
        var submissions = await JsonSerializer.DeserializeAsync<List<StandupSubmission>>(stream, cancellationToken: ct);
        return submissions ?? [];
    }

    private static string BuildStandupPrompt(List<StandupSubmission> submissions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""
You are an AI Scrum Master for the Marketplace team.
Summarize today's Daily Standup in a concise, professional manner.

Standup data:
""");

        foreach (var s in submissions)
        {
            sb.AppendLine($"""
👤 {s.MemberName}:
✅ Yesterday: {s.Yesterday}
🎯 Today: {s.Today}
🚧 Blockers: {(string.IsNullOrWhiteSpace(s.Blockers) ? "None" : s.Blockers)}
""");
        }

        sb.AppendLine("""
Output requirements:
1. Brief summary (3-5 sentences) of team progress
2. Highlight key points
3. If there are blockers: assess impact and suggest actions
4. Short team health comment
Respond in English.
IMPORTANT: Do not use ### headers. Only use **bold** and line breaks.
Example format:
**📊 Daily Standup Summary**
[content...]

**🎯 Highlights**
- ...

**🚧 Blockers**
- ...

**💚 Team Health**
...
""");

        return sb.ToString();
    }

}
