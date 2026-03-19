# Test Cases — AI Scrum Master API

## Test Suites

| Suite | File | Scope | # Test Cases |
|-------|------|-------|-------------|
| TS01 | [TS01-Standup.md](./TS01-Standup.md) | Daily Standup (submit, analyze, clear) | 13 |
| TS02 | [TS02-SprintMonitor.md](./TS02-SprintMonitor.md) | Sprint Monitor (ADO MCP + AI analysis) | 12 |
| TS03 | [TS03-BlockerTracker.md](./TS03-BlockerTracker.md) | Blocker Tracker (CRUD + auto follow-up + escalation) | 22 |

**Total: 47 test cases**

---

## Base URL
```
http://localhost:5104
```

---

## Environment Setup
Ensure the following env vars are set before running tests:
```
AZURE_OPENAI_KEY=<your-key>
ADO_ORG=Mantu
ADO_DOMAIN=dev.azure.com
```

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
- **TS02 requires live ADO connection**: MCP server must be running with valid PAT token.
- **TS03 time-based tests** (TC03-15, TC03-16, TC03-17, TC03-18): Manually set `createdAt` or `lastFollowUpAt` in the DB to simulate time passing.
