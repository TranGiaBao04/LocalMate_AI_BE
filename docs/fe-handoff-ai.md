# AI Subscription Entitlement - Backend/Frontend Handoff

Date: 2026-10-05. Additive AI-S3 contract on approved AI-S1/S2 foundation.
Payment/Subscription Upgrade Contract v1.0 and Single contracts are unchanged.

## Versioned Entitlements

| Built-in plan | Daily LLM calls | Successful Explain calls per Trip |
|---|---:|---:|
| Free | 3 | 1 |
| Trip Pass | 15 | 3 |
| Membership | 30 | 3 |

These are canonical version terms, not client-side enforcement constants.
Current catalog terms may change after Admin publishes a new version.
Effective paid rights come from the exact purchased version, not current catalog terms.
Free uses the current active Free version. Future paid periods grant no early rights.
Expired/terminated paid periods fall back to currently effective Free.

## Accounting / Operation Boundaries

- POST /api/trips/parse-request and POST /api/trips/{tripId}/explanations share UserId-wide daily LLM usage.
- Every persisted provider-call outcome counts daily: Succeeded, ProviderFailed and InvalidOutput, across both kinds.
- Checks occur before the provider call. Caller cancellation behavior remains unchanged; do not assume every attempted HTTP request creates a log.
- Explain-per-Trip counts only Kind=Explain, Outcome=Succeeded for the exact TripId, across days.
- ProviderFailed and InvalidOutput do not consume successful Explain allowance, but do count daily if persisted.
- Trip exhaustion is checked before daily exhaustion, after provider/configuration and global Ai.Enabled checks.
- Semantic search and Trip Note/semantic scoring are available to all plans; they are not gated by these LLM entitlements.
- Note continues to follow normal Generate constraints/usage.
- Parse/Explain themselves do not consume normal Generate quota.
- Regenerate criteria uses Parse with base criteria; the later explicit Generate creates a new Trip and consumes normal Generate usage.
- Single purchase grants no additional AI quota/entitlement. Current effective account/subscription rights still apply.

Upgrade, renewal, downgrade/expiry and plan publication never reset persisted daily usage.
Free used3 -> Membership: used3, limit30.
Membership used10 -> expired Free: used10, limit3 until next Vietnam midnight.
Do not clamp dailyUsed to dailyLimit.

Daily boundaries are Vietnam calendar days (UTC+07:00). resetAt is the next Vietnam midnight represented in UTC.
Admission is not atomic in AI-S2/S3: concurrent requests can exceed limits under the existing check/provider/log race.
No strict concurrent hard-cap guarantee is made; atomic hardening belongs to AI-S5.

## Subscription State

GET /api/subscription/me remains authenticated under the existing RegisteredUser policy.
Anonymous401 and non-persisted-account403 persisted_account_required behavior are unchanged.
Existing plan, endsAt, effectiveUntil, usage and savedTrips fields are unchanged.

New top-level block (example additive fragment):

```json
{
  "ai": {
    "dailyUsed": 2,
    "dailyLimit": 3,
    "resetAt": "2026-10-05T17:00:00Z"
  }
}
```

dailyUsed counts persisted LLM logs for the current authenticated owner since current Vietnam midnight.
dailyLimit uses AiDailyCallLimit from the already resolved effective version.
Non-null values, including0, are authoritative. Only null uses the legacy Ai.DailyCallsPerUser setting.
A broken resolver/binding/database does not award fallback entitlement or a fabricated ai block.
Ai.Enabled=0 and missing Gemini configuration do not zero/change this block: it describes entitlement and accounting, not current provider availability.
There are no dailyRemaining, enabled, available, providerConfigured, explainUsed or explainRemaining fields.
Do not infer per-trip remaining allowance from this block.

## Customer Catalog

GET /api/subscription/plans and existing alias GET /api/subscriptions/plans remain anonymous.
Catalog still lists current versions of active system plans, using existing public codes Free, TripPass, Membership.
Each existing plan item adds these nullable integer properties:

```json
{
  "aiDailyCallLimit": 15,
  "aiExplainCallsPerTripLimit": 3
}
```

They are stored current-version metadata, not runtime fallback or provider availability.
Canonical built-in baseline versions contain3/1,15/3,30/3; new published versions require explicit non-null values.
Historical/custom null metadata must not be replaced with hardcoded10/3 in response/history display.
The catalog is not authoritative for an existing paid user's effective historical version; use /me for daily entitlement/accounting.
PlanFeatures remain display metadata, not AI enforcement.

## Admin Plans

Existing permission ManagePlans, routes, paging shapes and errors remain unchanged.
GET /api/admin/plans returns the new fields in each item's currentVersion.
GET /api/admin/plans/{id} returns them in currentVersion.
GET /api/admin/plans/{id}/versions returns them in each history item.
POST /api/admin/plans and PUT /api/admin/plans/{id} responses expose the new stored version fields as well.

AI metadata is nullable in Admin responses; null stays JSON null, not legacy fallback10/3.
Old versions preserve their original values after a new publication.
Publication requests still require explicit aiDailyCallLimit (0..1000) and aiExplainCallsPerTripLimit (0..20), both non-null.
Missing/invalid terms use existing400 invalid_plan_data. AI term changes publish a new immutable version.
Same terms/name-only edits do not publish a version.
0 is authoritative zero; unlimited AI is not supported.

## Existing AI Errors

| HTTP | Code | Meaning |
|---|---|---|
| 429 | ai_daily_limit_reached | Shared daily persisted-call allowance exhausted; existing resetAt is next Vietnam midnight |
| 429 | ai_trip_limit_reached | Successful Explain allowance exhausted for this Trip |
| 503 | ai_unavailable | Global AI disabled/provider unavailable or existing provider/output failure path |
| 403 | ai_requires_persisted_user | AI operation requires a persisted account |

Existing endpoint validation/ownership/authentication errors are unchanged.
No PlanNotAllowed status or ai_requires_paid_plan code exists.
Frontend must render server values/errors, not calculate quota boundaries or apply guessed paid-only restrictions.

## Protected Boundaries

No payment/Upgrade/credit/receipt/Single/Finalize/Generate contract change.
No wallet/refund/carry-forward, new PlanFeature enforcement or new AI status/error.
No frontend implementation in AI-S3.
