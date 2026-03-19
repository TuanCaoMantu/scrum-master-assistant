using Azure;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using ScrumMaster.API.Data;
using ScrumMaster.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
var endpoint = new Uri("https://ra-azure-openai.openai.azure.com/");
var deploymentName = "gpt-4.1-mini";

builder.Services.AddSingleton(provider =>
{
    AzureOpenAIClient azureClient = new(
    endpoint,
    new AzureKeyCredential(builder.Configuration["AZURE_OPENAI_KEY"] ?? throw new InvalidOperationException("AZURE_OPENAI_KEY is not configured"))
    );
    return azureClient.GetChatClient(deploymentName);
});

builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddSingleton<IAzureDevOpsMcpService, AzureDevOpsMcpService>();

// SQLite for Blocker Tracker
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=scrum_master.db"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapControllers();

// Auto-create DB on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
