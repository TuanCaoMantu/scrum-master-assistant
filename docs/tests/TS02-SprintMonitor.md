# TS02 — Sprint Monitor Test Suite

## Overview
Covers `GET /sprint/tools`, `GET /sprint/analyze`

---

## TC02-01 — List MCP tools returns non-empty array
**Precondition:** ADO MCP server is running, env vars set (`ADO_ORG`, `ADO_DOMAIN`)

**Request:**
```http
GET /sprint/tools
```

**Expected:**
- Status: `200 OK`
- Body: array of strings with at least:
  - `"work_list_team_iterations"`
  - `"wit_get_work_items_for_iteration"`
  - `"wit_get_work_items_batch_by_ids"`

---

## TC02-02 — Analyze returns valid sprint data for active sprint
**Precondition:** Valid `project` and `team` with an active sprint in ADO

**Request:**
```http
GET /sprint/analyze?project=MyProject&team=MyTeam
```

**Expected:**
- Status: `200 OK`
- Body contains:
  - `sprintName`: non-empty string
  - `summary`: non-empty English text with **bold** formatting
  - `sprintHealth`: one of `"On Track"`, `"At Risk"`, `"Off Track"`
  - `progressPercent`: number between 0 and 100
  - `warnings`: array (may be empty)

---

## TC02-03 — Sprint health is "Off Track" when progress < 30%
**Precondition:** Sprint with 0 or very few completed story points

**Expected:**
- `sprintHealth`: `"Off Track"`
- `progressPercent` < 30

---

## TC02-04 — Sprint health is "At Risk" when progress 30–59%
**Precondition:** Sprint with ~30–59% completed story points

**Expected:**
- `sprintHealth`: `"At Risk"`
- `progressPercent` between 30 and 59

---

## TC02-05 — Sprint health is "On Track" when progress >= 60%
**Precondition:** Sprint with ≥60% completed story points

**Expected:**
- `sprintHealth`: `"On Track"`
- `progressPercent` >= 60

---

## TC02-06 — Warning raised for unassigned work items
**Precondition:** Sprint contains at least 1 work item with no `AssignedTo`

**Expected:**
- `warnings` contains entry starting with `"⚠️"` mentioning unassigned items
- Warning includes count and item IDs

---

## TC02-07 — Warning raised for high story point items still New
**Precondition:** Sprint contains work item with StoryPoints >= 5 and Status = "New"

**Expected:**
- `warnings` contains entry starting with `"🔴"` mentioning high-point unstarted items

---

## TC02-08 — No warnings when sprint is healthy
**Precondition:** All items assigned, no high-point "New" items

**Expected:**
- `warnings`: `[]`

---

## TC02-09 — Analyze with missing project query param
**Request:**
```http
GET /sprint/analyze?team=MyTeam
```

**Expected:**
- Status: `400 Bad Request`

---

## TC02-10 — Analyze with invalid project name
**Request:**
```http
GET /sprint/analyze?project=NonExistentProject&team=MyTeam
```

**Expected:**
- Status: `500 Internal Server Error` or graceful error response
- Does NOT crash the server

---

## TC02-11 — Sprint with 0 story points returns progressPercent = 0
**Precondition:** All work items have no story points set

**Expected:**
- `progressPercent`: `0`
- `summary` mentions lack of story point estimation

---

## TC02-12 — AI summary is in English with correct format
**Expected:**
- `summary` is written in English
- Contains at least one of: `**📊 Sprint Status**`, `**👥 Team Workload**`, `**⚠️ Risks**`, `**💡 Suggestions**`
- Does NOT contain `###` headers
