# Auditing

The audit trail records every change of audited entities field by field: who, when, old value, new value. It is written in the same transaction as the change, so it never lies.

- The **Audit log** page lists all changes with filters for user, entity type and time.
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` shows the history of one entity on its own page.
- `[NotAudited]` excludes a type or property, `[Sensitive]` records that a property changed without its value.
- Entities with `IVersioned` also keep full snapshots; `<VersionHistory Type="..." Id="..." />` lists them and restores an older revision.
