using System.Net;
using System.Net.Http.Json;
using Azure;
using Moq;
using ScrumMaster.API.Models;
using ScrumMaster.API.Services;

namespace ScrumMaster.Tests;

public class InsightControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public InsightControllerTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── FailedRequests ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_FailedRequests_ReturnsOkWithItems()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawFailedRequest("GET /api/users",   100, 10, 10.0, "evt-001", 5, "2024-01-01T00:00:00Z"),
                new RawFailedRequest("POST /api/orders",  50,  5, 10.0, "evt-002", 3, "2024-01-01T01:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=FailedRequests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<FailedRequestItem>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.Equal("GET /api/users", items[0].Operation);
        Assert.Equal(10, items[0].FailedCount);
        Assert.Equal(100, items[0].TotalCount);
        Assert.Equal(5, items[0].Users);
        Assert.Contains("portal.azure.com", items[0].RootCause);
    }

    [Fact]
    public async Task GetReport_FailedRequests_FiltersRowsWithEmptyEventId()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawFailedRequest("GET /api/users",  100, 10, 10.0, "evt-001", 5, "2024-01-01T00:00:00Z"),
                new RawFailedRequest("POST /api/orders", 50,  5, 10.0, "",        3, "2024-01-01T01:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=FailedRequests");
        var items = await response.Content.ReadFromJsonAsync<List<FailedRequestItem>>();

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("GET /api/users", items[0].Operation);
    }

    [Fact]
    public async Task GetReport_FailedRequests_KqlError_Returns200WithEmptyList()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(400, "Bad KQL query"));

        var response = await _client.GetAsync("/insight/report?reportType=FailedRequests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<FailedRequestItem>>();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    // ── FailedDependencies ────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_FailedDependencies_ReturnsOkWithItems()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedDependenciesAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawFailedDependency("SQL: SELECT users", "SQL", 200, 20, 450.0, "evt-100", "2024-01-01T00:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=FailedDependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<FailedDependencyItem>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("SQL: SELECT users", items[0].Name);
        Assert.Equal("SQL", items[0].DependencyType);
        Assert.Equal(450.0, items[0].AvgDurationMs);
        Assert.Contains("portal.azure.com", items[0].RootCause);
    }

    [Fact]
    public async Task GetReport_FailedDependencies_FiltersRowsWithEmptyEventId()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedDependenciesAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawFailedDependency("SQL: ok",     "SQL", 100, 5, 100.0, "evt-ok", "2024-01-01T00:00:00Z"),
                new RawFailedDependency("SQL: no-evt", "SQL",  50, 2, 200.0, "",       "2024-01-01T00:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=FailedDependencies");
        var items = await response.Content.ReadFromJsonAsync<List<FailedDependencyItem>>();

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("SQL: ok", items[0].Name);
    }

    // ── Exceptions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_Exceptions_ReturnsOkWithItems()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetExceptionsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawException("System.NullReferenceException", "Object ref not set", "prob-1", 42, 7, "evt-200", "2024-01-01T00:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=Exceptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<ExceptionItem>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("System.NullReferenceException", items[0].ExceptionType);
        Assert.Equal("Object ref not set", items[0].OuterMessage);
        Assert.Equal(42, items[0].Count);
        Assert.Equal(7, items[0].AffectedUsers);
        Assert.Contains("portal.azure.com", items[0].RootCause);
    }

    [Fact]
    public async Task GetReport_Exceptions_FiltersRowsWithEmptyEventId()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetExceptionsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RawException("ValidException",   "msg", "p1", 5, 1, "evt-300", "2024-01-01T00:00:00Z"),
                new RawException("NoEventException", "msg", "p2", 2, 0, "",        "2024-01-01T00:00:00Z")
            ]);

        var response = await _client.GetAsync("/insight/report?reportType=Exceptions");
        var items = await response.Content.ReadFromJsonAsync<List<ExceptionItem>>();

        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("ValidException", items[0].ExceptionType);
    }

    // ── Query parameters ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_DefaultReportType_CallsFailedRequests()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/insight/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.AppInsightsMock.Verify(
            s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetReport_PassesTimeRangeToService()
    {
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync("7d", It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _client.GetAsync("/insight/report?timeRange=7d");

        _factory.AppInsightsMock.Verify(
            s => s.GetFailedRequestsAsync("7d", It.IsAny<List<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetReport_ParsesCommaSeparatedRolesToList()
    {
        List<string>? capturedRoles = null;
        _factory.AppInsightsMock
            .Setup(s => s.GetFailedRequestsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, CancellationToken>((_, roles, _) => capturedRoles = roles)
            .ReturnsAsync([]);

        await _client.GetAsync("/insight/report?roles=API.One,API.Two,API.Three");

        Assert.NotNull(capturedRoles);
        Assert.Equal(3, capturedRoles.Count);
        Assert.Contains("API.One", capturedRoles);
        Assert.Contains("API.Two", capturedRoles);
        Assert.Contains("API.Three", capturedRoles);
    }

}
