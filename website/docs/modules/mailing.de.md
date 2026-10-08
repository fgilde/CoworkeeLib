# Mailversand

Mails sind Vorlagen mit einem Modell. Module definieren sie mit Standardtexten pro Sprache; Administratoren überschreiben Betreff und Text in der Oberfläche. Der Versand läuft über eine Warteschlange mit Wiederholungen in einem Hintergrundjob, jede Mail landet im Mail-Log.

```csharp
internal sealed class OrderMailTemplates : IMailTemplateContributor
{
    public void Define(MailTemplateContext context) =>
        context.Add(new MailTemplateDefinition("Orders.Shipped", "Order shipped", new OrderShippedMail("Ada", "A-1001"),
            new Dictionary<string, MailTemplateContent>
            {
                ["en"] = new("Your order {{ number }} is on its way", "Hello {{ name }}, ..."),
                ["de"] = new("Ihre Bestellung {{ number }} ist unterwegs", "Hallo {{ name }}, ..."),
            }));
}

await mails.QueueAsync(customer.Email, "Orders.Shipped", new OrderShippedMail(customer.Name, order.Number), culture: "de", ct);
```

Vorlagen nutzen [Scriban](https://github.com/scriban/scriban). Das Beispielmodell füllt die Vorschau und die Schaltfläche *Send test* im Vorlageneditor. Ein `en`-Standardtext ist Pflicht.

SMTP-Host, Port, Benutzer, Passwort und Absender sind [Einstellungen](settings.md) (`Mail.Smtp.*`). Lokal zeigt der AppHost sie auf Mailpit.
