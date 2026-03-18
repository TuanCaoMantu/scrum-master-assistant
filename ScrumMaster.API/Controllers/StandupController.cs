using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;

namespace ScrumMaster.API.Controllers;

[ApiController]
[Route("standup")]
public class StandupController(IGeminiService gemini) : ControllerBase
{
    private readonly IGeminiService _gemini = gemini;

    [HttpPost("analyze")]
    public async Task<ActionResult<StandupSummary>> Analyze(
        CancellationToken ct)
    {
        var submissions = await ReadSubmissionsAsync(GetSubmissionsFilePath(), ct);
        var prompt = BuildStandupPrompt(submissions);
        var analysis = await _gemini.AnalyzeStandupAsync(prompt, ct);

        var blockers = submissions
        .Where(s => !string.IsNullOrWhiteSpace(s.Blockers) &&
            !s.Blockers.Equals("none", StringComparison.CurrentCultureIgnoreCase) &&
            !s.Blockers.Equals("không có", StringComparison.CurrentCultureIgnoreCase) &&
            !s.Blockers.Equals("không", StringComparison.CurrentCultureIgnoreCase))
        .Select(s => $"{s.MemberName}: {s.Blockers}")
        .ToList();

        return Ok(new StandupSummary(
        Summary: analysis,
        Blockers: blockers,
        CreatedTasks: []
        ));
    }

    [HttpPost("submit")]
    public async Task<ActionResult> Submit([FromBody] StandupSubmission submission)
    {
        var filePath = GetSubmissionsFilePath();
        var submissions = await ReadSubmissionsAsync(filePath);

        var normalizedMemberName = submission.MemberName?.Trim() ?? string.Empty;
        submissions.RemoveAll(s =>
            string.Equals(s.MemberName?.Trim(), normalizedMemberName, StringComparison.OrdinalIgnoreCase));
        submissions.Add(submission);

        var updatedJson = JsonSerializer.Serialize(submissions, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(filePath, updatedJson);
        return Ok(new { Message = "Submission saved successfully" });
    }

    [HttpGet("submissions")]
    public async Task<ActionResult<List<StandupSubmission>>> GetSubmissions()
    {
        var submissions = await ReadSubmissionsAsync(GetSubmissionsFilePath());
        return Ok(submissions);
    }

    [HttpDelete("submissions")]
    public async Task<ActionResult> ClearSubmissions(CancellationToken ct)
    {
        var filePath = GetSubmissionsFilePath();
        await System.IO.File.WriteAllTextAsync(filePath, "[]", ct);
        return Ok(new { Message = "All submissions cleared successfully" });
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
Bạn là Scrum Master AI cho team Marketplace.
Hãy tổng hợp Daily Standup hôm nay thành một summary ngắn gọn, chuyên nghiệp.

Dữ liệu standup:
""");

        foreach (var s in submissions)
        {
            sb.AppendLine($"""
👤 {s.MemberName}:
✅ Hôm qua: {s.Yesterday}
🎯 Hôm nay: {s.Today}
🚧 Blockers: {(string.IsNullOrWhiteSpace(s.Blockers) ? "Không có" : s.Blockers)}
""");
        }

        sb.AppendLine("""
Yêu cầu output:
1. Summary ngắn gọn (3-5 câu) về progress của team
2. Highlight những điểm quan trọng
3. Nếu có blockers: đánh giá mức độ ảnh hưởng và đề xuất action
4. Nhận xét ngắn về team health hôm nay
Trả lời bằng tiếng Anh.
QUAN TRỌNG: Không dùng ### headers. Chỉ dùng **bold** và xuống dòng thường.
Format ví dụ:
**📊 Daily Standup Summary**
[nội dung...]

**🎯 Highlight**
- ...

**🚧 Blockers**
- ...

**💚 Team Health**
...
""");

        return sb.ToString();
    }

}
