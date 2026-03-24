# Test Cases — AI Scrum Master API

## Test Suites

| Suite | File | Scope | # Test Cases |
|-------|------|-------|-------------|
| TS01 | [TS01-Standup.md](./TS01-Standup.md) | Daily Standup (submit, analyze, clear) | 13 |
| TS02 | [TS02-SprintMonitor.md](./TS02-SprintMonitor.md) | Sprint Monitor (ADO REST API + AI analysis) | 15 |
| TS03 | [TS03-BlockerTracker.md](./TS03-BlockerTracker.md) | Blocker Tracker (CRUD + auto follow-up + escalation) | 22 |

**Total: 50 test cases**

---

## Base URL
```
http://localhost:5104
```

---

## Environment Setup
Ensure the following are configured in `appsettings.Development.json` (local) or Azure App Service → Configuration (production):
```
AZURE_OPENAI_KEY=<your-key>
ADO_PAT=<your-personal-access-token>
ADO_ORG=Mantu   (optional, defaults to "Mantu")
```

> ⚠️ Never commit `ADO_PAT` or `AZURE_OPENAI_KEY` to source control.
> Use `appsettings.Development.json` (gitignored) for local, App Service Configuration for production.

---

## Test Execution Order
Run suites in this order to avoid dependency issues:

```
TS01 → TS02 → TS03
```

For TS03, some test cases depend on each other (noted in preconditions). Run TC03-xx in sequence within the suite.

---

## Notes
- **AI response validation**: For any test case checking `summary`/`aiSummary`, verify language (English) and format (**bold**, no ### headers). Exact content will vary per AI response.
- **TS02 requires live ADO connection**: `ADO_PAT` must be configured with Work Items Read + Project Read scopes. No MCP server needed (uses REST API directly).
- **TS02 — project/team hardcoded**: `Marketplace` / `Recruitement Activities` — no query params needed for `/sprint/analyze`.
- **TS03 time-based tests** (TC03-15, TC03-16, TC03-17, TC03-18): Manually set `createdAt` or `lastFollowUpAt` in the DB to simulate time passing.
