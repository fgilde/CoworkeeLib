# Audit

Das Audit protokolliert jede Änderung an auditierten Entities Feld für Feld: wer, wann, alter Wert, neuer Wert. Es wird in derselben Transaktion wie die Änderung geschrieben und lügt deshalb nie.

- Die Seite **Audit log** listet alle Änderungen mit Filtern für Benutzer, Entity-Typ und Zeitraum.
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` zeigt die Historie einer Entity auf ihrer eigenen Seite.
- `[NotAudited]` nimmt einen Typ oder ein Feld aus, `[Sensitive]` protokolliert, dass sich ein Feld geändert hat, ohne den Wert.
- Entities mit `IVersioned` behalten zusätzlich vollständige Schnappschüsse; `<VersionHistory Type="..." Id="..." />` listet sie und stellt eine ältere Revision wieder her.
