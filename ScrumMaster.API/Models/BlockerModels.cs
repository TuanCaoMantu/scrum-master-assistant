namespace ScrumMaster.API.Models;

public class Blocker
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reporter { get; set; } = "";          // Người báo blocker
    public string? AssignedTo { get; set; }             // Người chịu trách nhiệm resolve
    public BlockerStatus Status { get; set; } = BlockerStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime LastFollowUpAt { get; set; } = DateTime.UtcNow;
    public int FollowUpCount { get; set; } = 0;
    public string? Resolution { get; set; }             // Ghi chú khi resolve
    public string? SprintName { get; set; }
    public int? AdoTaskId { get; set; }                 // Link tới ADO task nếu có
}

public enum BlockerStatus
{
    Open,
    InProgress,
    Escalated,
    Resolved
}

// Request models
public record CreateBlockerRequest(
    string Title,
    string Description,
    string Reporter,
    string? AssignedTo,
    string? SprintName
);

public record ResolveBlockerRequest(
    string Resolution
);

public record BlockerSummary(
    int Id,
    string Title,
    string Reporter,
    string? AssignedTo,
    BlockerStatus Status,
    DateTime CreatedAt,
    int HoursOpen,
    int FollowUpCount,
    bool NeedsFollowUp,    // > 24h chưa được follow up
    bool NeedsEscalation   // > 48h chưa resolve
);
