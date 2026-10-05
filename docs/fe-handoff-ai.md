# AI Subscription Entitlement - Backend/Frontend Handoff

Updated: 2026-10-06. AI-S5D recovery/accounting cutover on approved AI-S1/S2/S3 foundation.
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
- Daily usage includes charged/in-flight admitted operations, across both kinds. Succeeded, ProviderFailed and InvalidOutput remain daily charged.
- Admission occurs before provider work. Pre-dispatch reservations released after cancellation/preparation failure do not count. After dispatch authorization, cancellation or an unresolved attempt does not refund daily charge; do not assume every charged operation has a call log.
- Explain-per-Trip counts only Kind=Explain, Outcome=Succeeded for the exact TripId, across days.
- ProviderFailed and InvalidOutput do not consume successful Explain allowance, but do count daily. In-flight Explain temporarily occupies Trip capacity; failed/invalid or abandoned attempts release that temporary hold without granting a successful use.
- Trip exhaustion is checked before daily exhaustion, after provider/configuration and global Ai.Enabled checks.
- Semantic search and Trip Note/semantic scoring are available to all plans; they are not gated by these LLM entitlements.
- Note continues to follow normal Generate constraints/usage.
- Parse/Explain themselves do not consume normal Generate quota.
- Regenerate criteria uses Parse with base criteria; the later explicit Generate creates a new Trip and consumes normal Generate usage.
- Single purchase grants no additional AI quota/entitlement. Current effective account/subscription rights still apply.

Upgrade, renewal, downgrade/expiry and plan publication never reset user/day usage, including reserved/in-flight/abandoned operations.
Free used3 -> Membership: used3, limit30.
Membership used10 -> expired Free: used10, limit3 until next Vietnam midnight.
Do not clamp dailyUsed to dailyLimit.

Daily boundaries are Vietnam calendar days (UTC+07:00). resetAt is the next Vietnam midnight represented in UTC.
New atomic usage belongs to the immutable Vietnam admission day, even if completion happens after midnight. Legacy unlinked historical logs retain their CreatedAt-based Vietnam day. Linked completion logs never count separately from their admitted operation.
Concurrent hard-cap behavior requires coordinated all-writer cutover; a mixed fleet of legacy and admission-aware writers is unsafe. No fleet guarantee is implied by a local patch alone.

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

dailyUsed is Backend-authoritative hybrid accounting for the current owner and Vietnam day: unlinked historical logs plus charged/in-flight admitted operations, once each.
Reserved and DispatchAuthorized operations occupy daily capacity; completed operations and conservatively Abandoned unresolved dispatches remain charged. Released pre-dispatch reservations do not count.
Completion after midnight stays on its admission day; the next day's /me does not count its linked log again. Clients must not derive quota usage from LlmCallLogs or provider responses.
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
| 429 | ai_daily_limit_reached | Shared daily charged/reserved allowance exhausted; existing resetAt is next Vietnam midnight |
| 429 | ai_trip_limit_reached | Successful Explain allowance exhausted or temporarily occupied by in-flight Explain for this Trip |
| 503 | ai_unavailable | Global AI disabled/provider unavailable or existing provider/output failure path |
| 403 | ai_requires_persisted_user | AI operation requires a persisted account |

Existing endpoint validation/ownership/authentication errors are unchanged.
No PlanNotAllowed status or ai_requires_paid_plan code exists.
Frontend must render server values/errors, not calculate quota boundaries or apply guessed paid-only restrictions.

## Protected Boundaries

No payment/Upgrade/credit/receipt/Single/Finalize/Generate contract change.
No wallet/refund/carry-forward, new PlanFeature enforcement or new AI status/error.
No new API fields/errors or frontend implementation in AI-S5D. Recovery is database-only and requires no Gemini configuration; housekeeping can continue while Ai.Enabled=0.

## Coordinated Deployment

MIXED_OLD_NEW_AI_WRITERS = UNSAFE.

1. Ai.Enabled=0.
2. Apply all approved migrations.
3. Deploy admission-aware code to ALL API instances.
4. Drain/remove all legacy writers.
5. Verify fleet version.
6. Ai.Enabled=1.

This is a deployment contract, not automated production deployment. Recovery never resends provider requests or fabricates provider-call evidence.
