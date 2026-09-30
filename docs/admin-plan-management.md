# Admin plan management (BE-115, BE-117, BE-119)

This layer extends BE-113/116 and BE-114 without changing their schema,
payment settlement, entitlement periods or quota algorithms.

## Authorization

Every endpoint requires `HasPermission(Permissions.ManagePlans)` and the
existing account-access middleware. Authentication alone, an Admin role claim
alone, a demo session or a locked account does not bypass permission checks.

## Endpoints

| Method | Route | Success |
| --- | --- | --- |
| GET | `/api/admin/plans` | 200, `PagedResult<AdminPlanResponse>` |
| GET | `/api/admin/plans/features` | 200, canonical feature catalog |
| GET | `/api/admin/plans/{id}` | 200, identity/current version/subscriber count |
| POST | `/api/admin/plans` | 201, response and detail Location |
| PUT | `/api/admin/plans/{id}` | 200, resulting plan |
| PUT | `/api/admin/plans/{id}/status` | 200, resulting plan |
| DELETE | `/api/admin/plans/{id}` | 204, unreferenced/unpublished custom identity only |
| GET | `/api/admin/plans/{id}/versions` | 200, paged immutable history |

List query uses BE-84 `page`, `pageSize`, `search`, `sortBy`, `sortDirection`,
plus optional `isActive` and `isSystem`. Search matches code/name,
case-insensitively. Sort whitelist: `code`, `name`, `entitlementPriority`,
`isActive`, `isSystem`, `createdAt`. Default: priority ascending. Every sort
uses Id as a deterministic tie-breaker. Counts and paging execute in SQL.

History is always `versionNumber` descending; `page`/`pageSize` are supported.
Optional sort parameters must agree with that fixed ordering. Search is not
supported on history. Each version exposes Id, versionNumber, price,
durationDays, generateLimit, savedTripLimit, origin, publishedAt, createdAt,
ordered features and isCurrent. No duplicate price-history table exists.

## Write contracts

Create accepts `code`, `name`, `entitlementPriority`, `price`, `durationDays`,
`generateLimit`, `savedTripLimit`, ordered `featureIds`.

PUT accepts the full editable terms: `name`, `price`, `durationDays`,
`generateLimit`, `savedTripLimit`, ordered `featureIds`. This is replacement,
not PATCH: send all intended terms, including explicit null quotas/duration
where allowed. An omitted/null feature list represents an empty selection.
Code, priority, IsSystem, IDs, version number, origin, publication timestamp
and current pointer are not update inputs. Unknown JSON members are rejected.

Status accepts only `{ "isActive": true/false }`; the property is required.

- Code: `^[A-Z][A-Z0-9_]{0,63}$`, unique and stable.
- Name: required, trimmed, maximum 200 characters.
- Priority: required on create, non-negative and unique; never editable.
- Paid price: positive whole VND, maximum 999999999999.
- Paid duration: positive integer days.
- FREE price: exactly zero; duration: null.
- Quotas: null means unlimited; otherwise non-negative integers, including zero.
- Features: distinct existing non-empty GUIDs, in the requested order; zero
  features is valid. This API cannot create/edit/delete feature definitions.

Create atomically inserts an inactive, non-system identity, published V1,
ordered associations and current pointer. Server assigns all identifiers and
publication metadata. Name-only edits save identity metadata but publish no
version. Identical edits are no-ops. Any commercial/feature/order change
publishes exactly one next version and advances the pointer atomically.

## Concurrency and immutability

All writes to an existing identity execute inside a transaction with
`SELECT ... FOR UPDATE` on that plan. The tracked identity is refreshed after
locking. Validation/comparison and next-version allocation occur inside the
lock. Versions and their associations are inserted together, retaining the
BE-114 publication-transaction protection; pointer update commits in the same
transaction. Existing versions/associations are never modified or deleted.

Two different concurrent edits serialize to consecutive versions. Two
identical concurrent edits produce one new version and then a no-op. Database
unique constraints resolve concurrent create races to stable 409 responses.

## Lifecycle and history safety

FREE cannot deactivate or delete. Other system plans cannot delete. Paid/custom
plans can deactivate/reactivate if activation has a valid current contract.
Same status is a no-op without SaveChanges.

Deactivation does not mutate periods, pending orders or historical versions.
Existing PaymentService checks already block new checkout/renewal for inactive
plans. Verified late payments still settle their purchased version through the
unchanged settlement path. Existing paid entitlements remain effective.

`activeSubscriberCount` counts distinct users with periods for this PlanId
where `StartsAt <= now < EndsAt`, independent of legacy UserSubscriptions,
plan activation and effective-plan masking. Future periods are excluded.

DELETE rejects every published identity, any version/order/period reference and
every system identity. Only a genuinely bare custom identity can be deleted;
FK RESTRICT protections are unchanged. A newly created custom V1 is already
published history and therefore is not deletable.

Errors: 400 `invalid_plan_data`; 404 `plan_not_found`; 409 `plan_code_exists`,
`plan_priority_exists`, `system_plan_locked`, `plan_in_use`,
`free_cannot_deactivate`, `invalid_current_plan_version`.

## Verification and scope

Tests cover service rules, HTTP/RBAC/account access and actual PostgreSQL
publication/rollback/concurrency/lifecycle behavior using isolated databases.
Full regression includes grandfathering, version snapshots, PayOS verification
and idempotency, quota enforcement and previous migrations.

No migration, consumer custom-plan discovery change, real PayOS call, ordinary
DB migration, FE change, Single Itinerary, refund/manual grant or audit ledger.
