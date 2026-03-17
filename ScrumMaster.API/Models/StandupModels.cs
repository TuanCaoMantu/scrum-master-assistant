namespace ScrumMaster.API.Models;

// Power Automate gửi lên
public record StandupSubmission(
string MemberName,
string Yesterday,
string Today,
string Blockers
);

// API trả về cho Power Automate
public record StandupSummary(
string Summary, // Gemini tổng hợp
List<string> Blockers, // Danh sách blockers
List<string> CreatedTasks // ADO tasks đã tạo
);
