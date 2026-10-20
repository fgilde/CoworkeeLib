# Detailbereich

`CoworkeeDetailPanel<TKey>` zeigt die Details eines Eintrags eines Boards oder einer Liste in einer Schublade rechts, gebunden an einen Query-Parameter: `/board?item=42` öffnet Eintrag 42, Links, Neuladen und die Zurück-Taste funktionieren also. Auf Smartphones nimmt er den ganzen Bildschirm ein. Escape, die Schließen-Schaltfläche und ein Klick daneben schließen ihn.

```razor
<CoworkeeDetailPanel @ref="_panel" TKey="Guid" Width="560px" KeyChanged="_ => LoadAsync()">
    <Title>@_cards.FirstOrDefault(c => c.Id == context)?.Title</Title>
    <Actions>
        <MudIconButton Icon="@Icons.Material.Outlined.Edit" aria-label="@L["Edit"]" OnClick="() => EditAsync(context)" />
    </Actions>
    <ChildContent>
        <CardDetails CardId="context" />
    </ChildContent>
</CoworkeeDetailPanel>

@code {
    private CoworkeeDetailPanel<Guid> _panel = null!;

    private void Show(Card card) => _panel.Open(card.Id);
}
```

| Parameter | |
|---|---|
| `QueryParameter` | der Name in der Adresse, standardmäßig `item`; zwei Bereiche auf einer Seite brauchen zwei Namen |
| `Width` | die Breite auf breiteren Bildschirmen, standardmäßig `520px`; schmalere Bildschirme bekommen die ganze Breite |
| `Title`, `Actions`, `ChildContent` | Fragmente mit dem Schlüssel des offenen Eintrags als `context`: der Kopftext, Schaltflächen links neben Schließen, der scrollende Inhalt |
| `KeyChanged` | wird mit dem Schlüssel des geöffneten Eintrags ausgelöst, beim Schließen mit dem Standardwert, z. B. um das Board neu zu laden |

`TKey` ist jeder Typ mit `IParsable<TKey>` (`Guid`, `int`, `string`); ein Wert, der sich nicht parsen lässt, lässt den Bereich zu. `Open(key)` legt einen Verlaufseintrag an, die Zurück-Taste schließt den Bereich also wieder; Schließen ersetzt den Eintrag. Ein einfacher Link auf `?item={id}` geht ebenso. `IsOpen` und `Key` nennen den aktuellen Zustand. Der Bereich nimmt beim Öffnen den Fokus, Escape wirkt also sofort und Screenreader landen darin.
