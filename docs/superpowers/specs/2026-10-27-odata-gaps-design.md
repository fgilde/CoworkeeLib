# OData gaps L12–L17 (found while wiring CoShipee's hub data)

## Why

CoShipee (the CargonerdsApp rebuild) exposes another team's database through Coworkee OData sets, scoped per organisation unit. A security review showed that row filters (`IODataEntityFilter<T>`) hold only for the root of an OData query: with `$expand`, a user reaches rows the filter hides, for example other customers' invoices. CoShipee closed that for its hub data with EF query filters inside its own DbContext. **Every Coworkee app** with row filters still has the hole, for example an app that filters `Crate` by group: `$expand` from another set reaches hidden crates. The other gaps block the realtime FE compat and entity sets with unusual keys.

## L12: row filters and permissions on `$expand` (security, must)

**Wanted:** an expanded navigation shows exactly the rows its target set would list for the same user, or nothing.

- **Target is a registered set** (`ODataEntityRegistry.Find(T)`):
  - The user needs the set's permission. Without it, the request answers 403 naming the navigation; dropping the navigation silently is an alternative the builder may pick.
  - The set's `IODataEntityFilter<T>`s apply to the expanded rows: a collection is filtered; a single navigation becomes null when the row is filtered out.
  - The same denied-property rules (`HideUnless`) apply. Check whether the serializer already does that per EDM type.
- **Target type has row filters but no registered set:** apply the filters.
- **Target has neither:** unchanged (EF global filters, tenant and soft delete, already apply to navigations).
- **Builder's call, design suggestion (verified feasible by reasoning only):** a subclass of the `[EnableQuery]` attribute used by `EntityODataController` (or `ODataQueryOptions` handling in the controller) that, after OData applied `$expand`/`$select`, rewrites the expression tree.
  - Every navigation member access inside the projection whose element type is in scope gets `.Where(predicate)` for collections, or `pred(x.Nav) ? x.Nav : null` for references.
  - The predicates come from running the type's filters on an empty `IQueryable<T>` and harvesting the `Where` lambdas from the resulting expression. They are evaluated per request, so closures with ids are fine.
  - A filter that does more than `Where` (Join, SelectMany, Take) cannot be harvested. For such types `$expand` answers 400 "cannot expand X", so it fails closed.
  - Nested expands (depth 2+) must be covered.
- **`$count` inside expand, `$filter` on navigation paths** (`$filter=Invoices/any(...)`): a `$filter` that reads through a navigation into a filtered type must see only visible rows, or be refused. At minimum, `any`/`all` over a filtered collection must apply the predicate as well. Test it.
- **Same rule for other paths that walk navigations:** global search fields over navigations, export columns through navigations, QueryData, and (L17) report navigation fields. Find them; fix those that read hidden rows, and list any you leave.
- **Tests (Coworkee.OData.Tests, real HTTP, at least SQLite + Postgres + SQL Server for the new tests):**
  - a parent set whose child collection has a row filter, where `$expand` hides the filtered children;
  - a reference navigation to a filtered row comes back null;
  - expanding a set without its permission gives 403;
  - nested expand;
  - `$filter` with `any` over a filtered navigation;
  - a filter that cannot be harvested gives 400 on expand;
  - no change for unfiltered navigations (existing tests stay green).

## L17: report navigation fields

`ReportSchema` adds fields through navigations with the target set's permission but **not** its row filters (around `ReportSchema.cs:70`). Navigation fields must aggregate only over rows the target set shows; the L12 predicates can be reused. Test: a report grouped by a navigation field does not count hidden target rows.

## L13: `Hide` for inherited navigations and properties

`AddODataEntity<T>(..., o => o.Hide(x => x.Tags))` fails when `Tags` is declared on a base class of `T` that is not itself registered. `$expand=Tags` still works. Make `Hide` (and `HideUnless`) work for members declared on base types, without hiding them on other registered types that share the base. If the EDM shares one structural type, explain the limitation and pick the closest safe behaviour: hide for all derived sets plus a startup warning. Test it.

## L14: case-insensitive OData URIs (helps a camelCase client)

The realtime FE sends camelCase property names in `$filter`, `$select`, `$orderby` and `$expand`. Enable case-insensitive URI resolution (`ODataUriResolver.EnableCaseInsensitive`) as an option, `CoworkeeODataOptions.CaseInsensitive`, default **on** if no existing test breaks, else off.

Response casing (camelCase JSON) is **not** in the lib. The app's compat layer does it. Document the option. Test: `$filter=name eq 'x'` and `$orderby=price` work.

## L15: keys not named `Id`

`AddODataEntity<T>("Countries", permission, o => o.Key(c => c.IsoAlpha2Code))` registers a set whose key is another property, of any primitive type (string, int, Guid). By-key reads (`/odata/Countries('DE')`) work.

Features that need a Guid `Id` must refuse such a set at registration with a clear message, or skip it, and document which ones: client entities, `VisibleIdsAsync` guards (attachments, social), workflows, history, webhooks with entity ids. Lists, `$count`, facets, search, export, reports and QueryData must work.

Tests: a string-key set and an int-key set, lists plus by-key.

## L16: computed values on hub-like entities

CoShipee needs `CalculatedShipment.ReferenceNumberDisplay`, a value computed from other columns (the ABP app added it as an EDM computed property).

Coworkee already has computed OData properties (`ODataComputedProperty`, `Inline`, `ComputedQueryRoot`). Check whether an app can add such a property to a set of an external DbContext. The `ComputedQueryRoots` interceptor is only on the app's context, see website/docs/fundamentals/persistence.md "External databases". If an expression-only computed property works without `ComputedQueryRoot`, document it with an example and a test on an external-context set. If it does not, make the minimal change so it does.

## Rules

- Docs (EN and DE) for each item in the matching website pages, changelog under Unreleased, upgrade rules if apps must act.
- Docs never mention "ABP".
- Commits without any Co-Authored-By or attribution lines.
- **CI is unavailable until about 2026-11-01** (GitHub Actions quota). The full local suite must pass, and so must provider runs for OData/Reporting changes: `COWORKEE_TEST_DATABASE=sqlite|sqlserver|postgres`.
