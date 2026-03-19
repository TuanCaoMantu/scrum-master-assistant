using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using ScrumMaster.API.Models;

namespace ScrumMaster.Tests;

public class SprintControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public SprintControllerTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── ListTools ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTools_ReturnsToolNamesFromAdoService()
    {
        _factory.AdoMock
            .Setup(a => a.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "get_sprint", "list_work_items", "create_task" });

        var response = await _client.GetAsync("/sprint/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tools = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(tools);
        Assert.Equal(3, tools.Count);
        Assert.Contains("get_sprint", tools);
    }

    // ── Analyze ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_ValidData_ReturnsSprintAnalysis()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Story 1", "Resolved", "Alice", 5),
            Item(2, "Story 2", "Closed",   "Bob",   3)));

        var response = await _client.GetAsync("/sprint/analyze?project=MyProject&team=TeamA");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();
        Assert.NotNull(analysis);
        Assert.Equal("Sprint 2024.1", analysis.SprintName);
        Assert.Equal("AI analysis result", analysis.Summary);
    }

    [Fact]
    public async Task Analyze_AllItemsDone_ReturnsOnTrackHealth()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Story 1", "Resolved", "Alice", 8),
            Item(2, "Story 2", "Done",     "Bob",   4)));

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Equal("On Track", analysis.SprintHealth);
        Assert.Equal(100, analysis.ProgressPercent);
    }

    [Fact]
    public async Task Analyze_LowProgress_ReturnsOffTrackHealth()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Story 1", "New",      "Alice", 10),
            Item(2, "Story 2", "Resolved", "Bob",   1)));

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Equal("Off Track", analysis.SprintHealth);
        // total=11, done=1 → ~9.1% < 30%
        Assert.True(analysis.ProgressPercent < 30);
    }

    [Fact]
    public async Task Analyze_AtRiskProgress_ReturnsAtRiskHealth()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Story 1", "Resolved", "Alice", 4),
            Item(2, "Story 2", "New",      "Bob",   6)));

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Equal("At Risk", analysis.SprintHealth);
        // total=10, done=4 → 40% (30≤x<60)
        Assert.InRange(analysis.ProgressPercent, 30, 59.9);
    }

    [Fact]
    public async Task Analyze_UnassignedItem_IncludesOwnerWarning()
    {
        // No System.AssignedTo field → defaults to "Unassigned"
        var sprintJson = JsonSerializer.Serialize(new
        {
            sprintName = "Sprint 2024.1",
            workItems  = new[]
            {
                new
                {
                    id     = 1,
                    fields = new Dictionary<string, object>
                    {
                        ["System.Title"]       = "Unowned Story",
                        ["System.State"]       = "New",
                        ["System.WorkItemType"] = "User Story",
                        ["Microsoft.VSTS.Scheduling.StoryPoints"] = (object)3.0
                    }
                }
            }
        });
        SetupAdoSprint(sprintJson);

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Contains(analysis.Warnings, w => w.Contains("chưa có owner"));
    }

    [Fact]
    public async Task Analyze_HighPointsNewItem_IncludesHighSpWarning()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Big Story", "New", "Alice", 5)));

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Contains(analysis.Warnings, w => w.Contains("chưa start"));
    }

    [Fact]
    public async Task Analyze_NoHighPointsOrUnassigned_NoWarnings()
    {
        SetupAdoSprint(SprintJsonWithItems(
            Item(1, "Small Story", "New", "Alice", 3)));

        var response = await _client.GetAsync("/sprint/analyze?project=P&team=T");
        var analysis = await response.Content.ReadFromJsonAsync<SprintAnalysis>();

        Assert.NotNull(analysis);
        Assert.Empty(analysis.Warnings);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupAdoSprint(string json)
    {
        _factory.AdoMock
            .Setup(a => a.GetCurrentSprintItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    private static object Item(int id, string title, string state, string assignedTo, double sp) =>
        new
        {
            id,
            fields = new Dictionary<string, object>
            {
                ["System.Title"]    = title,
                ["System.State"]    = state,
                ["System.AssignedTo"] = assignedTo,
                ["Microsoft.VSTS.Scheduling.StoryPoints"] = sp,
                ["System.WorkItemType"] = "User Story"
            }
        };

    private static string SprintJsonWithItems(params object[] items) =>
        JsonSerializer.Serialize(new { sprintName = "Sprint 2024.1", workItems = items });
}
