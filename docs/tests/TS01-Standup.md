# TS01 — Daily Standup Test Suite

## Overview
Covers `POST /standup/submit`, `GET /standup/submissions`, `POST /standup/analyze`, `DELETE /standup/submissions`

---

## TC01-01 — Submit a valid standup entry
**Precondition:** API is running

**Request:**
```http
POST /standup/submit
Content-Type: application/json

{
  "memberName": "Tony",
  "yesterday": "Fixed login bug",
  "today": "Review PR #45",
  "blockers": ""
}
```

**Expected:**
- Status: `200 OK`
- Body: `{ "message": "Submission saved successfully" }`
- Submission persisted in `submissions.json`

---

## TC01-02 — Submit overwrites existing entry for same member
**Precondition:** Tony already has a submission from TC01-01

**Request:** Same as TC01-01 but with different `yesterday`/`today` values

**Expected:**
- Status: `200 OK`
- `GET /standup/submissions` returns only **1** entry for Tony (not duplicated)
- Content reflects the latest submission

---

## TC01-03 — Submit is case-insensitive for memberName
**Precondition:** "Tony" already submitted

**Request:** Submit with `"memberName": "tony"` (lowercase)

**Expected:**
- Status: `200 OK`
- `GET /standup/submissions` still returns only 1 entry for Tony
- Previous submission replaced

---

## TC01-04 — Submit with blockers populated
**Request:**
```json
{
  "memberName": "Nam",
  "yesterday": "Design dashboard",
  "today": "Implement dashboard",
  "blockers": "Waiting for API key from vendor"
}
```

**Expected:**
- Status: `200 OK`
- Blockers field stored correctly

---

## TC01-05 — Submit with null/empty memberName
**Request:**
```json
{
  "memberName": "",
  "yesterday": "Something",
  "today": "Something",
  "blockers": ""
}
```

**Expected:**
- Status: `200 OK` (currently accepted — note for future validation)
- OR `400 Bad Request` if validation added

---

## TC01-06 — Get submissions returns all current entries
**Precondition:** Tony and Nam have submitted (TC01-01, TC01-04)

**Request:**
```http
GET /standup/submissions
```

**Expected:**
- Status: `200 OK`
- Body: array with 2 entries
- Each entry has `memberName`, `yesterday`, `today`, `blockers`

---

## TC01-07 — Get submissions when no one has submitted
**Precondition:** submissions.json is empty or does not exist

**Request:**
```http
GET /standup/submissions
```

**Expected:**
- Status: `200 OK`
- Body: `[]`

---

## TC01-08 — Analyze with submissions — no blockers
**Precondition:** At least 1 submission with empty blockers

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- Status: `200 OK`
- Body contains:
  - `summary`: non-empty string in English, contains **bold** formatting
  - `blockers`: `[]`
  - `createdTasks`: `[]`
- No new Blocker records created in DB

---

## TC01-09 — Analyze with submissions — has blockers
**Precondition:** Nam submitted with `"blockers": "Waiting for API key from vendor"`

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- Status: `200 OK`
- `blockers`: `["Nam: Waiting for API key from vendor"]`
- New Blocker record created in DB with:
  - `title`: `"Nam: Waiting for API key from vendor"`
  - `reporter`: `"Nam"`
  - `status`: `"Open"`
- AI summary mentions blocker in English

---

## TC01-10 — Analyze does not create duplicate blocker on same day
**Precondition:** TC01-09 already ran — blocker for Nam exists in DB for today

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- Status: `200 OK`
- Blocker count in DB for Nam today: still **1** (not 2)

---

## TC01-11 — Analyze with no submissions returns graceful response
**Precondition:** `DELETE /standup/submissions` run first

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- Status: `200 OK`
- `summary`: some response (AI handles empty prompt gracefully)
- `blockers`: `[]`

---

## TC01-12 — Clear submissions
**Precondition:** Tony and Nam have submitted

**Request:**
```http
DELETE /standup/submissions
```

**Expected:**
- Status: `200 OK`
- Body: `{ "message": "All submissions cleared successfully" }`
- `GET /standup/submissions` returns `[]`

---

## TC01-13 — Summary is in English and uses correct format
**Precondition:** At least 1 submission

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- `summary` is written in English
- Contains `**📊 Daily Standup Summary**`
- Does NOT contain `###` headers
- Contains `**🎯 Highlights**` or `**🚧 Blockers**` sections
