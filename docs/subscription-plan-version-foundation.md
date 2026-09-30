# Subscription Plan Version Foundation (BE-113 / BE-116)

## Runtime Contract

- SubscriptionPlans hold stable canonical identities and unique immutable published priorities.
- SubscriptionPlanVersions are immutable commercial contracts, including finite/null quotas.
- SubscriptionPeriods are immutable half-open paid intervals: StartsAt <= now < EndsAt.
- New orders snapshot PlanId, PlanVersionId and Amount. A current-pointer change never rebinds a pending order.
- Settlement under the existing User -> Order locks appends a period after the same-plan tail, marks Paid and enqueues one receipt in the same transaction.
- Different plans may overlap. Exactly one highest-priority active period supplies all terms. No quota union, fallback on exhaustion, pause or extension.
- Free resolves its current version immediately without per-user periods.
- Free Generate usage is monthly in Vietnam time, with null SubscriptionPeriodId. Paid finite usage belongs to the effective period. Unlimited usage is not charged.
- SavedTripLimit is all current non-deleted Finalized trips, not a period token counter.
- /me endsAt is continuous paid-through for the selected plan; effectiveUntil is the selected period/version boundary.
- Public built-in aliases remain Free, TripPass, Membership; canonical inputs also work.
- Custom codes work without enum additions. Public /plans discovery remains system-only pending consumer support.

## Schema and Immutability

Migration: 20260930114805_AddVersionedSubscriptionFoundation.

New tables: SubscriptionPlans, SubscriptionPlanVersions, SubscriptionPeriods.
Additions: PaymentOrders.PlanId / PlanVersionId / PlanVersionBinding and UsageEvents.SubscriptionPeriodId.
Legacy UserSubscriptions, PaymentOrder.PlanCode and financial history remain.

EF guards prohibit changes/deletion of versions and periods. PostgreSQL triggers protect identity/published priority, version terms, version/period immutability, native order snapshots and exact period source consistency.
Composite foreign keys protect plan/version and usage/period ownership.
The btree_gist exclusion constraint prevents same-user/same-plan interval overlap.
Deferred PostgreSQL constraint triggers require paid native orders and their source periods to agree at transaction commit, regardless of EF insert/update ordering.
Native and legacy period sources are unique and exactly one is required.

Publication is a repository foundation, not an Admin endpoint: lock the identity, append the next immutable version, then swap CurrentVersionId in one transaction.
All versions retained; no Draft version workflow or feature table implemented.

## Legacy Provenance

Three deterministic baseline V1 plans/versions are imported:
FREE = 0 / no duration / Generate 1 / Saved 1.
TRIP_PASS = 19000 / 7 days / Generate unlimited / Saved 3.
MEMBERSHIP = 59000 / 30 days / Generate unlimited / Saved unlimited.

Origin = LegacyBaseline; PublishedAt is unknown, not a reconstructed historical publication date.
Each paid legacy UserSubscription imports ONE whole baseline period, retaining UserId, StartsAt, EndsAt and LegacyUserSubscriptionId. No inferred renewal split or payment association.
Existing PaymentOrders retain financial fields and receive a stable PlanId where known, NULL PlanVersionId and LegacyUnresolved binding.
A matching amount is NOT historical evidence. A historical 49000 TripPass order is NOT bound to the 19000 baseline.
Old UsageEvents keep NULL period attribution; no paid usage is invented.
A successful notification for an unresolved non-Paid order returns UnresolvedPlanVersion without granting entitlement or changing its successful-settlement state.
Already-Paid legacy orders remain idempotent terminal no-ops.

## Deployment Gate (Not Executed Against Local/Team/Production DB)

This is an additive schema migration, NOT permission for unattended mixed-version deployment.
Old binaries must stop writing entitlement aggregates before import/runtime cutover; continued legacy writers would leave the period model stale.
Do not rely on rolling deployment with old order writers: custom orders have nullable legacy PlanCode, and old inserts cannot establish the new purchase snapshot.

Before deploying:
1. Back up the target database and review the SQL in staging.
2. Verify extension privilege/availability for btree_gist.
3. Check unsupported legacy aggregate codes (including explicit Free rows) and invalid StartsAt/EndsAt ranges; reconcile with evidence, never discard them. Migration aborts atomically if any are found.
4. Stop legacy writers during import and cutover.
5. Inventory every LegacyUnresolved Pending / Failed / Expired order. These can receive a late payment.
6. Resolve them through an explicitly approved, evidence-backed legacy binding procedure, or postpone cutover. Never derive exact versions from amount alone, rewrite financial fields, or auto-bind to current terms.
7. Verify imported aggregates and historical amounts against the backup before starting native writers.
8. Monitor UnresolvedPlanVersion errors. Verified webhook delivery acknowledgement is not proof of entitlement settlement.
9. Future benefit features must attach to immutable version identity.

Read-only post-migration inventory:
```sql
SELECT "Id", "PlanCode", "Amount", "Status", "ProviderOrderCode"
FROM "PaymentOrders"
WHERE "PlanVersionBinding" = 'LegacyUnresolved'
  AND "Status" IN ('Pending', 'Failed', 'Expired');

SELECT "Id", "PlanCode", "StartsAt", "EndsAt"
FROM "UserSubscriptions"
WHERE "PlanCode" NOT IN ('TripPass', 'Membership')
   OR "StartsAt" >= "EndsAt";
```

Down migration refuses native orders/periods or custom plans. After native purchases, use forward repair or a reviewed backup recovery; do not drop paid history to make downgrade succeed.

## Verification

Set LOCALMATE_TEST_CONNECTION only for the test process and use an isolated disposable PostGIS database whose name starts with localmate_s1b_test.
PostgreSQL tests fail explicitly when configuration is missing or names an ordinary database; they do not silently return.
Existing PostgreSQL tests require the full migrated schema. Immutable test fixtures may remain until their disposable DB/container is removed.
New foundation tests create/drop uniquely named isolated databases, verifying both fresh migration and upgrade from AddEmailOutbox.

```powershell
dotnet build --nologo
dotnet test --no-build --nologo
dotnet ef migrations list --project LocalMateAI.Infrastructure --startup-project LocalMateAI.Infrastructure
dotnet ef migrations has-pending-model-changes --project LocalMateAI.Infrastructure --startup-project LocalMateAI.Infrastructure
```

No PayOS network calls, real payments, production migration, Admin CRUD/lifecycle/history APIs, PlanFeatures, single-itinerary, refund or frontend changes are part of this batch.
