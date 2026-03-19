# TS03 — Blocker Tracker Test Suite

## Overview
Covers `POST /blockers`, `GET /blockers`, `GET /blockers/{id}`, `POST /blockers/{id}/followup`, `POST /blockers/{id}/resolve`, `GET /blockers/check`

---

## TC03-01 — Create a new blocker
**Request:**
```http
POST /blockers
Content-Type: application/json

{
  "title": "Waiting for vendor API key",
  "description": "Cannot test payment flow without the key",
  "reporter": "Tony",
  "assignedTo": "Khoi",
  "sprintName": "Sprint 198"
}
```

**Expected:**
- Status: `201 Created`
- Body: Blocker object with:
  - `id`: auto-generated integer > 0
  - `status`: `"Open"`
  - `followUpCount`: `0`
  - `createdAt`: recent timestamp (UTC)
  - `resolvedAt`: `null`

---

## TC03-02 — Create blocker without assignedTo (optional field)
**Request:** Same as TC03-01 but omit `assignedTo`

**Expected:**
- Status: `201 Created`
- `assignedTo`: `null`

---

## TC03-03 — Create blocker without sprintName (optional field)
**Expected:**
- Status: `201 Created`
- `sprintName`: `null`

---

## TC03-04 — Get all open blockers
**Precondition:** At least 1 blocker created (TC03-01)

**Request:**
```http
GET /blockers
```

**Expected:**
- Status: `200 OK`
- Returns array of `BlockerSummary`
- Each item has: `id`, `title`, `reporter`, `assignedTo`, `status`, `createdAt`, `hoursOpen`, `followUpCount`, `needsFollowUp`, `needsEscalation`
- Resolved blockers NOT included by default

---

## TC03-05 — Get all blockers including resolved
**Precondition:** At least 1 resolved blocker exists

**Request:**
```http
GET /blockers?includeResolved=true
```

**Expected:**
- Status: `200 OK`
- Includes blockers with `status = "Resolved"`

---

## TC03-06 — Get empty list when no open blockers
**Precondition:** All blockers resolved or none created

**Request:**
```http
GET /blockers
```

**Expected:**
- Status: `200 OK`
- Body: `[]`

---

## TC03-07 — Get blocker by ID
**Precondition:** Blocker with ID 1 exists

**Request:**
```http
GET /blockers/1
```

**Expected:**
- Status: `200 OK`
- Full `Blocker` object returned

---

## TC03-08 — Get blocker with non-existent ID
**Request:**
```http
GET /blockers/99999
```

**Expected:**
- Status: `404 Not Found`

---

## TC03-09 — Manual follow-up on open blocker
**Precondition:** Blocker #1 exists with `status = "Open"`, `followUpCount = 0`

**Request:**
```http
POST /blockers/1/followup
```

**Expected:**
- Status: `200 OK`
- `followUpCount`: `1`
- `status`: still `"Open"` (escalates after 2nd follow-up)

---

## TC03-10 — Second follow-up escalates blocker
**Precondition:** Blocker #1 has `followUpCount = 1`, `status = "Open"` (from TC03-09)

**Request:**
```http
POST /blockers/1/followup
```

**Expected:**
- Status: `200 OK`
- `followUpCount`: `2`
- `status`: `"Escalated"`

---

## TC03-11 — Follow-up on non-existent blocker
**Request:**
```http
POST /blockers/99999/followup
```

**Expected:**
- Status: `404 Not Found`

---

## TC03-12 — Resolve a blocker
**Precondition:** Blocker #1 exists and is Open/Escalated

**Request:**
```http
POST /blockers/1/resolve
Content-Type: application/json

{
  "resolution": "Vendor provided API key on 19/03/2026"
}
```

**Expected:**
- Status: `200 OK`
- `status`: `"Resolved"`
- `resolvedAt`: recent timestamp
- `resolution`: matches the provided text

---

## TC03-13 — Resolve non-existent blocker
**Request:**
```http
POST /blockers/99999/resolve
Content-Type: application/json

{ "resolution": "Fixed" }
```

**Expected:**
- Status: `404 Not Found`

---

## TC03-14 — Resolved blocker excluded from GET /blockers by default
**Precondition:** Blocker #1 resolved (TC03-12)

**Request:**
```http
GET /blockers
```

**Expected:**
- Blocker #1 NOT in response
- Status: `200 OK`

---

## TC03-15 — NeedsFollowUp is true when last follow-up > 24h ago
**Precondition:** Blocker with `lastFollowUpAt` set to > 24h ago, not Resolved

**Expected:**
- In `GET /blockers` response, that blocker has `needsFollowUp: true`

---

## TC03-16 — NeedsEscalation is true when blocker > 48h old and not Resolved
**Precondition:** Blocker with `createdAt` set to > 48h ago, not Resolved

**Expected:**
- In `GET /blockers` response, that blocker has `needsEscalation: true`

---

## TC03-17 — Check endpoint auto-escalates blockers > 48h old
**Precondition:** Blocker exists with `createdAt` = 3 days ago, `status = "Open"`

**Request:**
```http
GET /blockers/check
```

**Expected:**
- Status: `200 OK`
- `escalated`: array contains that blocker's ID
- Blocker `status` in DB updated to `"Escalated"`

---

## TC03-18 — Check endpoint auto-follows-up blockers > 24h since last follow-up
**Precondition:** Blocker exists with `lastFollowUpAt` = 2 days ago

**Request:**
```http
GET /blockers/check
```

**Expected:**
- `followedUp`: array contains that blocker's ID
- Blocker `followUpCount` incremented by 1
- `lastFollowUpAt` updated to now

---

## TC03-19 — Check endpoint returns AI summary when open blockers exist
**Precondition:** At least 1 open blocker

**Request:**
```http
GET /blockers/check
```

**Expected:**
- `aiSummary`: non-empty English string
- Contains `**🚧 Blocker Status**` section
- Does NOT contain `###` headers

---

## TC03-20 — Check endpoint returns null aiSummary when no open blockers
**Precondition:** All blockers resolved

**Request:**
```http
GET /blockers/check
```

**Expected:**
- `totalOpen`: `0`
- `followedUp`: `[]`
- `escalated`: `[]`
- `aiSummary`: `null`

---

## TC03-21 — Blocker auto-created from standup analyze
**Precondition:** Nam submitted standup with blocker text, no existing blocker for Nam today

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- New Blocker in DB:
  - `title`: `"Nam: Waiting for API key from vendor"`
  - `reporter`: `"Nam"`
  - `status`: `"Open"`

---

## TC03-22 — Blocker NOT duplicated when analyze runs multiple times same day
**Precondition:** TC03-21 already ran, same submissions still in file

**Request:**
```http
POST /standup/analyze
```

**Expected:**
- Blocker count for Nam today: still **1**
- No duplicate records in DB
