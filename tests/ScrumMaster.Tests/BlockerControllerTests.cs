using System.Net;
using System.Net.Http.Json;
using Moq;
using ScrumMaster.API.Models;

namespace ScrumMaster.Tests;

public class BlockerControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public BlockerControllerTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    private static CreateBlockerRequest NewRequest(string? sprint = null) =>
        new("Cannot connect to DB", "DB connection times out", "Alice", "Bob", sprint);

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/blockers", NewRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var blocker = await response.Content.ReadFromJsonAsync<Blocker>();
        Assert.NotNull(blocker);
        Assert.True(blocker.Id > 0);
        Assert.Equal("Cannot connect to DB", blocker.Title);
        Assert.Equal("Alice", blocker.Reporter);
        Assert.Equal(BlockerStatus.Open, blocker.Status);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_DefaultExcludesResolvedBlockers()
    {
        // Create two blockers, resolve one
        var openResponse     = await _client.PostAsJsonAsync("/blockers", NewRequest());
        var resolvedResponse = await _client.PostAsJsonAsync("/blockers", NewRequest());

        var openId     = (await openResponse.Content.ReadFromJsonAsync<Blocker>())!.Id;
        var resolvedId = (await resolvedResponse.Content.ReadFromJsonAsync<Blocker>())!.Id;

        await _client.PostAsJsonAsync($"/blockers/{resolvedId}/resolve",
            new ResolveBlockerRequest("Fixed the connection pool"));

        var summaries = await _client.GetFromJsonAsync<List<BlockerSummary>>("/blockers");
        Assert.NotNull(summaries);
        Assert.Contains(summaries, s => s.Id == openId);
        Assert.DoesNotContain(summaries, s => s.Id == resolvedId);
    }

    [Fact]
    public async Task GetAll_IncludeResolved_ReturnsAllBlockers()
    {
        var openResponse     = await _client.PostAsJsonAsync("/blockers", NewRequest());
        var resolvedResponse = await _client.PostAsJsonAsync("/blockers", NewRequest());

        var openId     = (await openResponse.Content.ReadFromJsonAsync<Blocker>())!.Id;
        var resolvedId = (await resolvedResponse.Content.ReadFromJsonAsync<Blocker>())!.Id;

        await _client.PostAsJsonAsync($"/blockers/{resolvedId}/resolve",
            new ResolveBlockerRequest("Resolved"));

        var summaries = await _client.GetFromJsonAsync<List<BlockerSummary>>("/blockers?includeResolved=true");
        Assert.NotNull(summaries);
        Assert.Contains(summaries, s => s.Id == openId);
        Assert.Contains(summaries, s => s.Id == resolvedId);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingId_ReturnsBlocker()
    {
        var created = (await (await _client.PostAsJsonAsync("/blockers", NewRequest()))
            .Content.ReadFromJsonAsync<Blocker>())!;

        var response = await _client.GetAsync($"/blockers/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var blocker = await response.Content.ReadFromJsonAsync<Blocker>();
        Assert.NotNull(blocker);
        Assert.Equal(created.Id, blocker.Id);
    }

    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/blockers/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── FollowUp ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FollowUp_FirstCall_IncrementsFollowUpCount()
    {
        var blocker = (await (await _client.PostAsJsonAsync("/blockers", NewRequest()))
            .Content.ReadFromJsonAsync<Blocker>())!;

        var response = await _client.PostAsync($"/blockers/{blocker.Id}/followup", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FollowUpResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body.FollowUpCount);
        Assert.Equal(BlockerStatus.Open, body.Status);
    }

    [Fact]
    public async Task FollowUp_SecondCall_EscalatesToEscalatedStatus()
    {
        var blocker = (await (await _client.PostAsJsonAsync("/blockers", NewRequest()))
            .Content.ReadFromJsonAsync<Blocker>())!;

        await _client.PostAsync($"/blockers/{blocker.Id}/followup", null);
        var response = await _client.PostAsync($"/blockers/{blocker.Id}/followup", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FollowUpResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.FollowUpCount);
        Assert.Equal(BlockerStatus.Escalated, body.Status);
    }

    [Fact]
    public async Task FollowUp_NonExistentId_Returns404()
    {
        var response = await _client.PostAsync("/blockers/999999/followup", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ExistingBlocker_SetsStatusResolvedAndResolution()
    {
        var blocker = (await (await _client.PostAsJsonAsync("/blockers", NewRequest()))
            .Content.ReadFromJsonAsync<Blocker>())!;

        var response = await _client.PostAsJsonAsync(
            $"/blockers/{blocker.Id}/resolve",
            new ResolveBlockerRequest("Increased connection pool size"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.Content.ReadFromJsonAsync<Blocker>();
        Assert.NotNull(resolved);
        Assert.Equal(BlockerStatus.Resolved, resolved.Status);
        Assert.Equal("Increased connection pool size", resolved.Resolution);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task Resolve_NonExistentId_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/blockers/999999/resolve",
            new ResolveBlockerRequest("Whatever"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_WithOpenBlockers_ReturnsAiSummaryAndCounts()
    {
        _factory.GeminiMock
            .Setup(g => g.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Blockers need attention.");

        await _client.PostAsJsonAsync("/blockers", NewRequest());

        var response = await _client.GetAsync("/blockers/check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.NotNull(body);
        Assert.True(body.TotalOpen >= 1);
        Assert.Equal("Blockers need attention.", body.AiSummary);
    }

    [Fact]
    public async Task Check_OldBlocker_EscalatesAutomatically()
    {
        // Insert a blocker with CreatedAt > 48h ago directly via DB
        using var db = _factory.CreateDbContext();
        var oldBlocker = new ScrumMaster.API.Models.Blocker
        {
            Title          = "Old unresolved blocker",
            Description    = "Still open after 3 days",
            Reporter       = "Tester",
            CreatedAt      = DateTime.UtcNow.AddHours(-73),
            LastFollowUpAt = DateTime.UtcNow.AddHours(-73),
            Status         = ScrumMaster.API.Models.BlockerStatus.Open
        };
        db.Blockers.Add(oldBlocker);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/blockers/check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.NotNull(body);
        Assert.Contains(body.Escalated, id => id == oldBlocker.Id);
    }
}

// Local response shapes for deserialization
file record FollowUpResponse(int Id, BlockerStatus Status, int FollowUpCount);
file record CheckResponse(int TotalOpen, List<int> FollowedUp, List<int> Escalated, string? AiSummary);
