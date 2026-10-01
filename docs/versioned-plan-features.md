# BE-114: Versioned qualitative plan features

Base: BE-113/116 commit `473d5ab73601d327182878f4e0355f10241d2ae2`.

## Contract

- `PlanFeatures` is a canonical qualitative catalog: unique uppercase code (64 characters), name (200), optional description (1000), and system marker.
- `SubscriptionPlanVersionFeatures` is inclusion by presence, ordered by `SortOrder` then feature code. Its composite primary key is `(PlanVersionId, FeatureId)`. Both foreign keys use RESTRICT; SortOrder is nonnegative.
- Features attach to versions, never plan identities. GenerateLimit and SavedTripLimit remain the only numeric quota fields. No feature value/quantity/quota is introduced.
- No Metro/Maps authorization or entitlement gate is added. This is metadata, not a new enforcement policy.
- Seed only METRO_GOOGLE_MAPS, named "Bản đồ Metro & chỉ đường Google Maps", for the three exact baseline V1 IDs. No feature set is inferred for preexisting non-baseline versions.

## Publication and immutability

`PublishVersionAsync(version, featureIds, cancellationToken)` takes an ordered selected ID list, rejects duplicates/unknown IDs, and uses the existing SubscriptionPlan row lock. Version and associations are inserted in the same save/transaction; the current pointer is advanced before commit. Any failure rolls back the entire publication.

The original `PublishVersionAsync(version, cancellationToken)` remains and delegates with an empty selection. It does not inherit prior features implicitly. Zero qualitative features is valid; callers wanting inclusion must select it explicitly.

EF rejects modification/deletion of version-feature rows and rejects adding associations unless the parent version is Added in the same SaveChanges. Existing version/period guards remain.

PostgreSQL also rejects association updates/deletes, TRUNCATE and late inserts, including a first association appended to a previously empty version. A database-only shadow column on the version, `FeaturePublicationTransactionId`, is stamped unconditionally by a BEFORE INSERT trigger using the creating top-level transaction ID. Association inserts require that stamp to equal the current transaction ID. This works with EF savepoints and avoids relying on tuple xmin/subtransaction/freeze behavior. The stamp cannot be forged by specifying a future value; the version trigger overwrites it, and existing version UPDATE/DELETE protection prevents later changes.

This internal stamp is not commercial data, not a quota, and not part of public DTOs. The new migration stamps existing versions as additive metadata without rewriting their price/quota/audit fields.

Feature names/descriptions are catalog display metadata, not frozen copies per version. Purchased inclusion is the immutable association/feature identity. This task does not add catalog editing APIs.

## Public API

`SubscriptionPlanResponse` retains its five-argument positional constructor and adds `Features` defaulting to an empty array. Each entry has Code, Name and Description.

Both anonymous routes return the current version's selected features:

- Existing `GET /api/subscription/plans` is preserved.
- Additive `GET /api/subscriptions/plans` alias implements the requested spelling without changing other subscription routes.

No payment, subscription-me, resolver, quota or settlement contract is changed.

## Migration and deployment

New migration: `20260930143244_AddVersionedPlanFeatures`, after `20260930114805_AddVersionedSubscriptionFoundation`.

Historical migrations are not modified. Only the two feature tables and internal publication stamp are added. Baseline links are inserted before enabling the new feature guard; existing financial/order/period/usage/RBAC rows are preserved.

Apply only through a reviewed deployment. S2 verification uses disposable isolated PostGIS databases; normal local/team/production databases are not migrated. Down removes feature metadata, so rolling back after new feature publications would lose those selections; preserve a backup and prefer a forward fix.

## Verification

`PlanFeaturesPostgresTests` covers fresh migration, an upgrade from S1B with exact historical-row comparisons, canonical catalog constraints, FK/duplicate/order constraints, EF/PostgreSQL guards, empty and different V2 selections, explicit ordering, concurrent publication and rollback after a pointer-write failure.

HTTP tests cover both anonymous routes, additive serialization and actual DB-backed current-version discovery. Existing quota, checkout, settlement, renewal, PayOS signature/idempotency and RBAC tests must continue to pass.

PostgreSQL tests require LOCALMATE_TEST_CONNECTION with the existing isolated `localmate_s1b_test*` naming gate. They must actually run; missing connections are not counted as passes.
