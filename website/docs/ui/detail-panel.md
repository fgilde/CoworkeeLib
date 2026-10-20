# Detail panel

`CoworkeeDetailPanel<TKey>` shows the details of one item of a board or list in a drawer on the right, bound to a query parameter: `/board?item=42` opens item 42, so links, a reload and the back button work. On phones it takes the whole screen. Escape, the close button and a click beside it close it.

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
| `QueryParameter` | the name in the address, default `item`; two panels on a page need two names |
| `Width` | the width on wider screens, default `520px`; narrower screens get the whole width |
| `Title`, `Actions`, `ChildContent` | fragments with the key of the open item as `context`: the header text, buttons left of the close button, the scrolling body |
| `KeyChanged` | raised with the key of the opened item, or the default when it closes, e.g. to reload the board |

`TKey` is any type with `IParsable<TKey>` (`Guid`, `int`, `string`); a value that does not parse leaves the panel closed. `Open(key)` adds a history entry, so the back button closes the panel again; closing replaces the entry. A plain link to `?item={id}` works too. `IsOpen` and `Key` tell the current state. The panel takes the focus when it opens, so Escape works at once and screen readers land in it.
