# Scrum Master Assistant

ASP.NET Core Web API that acts as an AI-powered Scrum management assistant, integrating Azure DevOps, Azure Application Insights, and Azure OpenAI to automate standups, sprint analysis, blocker tracking, and telemetry insights.

## Tech Stack
- .NET 10.0, C#
- ASP.NET Core Web API
- SQLite + Entity Framework Core 9
- Azure OpenAI (GPT-4.1 mini) via `Azure.AI.OpenAI`
- Azure DevOps REST API (PAT auth)
- Azure Monitor Query (KQL) for Application Insights
- XUnit + Moq for testing

## Architecture
- `ScrumMaster.API/Controllers/` — API endpoints (one controller per domain)
- `ScrumMaster.API/Services/` — Business logic and external API integrations
- `ScrumMaster.API/Models/` — DTOs and response records (use C# `record` types)
- `ScrumMaster.API/Data/` — EF Core `AppDbContext` (SQLite, single `Blockers` DbSet)
- `ScrumMaster.API/Migrations/` — EF Core migrations (auto-applied on startup)
- `tests/ScrumMaster.Tests/` — Integration tests using `WebApplicationFactory`

**Key pattern:** Services return raw data → Controllers optionally enrich with AI via `IGeminiService`.

## Coding Rules
- Use `record` types for all DTOs and response models
- All I/O must be `async`/`await`; propagate `CancellationToken`
- Register all services in `Program.cs` via DI; never instantiate services directly
- Use service interfaces (`IGeminiService`, `IAzureDevOpsMcpService`, `IApplicationInsightsService`) — required for testability
- Standup submissions use file-based JSON storage with `SemaphoreSlim` mutex — do not replace with DB without discussion
- Secrets (API keys, PAT tokens, workspace IDs) go in `appsettings.Development.json` only — never in `appsettings.json`

## Commands
- `dotnet run --project ScrumMaster.API` — start API (defaults to `http://localhost:5046`)
- `dotnet test` — run all tests
- `dotnet ef migrations add <Name> --project ScrumMaster.API` — add EF migration
- `dotnet build` — build solution

## Testing
- Tests live in `tests/ScrumMaster.Tests/`
- Use `IntegrationTestFactory` (extends `WebApplicationFactory`) to bootstrap the API in tests
- Mock external services (`IGeminiService`, `IAzureDevOpsMcpService`) with Moq — never call real Azure endpoints in tests
- Run `dotnet test` after any controller or service change

## Important Rules
- NEVER commit `appsettings.Development.json` — it contains secrets
- NEVER call Azure services directly from controllers — always go through a service interface
- EF migrations are auto-applied at startup via `db.Database.MigrateAsync()` in `Program.cs`; do not call `EnsureCreated`
- Blocker auto-escalation logic lives in `BlockerController` — escalation triggers after 2+ follow-ups; preserve this behaviour when modifying
- The `GeminiService` strips markdown code fences from AI responses — keep this when extending AI calls

## Additional Context
- API flow examples: @ScrumMaster.API/ScrumMaster.API.http
- CI/CD pipeline: @.github/workflows/
