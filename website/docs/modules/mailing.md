# Mailing

Mails are templates with a model. Modules define them with defaults per culture; administrators can override subject and body in the UI. Sending is queued and retried by a background job, every mail lands in the mail log.

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

Templates use [Scriban](https://github.com/scriban/scriban). The sample model fills the preview and the *Send test* button of the template editor. An `en` default is required.

SMTP host, port, user, password and sender are [settings](settings.md) (`Mail.Smtp.*`). Locally the app host points them to Mailpit.
