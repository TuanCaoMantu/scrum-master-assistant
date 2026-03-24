# TS02 — Sprint Monitor Test Suite

## Overview
Covers `GET /sprint/tools`, `GET /sprint/analyze`

> **Note:** Project and team are now **hardcoded** in the service (`Marketplace` / `Recruitement Activities`).
> `GET /sprint/analyze` takes no query params.

---

## TC02-01 — List tools returns REST API indicator
**Precondition:** API is running, `ADO_PAT` configured

**Request:**
```http
GET /sprint/tools
```

**Expected:**
- Status: `200 OK`
- Body: `["ADO REST API (no MCP tools)"]`

---

## TC02-02 — Analyze returns valid sprint data for active sprint
**Precondition:** `ADO_PAT` configured, active sprint exists in ADO for `Marketplace / Recruitement Activities`

**Request:**
```http
GET /sprint/analyze
```

**Expected:**
- Status: `200 OK`
- Body contains:
  - `sprintName`: non-empty string (e.g. `"Sprint 198 - ..."`)
  - `summary`: non-empty English text with **bold** formatting
  - `sprintHealth`: one of `"On Track"`, `"At Risk"`, `"Off Track"`
  - `progressPercent`: number between 0 and 100
  - `warnings`: array (may be empty)
  - `suggestions`: array

---

## TC02-03 — Team ID resolved correctly from team name
**Precondition:** Team `"Recruitement Activities"` exists in project `"Marketplace"` on ADO

**Expected:**
- No `404` or `"team does not exist"` error
- Log shows: `"Resolved team 'Recruitement Activities' → ID <guid>"`
- Iterations fetched successfully using GUID (not team name) in URL

---

## TC02-04 — Active sprint detected by date range
**Precondition:** A sprint with `startDate <= today <= finishDate` exists

**Expected:**
- Log shows: `"Found active sprint: <name> (<id>)"`
- `sprintName` matches the active sprint in ADO

---

## TC02-05 — Fallback to last sprint when no date-matched sprint
**Precondition:** No sprint has `startDate <= today <= finishDate` (e.g. between sprints)

**Expected:**
- Log shows: `"No date-matched sprint, using last: <name>"`
- `sprintName` matches the most recent sprint
- Status: `200 OK` (does not error)

---

## TC02-06 — Sprint health is "Off Track" when progress < 30%
**Precondition:** Sprint with 0 or very few completed story points

**Expected:**
- `sprintHealth`: `"Off Track"`
- `progressPercent` < 30

---

## TC02-07 — Sprint health is "At Risk" when progress 30–59%
**Precondition:** Sprint with ~30–59% completed story points

**Expected:**
- `sprintHealth`: `"At Risk"`
- `progressPercent` between 30 and 59

---

## TC02-08 — Sprint health is "On Track" when progress >= 60%
**Precondition:** Sprint with ≥60% completed story points

**Expected:**
- `sprintHealth`: `"On Track"`
- `progressPercent` >= 60

---

## TC02-09 — Warning raised for unassigned work items
**Precondition:** Sprint contains at least 1 work item with no `AssignedTo`

**Expected:**
- `warnings` contains entry starting with `"⚠️"` mentioning unassigned items
- Warning includes count and item IDs

---

## TC02-10 — Warning raised for high story point items still New
**Precondition:** Sprint contains work item with StoryPoints >= 5 and Status = "New"

**Expected:**
- `warnings` contains entry starting with `"🔴"` mentioning high-point unstarted items

---

## TC02-11 — No warnings when sprint is healthy
**Precondition:** All items assigned, no high-point "New" items

**Expected:**
- `warnings`: `[]`

---

## TC02-12 — Sprint with 0 story points returns progressPercent = 0
**Precondition:** All work items have no story points set (common in early sprint)

**Expected:**
- `progressPercent`: `0`
- `sprintHealth`: `"Off Track"`
- `summary` mentions lack of story point estimation

---

## TC02-13 — ADO_PAT missing causes 500 on startup
**Precondition:** `ADO_PAT` NOT configured in appsettings or env var

**Expected:**
- App fails to start OR first request to `/sprint/analyze` returns `500 Internal Server Error`
- Error message mentions `"ADO_PAT is not configured"`

---

## TC02-14 — AI summary is in English with correct format
**Request:**
```http
GET /sprint/analyze
```

**Expected:**
- `summary` written in English
- Contains at least one of: `**📊 Sprint Status**`, `**👥 Team Workload**`, `**⚠️ Risks**`, `**💡 Suggestions**`
- Does NOT contain `###` headers

---

## TC02-15 — Team not found logs available teams
**Precondition:** Team name in code does not match any team in ADO (simulate by temporarily changing team name)

**Expected:**
- Log shows: `"Team '<name>' not found. Available teams: [<list>]"`
- Falls back to using team name as-is in URL
- May result in `404` from ADO (acceptable — clearly logged)
