using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ScrumMaster.API.Data;
using ScrumMaster.API.Models;

namespace ScrumMaster.Tests;

public class StandupControllerTests : IClassFixture<IntegrationTestFactory>, IAsyncLifetime
{
    private readonly IntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public StandupControllerTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Clear submissions file before each test
        await _client.DeleteAsync("/standup/submissions");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Submit_NewMember_Returns200AndPersists()
    {
        var submission = new StandupSubmission("Alice", "Finished login UI", "Work on checkout", "");

        var response = await _client.PostAsJsonAsync("/standup/submit", submission);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var submissions = await _client.GetFromJsonAsync<List<StandupSubmission>>("/standup/submissions");
        Assert.NotNull(submissions);
        Assert.Single(submissions);
        Assert.Equal("Alice", submissions[0].MemberName);
    }

    [Fact]
    public async Task Submit_DuplicateMember_UpdatesExistingEntry()
    {
        var first  = new StandupSubmission("Bob", "Old work", "Old today", "");
        var second = new StandupSubmission("Bob", "New work", "New today", "none");

        await _client.PostAsJsonAsync("/standup/submit", first);
        await _client.PostAsJsonAsync("/standup/submit", second);

        var submissions = await _client.GetFromJsonAsync<List<StandupSubmission>>("/standup/submissions");
        Assert.NotNull(submissions);
        Assert.Single(submissions);
        Assert.Equal("New work", submissions[0].Yesterday);
    }

    [Fact]
    public async Task GetSubmissions_WhenEmpty_ReturnsEmptyArray()
    {
        var submissions = await _client.GetFromJsonAsync<List<StandupSubmission>>("/standup/submissions");

        Assert.NotNull(submissions);
        Assert.Empty(submissions);
    }

    [Fact]
    public async Task ClearSubmissions_DeletesAll_Returns200()
    {
        await _client.PostAsJsonAsync("/standup/submit",
            new StandupSubmission("Carol", "Something", "More things", ""));

        var deleteResponse = await _client.DeleteAsync("/standup/submissions");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var submissions = await _client.GetFromJsonAsync<List<StandupSubmission>>("/standup/submissions");
        Assert.NotNull(submissions);
        Assert.Empty(submissions);
    }

    [Fact]
    public async Task Analyze_WithBlocker_ReturnsBlockerListAndCreatesInDb()
    {
        var memberName = $"Dave_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/standup/submit",
            new StandupSubmission(memberName, "Fixed bug", "Deploy", "DB is slow"));

        _factory.GeminiMock
            .Setup(g => g.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Team is making progress.");

        var response = await _client.PostAsync("/standup/analyze", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<StandupSummary>();
        Assert.NotNull(summary);
        Assert.NotEmpty(summary.Blockers);
        Assert.Contains(summary.Blockers, b => b.Contains("DB is slow"));

        using var db = _factory.CreateDbContext();
        var blocker = db.Blockers.FirstOrDefault(b => b.Description == "DB is slow");
        Assert.NotNull(blocker);
        Assert.Equal(memberName, blocker.Reporter);
    }

    [Fact]
    public async Task Analyze_WithNoneBlocker_DoesNotCreateBlockerInDb()
    {
        var memberName = $"Eve_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/standup/submit",
            new StandupSubmission(memberName, "Reviewed PRs", "Write tests", "none"));

        var response = await _client.PostAsync("/standup/analyze", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<StandupSummary>();
        Assert.NotNull(summary);
        Assert.Empty(summary.Blockers);

        using var db = _factory.CreateDbContext();
        Assert.False(db.Blockers.Any(b => b.Reporter == memberName));
    }

    [Fact]
    public async Task Analyze_WithEmptyBlocker_DoesNotCreateBlockerInDb()
    {
        var memberName = $"Frank_{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/standup/submit",
            new StandupSubmission(memberName, "Reviewed PRs", "Write tests", ""));

        var response = await _client.PostAsync("/standup/analyze", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = _factory.CreateDbContext();
        Assert.False(db.Blockers.Any(b => b.Reporter == memberName));
    }

    [Fact]
    public async Task Analyze_DuplicateBlockerSameDay_NotCreatedTwice()
    {
        var memberName = $"Grace_{Guid.NewGuid():N}";
        var submission = new StandupSubmission(memberName, "Work", "More work", "Server is down");

        await _client.PostAsJsonAsync("/standup/submit", submission);
        await _client.PostAsync("/standup/analyze", null);

        // Resubmit same member, same blocker
        await _client.PostAsJsonAsync("/standup/submit", submission);
        await _client.PostAsync("/standup/analyze", null);

        using var db = _factory.CreateDbContext();
        var count = db.Blockers.Count(b => b.Reporter == memberName && b.Description == "Server is down");
        Assert.Equal(1, count);
    }
}
