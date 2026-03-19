using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ScrumMaster.API.Data;
using ScrumMaster.API.Services;

namespace ScrumMaster.Tests;

public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public sealed class ScopedDbContext : IDisposable
    {
        public IServiceScope Scope { get; }
        public AppDbContext Context { get; }

        public ScopedDbContext(IServiceScope scope, AppDbContext context)
        {
            Scope = scope;
            Context = context;
        }

        public void Dispose()
        {
            Context.Dispose();
            Scope.Dispose();
        }
    }

    // Keep an open in-memory SQLite connection alive for the factory's lifetime.
    // All EF Core scopes share this connection → data persists across requests.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public Mock<IGeminiService>         GeminiMock { get; } = new();
    public Mock<IAzureDevOpsMcpService> AdoMock    { get; } = new();

    public IntegrationTestFactory()
    {
        _connection.Open();

        GeminiMock
            .Setup(g => g.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("AI analysis result");

        AdoMock
            .Setup(a => a.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "get_sprint", "list_work_items" });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AZURE_OPENAI_KEY"] = "fake-key-for-testing"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace SQLite file-based DB with the same provider on an in-memory connection.
            // Using the same provider avoids the "multiple DB providers" EF Core error.
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(_connection));

            // Replace IGeminiService with mock
            var geminiDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IGeminiService));
            if (geminiDescriptor != null) services.Remove(geminiDescriptor);
            services.AddSingleton(GeminiMock.Object);

            // Replace IAzureDevOpsMcpService with mock
            var adoDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IAzureDevOpsMcpService));
            if (adoDescriptor != null) services.Remove(adoDescriptor);
            services.AddSingleton(AdoMock.Object);
        });
    }

    /// <summary>Opens a scoped DbContext against the shared in-memory SQLite database.</summary>
    public ScopedDbContext CreateDbContext()
    {
        var scope   = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new ScopedDbContext(scope, context);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _connection.Dispose();
        base.Dispose(disposing);
    }
}
