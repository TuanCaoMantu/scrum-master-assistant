using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumMaster.API.Data;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;
using System.Text;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("blockers")]
public class BlockerController(
    AppDbContext db,
    IGeminiService ai,
    ILogger<BlockerController> logger) : ControllerBase
{
    private const double FollowUpThresholdHours   = 24;
    private const double EscalationThresholdHours = 48;

    // POST /blockers — Tạo blocker mới
    [HttpPost]
    public async Task<ActionResult<Blocker>> Create(
        [FromBody] CreateBlockerRequest req,
        CancellationToken ct)
    {
        // Validate string lengths to match database constraints
        if (req.Title != null && req.Title.Length > 500)
        {
            ModelState.AddModelError(nameof(req.Title), "Title cannot exceed 500 characters.");
        }

        if (req.Reporter != null && req.Reporter.Length > 100)
        {
            ModelState.AddModelError(nameof(req.Reporter), "Reporter cannot exceed 100 characters.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var blocker = new Blocker
        {
            Title       = req.Title,
            Description = req.Description,
            Reporter    = req.Reporter,
            AssignedTo  = req.AssignedTo,
            SprintName  = req.SprintName,
            CreatedAt   = DateTime.UtcNow,
            LastFollowUpAt = DateTime.UtcNow
        };

        db.Blockers.Add(blocker);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Blocker #{Id} created by {Reporter}: {Title}", blocker.Id, blocker.Reporter, blocker.Title);
        return CreatedAtAction(nameof(GetById), new { id = blocker.Id }, blocker);
    }

    // GET /blockers — Danh sách tất cả blockers đang mở
    [HttpGet]
    public async Task<ActionResult<List<BlockerSummary>>> GetAll(
        [FromQuery] bool includeResolved = false,
        CancellationToken ct = default)
    {
        var query = db.Blockers.AsQueryable();
        if (!includeResolved)
            query = query.Where(b => b.Status != BlockerStatus.Resolved);

        var blockers = await query.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        var now      = DateTime.UtcNow;

        var summaries = blockers.Select(b => new BlockerSummary(
            Id              : b.Id,
            Title           : b.Title,
            Reporter        : b.Reporter,
            AssignedTo      : b.AssignedTo,
            Status          : b.Status,
            CreatedAt       : b.CreatedAt,
            HoursOpen       : (int)(now - b.CreatedAt).TotalHours,
            FollowUpCount   : b.FollowUpCount,
            NeedsFollowUp   : (now - b.LastFollowUpAt).TotalHours > FollowUpThresholdHours   && b.Status != BlockerStatus.Resolved,
            NeedsEscalation : (now - b.CreatedAt).TotalHours      > EscalationThresholdHours && b.Status != BlockerStatus.Resolved
        )).ToList();

        return Ok(summaries);
    }

    // GET /blockers/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Blocker>> GetById(int id, CancellationToken ct)
    {
        var blocker = await db.Blockers.FindAsync([id], ct);
        return blocker == null ? NotFound() : Ok(blocker);
    }

    // POST /blockers/{id}/followup — Follow up thủ công
    [HttpPost("{id}/followup")]
    public async Task<ActionResult> FollowUp(int id, CancellationToken ct)
    {
        var blocker = await db.Blockers.FindAsync([id], ct);
        if (blocker == null) return NotFound();

        blocker.FollowUpCount++;
        blocker.LastFollowUpAt = DateTime.UtcNow;

        if (blocker.FollowUpCount >= 2 && blocker.Status == BlockerStatus.Open)
            blocker.Status = BlockerStatus.Escalated;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Blocker #{Id} followed up (count={Count})", id, blocker.FollowUpCount);
        return Ok(new { blocker.Id, blocker.Status, blocker.FollowUpCount });
    }

    // POST /blockers/{id}/resolve — Resolve blocker
    [HttpPost("{id}/resolve")]
    public async Task<ActionResult> Resolve(
        int id,
        [FromBody] ResolveBlockerRequest req,
        CancellationToken ct)
    {
        var blocker = await db.Blockers.FindAsync([id], ct);
        if (blocker == null) return NotFound();

        blocker.Status     = BlockerStatus.Resolved;
        blocker.ResolvedAt = DateTime.UtcNow;
        blocker.Resolution = req.Resolution;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Blocker #{Id} resolved: {Resolution}", id, req.Resolution);
        return Ok(blocker);
    }

    // GET /blockers/check — Auto follow-up + escalate + AI summary
    [HttpGet("check")]
    public async Task<ActionResult> Check(CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var open     = await db.Blockers
            .Where(b => b.Status != BlockerStatus.Resolved)
            .ToListAsync(ct);

        var followedUp = new List<int>();
        var escalated  = new List<int>();

        foreach (var b in open)
        {
            var hoursOpen      = (now - b.CreatedAt).TotalHours;
            var hoursSinceFollowUp = (now - b.LastFollowUpAt).TotalHours;

            // Escalate nếu > 48h chưa resolve
            if (hoursOpen > EscalationThresholdHours && b.Status != BlockerStatus.Escalated)
            {
                b.Status = BlockerStatus.Escalated;
                escalated.Add(b.Id);
                logger.LogWarning("Blocker #{Id} escalated after {Hours}h", b.Id, (int)hoursOpen);
            }

            if (hoursSinceFollowUp > FollowUpThresholdHours)
            {
                b.FollowUpCount++;
                b.LastFollowUpAt = now;
                followedUp.Add(b.Id);
                logger.LogInformation("Auto follow-up Blocker #{Id}", b.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        string? aiSummary = null;
        if (open.Any())
        {
            var prompt = BuildBlockerCheckPrompt(open, now);
            aiSummary  = await ai.AnalyzeAsync(prompt, ct);
        }

        return Ok(new
        {
            totalOpen     = open.Count,
            followedUp,
            escalated,
            aiSummary
        });
    }

    private static string BuildBlockerCheckPrompt(List<Blocker> blockers, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            You are an AI Scrum Master. Analyze the current blockers and provide recommendations.

            **Open Blockers:**
            """);

        foreach (var b in blockers)
        {
            var hours = (int)(now - b.CreatedAt).TotalHours;
            sb.AppendLine($"""
                - #{b.Id} [{b.Status}] "{b.Title}"
                  Reporter: {b.Reporter} | Assigned: {b.AssignedTo ?? "Unassigned"}
                  Open: {hours}h | Follow-ups: {b.FollowUpCount}
                  Description: {b.Description}
                """);
        }

        sb.AppendLine("""

            Please analyze and respond in English:

            **🚧 Blocker Status** — Overall status summary
            **⚠️ Urgent** — Blockers requiring immediate attention (> 48h)
            **💡 Suggestions** — Specific actions for each blocker

            IMPORTANT: Do not use ### headers. Only use **bold** and line breaks.
            """);

        return sb.ToString();
    }
}
