# Webhooks

Mit `Coworkee.Webhooks` erfahren andere Systeme, was in deiner App passiert, ohne ständig nachzufragen. Admins einer Organisation tragen Endpunkte ein; jeder Endpunkt bekommt für die Ereignisse, die er ausgewählt hat, einen signierten `POST`. Ereignisse sind Änderungen an den Entity-Sets, die du freischaltest, Workflow-Übergänge und Ereignisse, die dein eigener Code veröffentlicht.

Mit der CLI: `coworkee new Shop --webhooks true` (standardmäßig aus; mit den Beispielen senden Produkte Ereignisse). Von Hand:

```csharp
[DependsOn(typeof(CoworkeeWebhooksModule))]
public sealed class MyAppCatalogModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View, p => p.Cost);
        context.Services.AddCoworkeeWebhooks(webhooks => webhooks
            .Entity<Product>()
            .Event("order.shipped", "An order left the warehouse"));
    }
}
```

Das Modul braucht die Background Jobs und den Outbox-Prozessor (`AddCoworkeeOutboxProcessing`); beides hat jede generierte App. Es bringt die Tabellen `cw.WebhookSubscriptions` und `cw.WebhookDeliveries` mit; leg nach dem Hinzufügen eine Migration an.

## Ereignisse

| Quelle | Ereignistypen | Daten |
|---|---|---|
| `Entity<T>()` eines OData-Sets | `products.created`, `products.updated`, `products.deleted` (Name des Sets in Snake Case) | die Entity so, wie ihr OData-Set sie liefert, gelesen als Besitzer des Endpunkts |
| `IWebhookPublisher.PublishAsync(type, data)` | was du vergibst, z. B. `order.shipped` | `data`, so wie du es übergibst |
| Workflow-Übergänge (mit `Coworkee.Workflows`) | `workflow.transitioned` | der Übergang: Entity-Typ und Id, von, nach, Übergang, Benutzer, Kommentar |
| Testereignis senden | `webhook.test` | eine kurze Nachricht |

Ein Endpunkt nennt die Ereignisse, die er haben will: genaue Namen, `products.*` für alle Ereignisse eines Sets oder `*` für alles. Das Testereignis kommt immer durch.

Deine eigenen Ereignisse gehen mit der Unit of Work raus, wie alles andere, was ein Command schreibt:

```csharp
internal sealed class ShipOrderHandler(CoworkeeDbContext db, IWebhookPublisher webhooks) : IHandler<ShipOrderCommand, Result>
{
    public async Task<Result> HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.Set<Order>().SingleAsync(o => o.Id == command.OrderId, cancellationToken);
        order.Ship();
        await webhooks.PublishAsync("order.shipped", new { order.Id, order.Number, order.Carrier }, cancellationToken);
        return Result.Success();
    }
}
```

Außerhalb eines Commands rufst du `SaveChangesAsync` selbst auf. Das Ereignis landet in derselben Transaktion in der Outbox; ein zurückgerollter Command sendet also nichts.

Auch Domain Events anderer Module können zu Webhook-Ereignissen werden, ohne dass sich die Module kennen: `Coworkee:Webhooks:DomainEvents` ordnet dem Typnamen eines Domain Events einen Ereignistyp zu (`WorkflowTransitioned` → `workflow.transitioned` ist voreingestellt). Trägt so ein Ereignis `EntityType` und `EntityId` eines OData-Sets, erreicht es nur Besitzer, die diese Entity sehen dürfen.

## Was ein Endpunkt bekommt

```http
POST /hooks/coworkee HTTP/1.1
content-type: application/json; charset=utf-8
webhook-id: 0199f0c4-6c55-7a1e-9c1a-5a8b3f0e2d11
webhook-timestamp: 1760620800
webhook-signature: v1,K5oZfzN95Z9UVu1EsfQmfVNQhnkZ2pj9o9NDN/H/pI4=

{"id":"0199f0c4-6c55-7a1e-9c1a-5a8b3f0e2d11","type":"products.updated","time":"2026-10-16T13:20:00+00:00","tenant":"0199…","data":{"id":"0199…","name":"Lamp","price":19.9}}
```

`id` ist das Ereignis; es bleibt bei jeder Wiederholung und bei einer erneuten Zustellung gleich, so kann der Empfänger Doppelte verwerfen. `tenant` ist die Organisation.

**Die Daten sind das, was der Besitzer des Endpunkts lesen darf.** Besitzer ist, wer den Endpunkt zuletzt gespeichert hat. Ein Entity-Ereignis wird als diese Person über das Entity-Set gelesen: Fehlt ihr die Berechtigung des Sets oder blendet ein Zeilenfilter (`IODataEntityFilter<T>`) die Zeile aus, bekommt der Endpunkt dafür nichts; versteckte Properties des Sets und Response-Filter (`ResponseFilter<T>`) wirken wie in der API, Navigation Properties bleiben weg. Eine hart gelöschte Entity kommt nur mit ihrer Id. Daten aus `PublishAsync` gehen unverändert raus, lass also weg, was nicht jeder Admin lesen darf.

## Die Admin-Seite

*Administration → System → Webhooks* (`/cw/admin/webhooks`, Berechtigung `Webhooks.Manage`) zeigt die Endpunkte mit ihrem Zustand und die letzten Zustellungen:

- **Endpunkt hinzufügen**: Adresse, Beschreibung, Ereignisse (mit Vorschlägen aller bekannten Ereignistypen) und *Aktiv*. Das Signaturgeheimnis wird erzeugt und einmal angezeigt; über den Schlüssel-Knopf siehst du es später wieder.
- **Secret erneuern** erzeugt ein neues Geheimnis und zeigt es einmal an. Für `SecretGracePeriod` (24 Stunden) trägt jede Zustellung zwei Signaturen, eine mit dem neuen und eine mit dem alten Geheimnis, so kann der Empfänger umstellen, ohne eine Zustellung zu verlieren; die Liste zeigt, bis wann.
- **Testereignis senden** reiht eine `webhook.test`-Zustellung für den Endpunkt ein, auch wenn er abgeschaltet ist.
- **Zustellungen** zeigt Zeit, Ereignis, Endpunkt, Status mit HTTP-Code und Versuche; eine Zeile klappt Anfrage und Antwort des letzten Versuchs auf. **Erneut zustellen** schickt denselben Body noch einmal als neue Zustellung.

Geheimnis, Bodies von Anfrage und Antwort und die Endpunkte bleiben außerhalb der KI-Tools und des MCP-Servers.

## Zustellung

Zustellungen laufen über die Outbox und die Background Jobs; ein Absturz zwischen Speichern und Senden verliert also nichts. Jeder Versuch wartet höchstens `Timeout` auf eine Antwort; jeder 2xx-Status gilt als zugestellt. Weiterleitungen werden nicht verfolgt.

Ein fehlgeschlagener Versuch wird mit exponentiellem Backoff wiederholt: 30 s, 1 min, 2 min, 4 min, 8 min (jeweils doppelt so lang, gedeckelt bei `MaxRetryDelay`), insgesamt sechs Versuche. Ist eine Zustellung endgültig gescheitert, zählt der Endpunkt mit; nach `DisableAfterFailures` gescheiterten Zustellungen in Folge wird er abgeschaltet, und sein Besitzer sowie die Administratoren der Organisation bekommen eine Benachrichtigung (mit `Coworkee.Notifications` in der App). Eine erfolgreiche Zustellung setzt den Zähler zurück, das Wiedereinschalten auch.

Das Protokoll behält die Anfrage mit ihren Headern (die Signatur maskiert) und die Antwort, beide nach `LogBodyLimit` Zeichen gekürzt, für `LogRetentionDays` Tage.

## Schutz vor SSRF

Endpunkte dürfen nicht in dein eigenes Netz greifen:

- nur `https` und Port 443, weitere Ports über `AllowedPorts`;
- kein Benutzername und kein Passwort in der Adresse;
- der Hostname wird beim Senden aufgelöst und die Verbindung geht nur an öffentliche Adressen; Loopback, private Netze (10/8, 172.16/12, 192.168/16, fc00::/7), Link-Local (169.254/16 mit den Metadaten-Diensten der Clouds, fe80::/10), Carrier-Grade-NAT, Multicast und reservierte Bereiche werden abgelehnt, auch hinter IPv4-gemappten, NAT64- und 6to4-Adressen; eine DNS-Antwort kann sich zwischen Prüfung und Verbindung nicht ändern;
- Adressen, die schon wörtlich Loopback oder privat sind, werden bereits beim Speichern abgelehnt.

## Konfiguration

`Coworkee:Webhooks` in den appsettings oder der Umgebung:

| Schlüssel | Standard | |
|---|---|---|
| `RequireHttps` | `true` | `false` nur für lokale Tests |
| `AllowPrivateNetworks` | `false` | lässt Endpunkte Loopback und private Netze erreichen, für Tests und geschlossene Netze |
| `AllowedPorts` | `[]` | Ports außer 443 (und 80 ohne `RequireHttps`) |
| `Timeout` | `00:00:10` | pro Versuch |
| `MaxAttempts` | `6` | Versuche pro Zustellung, der erste mitgezählt |
| `FirstRetryDelay`, `MaxRetryDelay` | `00:00:30`, `06:00:00` | Backoff |
| `DisableAfterFailures` | `5` | gescheiterte Zustellungen in Folge, bevor der Endpunkt abgeschaltet wird |
| `LogBodyLimit` | `4096` | Zeichen von Anfrage und Antwort im Protokoll |
| `LogRetentionDays` | `30` | ein täglicher Job löscht ältere Zustellungen |
| `SecretGracePeriod` | `24:00:00` | wie lange das alte Geheimnis nach *Secret erneuern* neben dem neuen signiert |
| `DomainEvents` | `{ "WorkflowTransitioned": "workflow.transitioned" }` | Domain Events anderer Module, die zu Webhook-Ereignissen werden |

Tests, die an einen lokalen Empfänger senden, setzen `RequireHttps=false`, `AllowPrivateNetworks=true` und den Port des Empfängers in `AllowedPorts`.

## Die Signatur prüfen

Die Signatur folgt der Spezifikation [Standard Webhooks](https://www.standardwebhooks.com/), deren Bibliotheken für viele Sprachen also direkt passen. Signiert wird `{webhook-id}.{webhook-timestamp}.{roher Body}`, der Schlüssel ist der Base64-Teil des `whsec_…`-Geheimnisses, der Algorithmus HMAC-SHA256, und der Header enthält `v1,{Base64-Signatur}` (mehrere, durch Leerzeichen getrennt, während ein Geheimnis wechselt). Prüf den **rohen** Body, bevor du ihn parst, vergleich in konstanter Zeit und lehn alte Zeitstempel ab (fünf Minuten sind üblich).

=== "C#"

    Mit dem Paket erledigt `WebhookSignature.Verify` alles:

    ```csharp
    app.MapPost("/hooks/coworkee", async (HttpRequest request, IConfiguration configuration) =>
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var headers = request.Headers;
        var valid = WebhookSignature.Verify(configuration["Coworkee:WebhookSecret"]!, headers["webhook-id"]!, headers["webhook-timestamp"]!, body,
            headers["webhook-signature"]!, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        return valid ? Results.Ok() : Results.Unauthorized();
    });
    ```

    Ohne das Paket:

    ```csharp
    static bool Verify(string secret, string id, string timestamp, string body, string signatures)
    {
        if (!long.TryParse(timestamp, out var seconds) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300)
        {
            return false;
        }

        var key = secret.StartsWith("whsec_") ? Convert.FromBase64String(secret[6..]) : Encoding.UTF8.GetBytes(secret);
        var expected = Encoding.ASCII.GetBytes("v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"))));
        return signatures.Split(' ').Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), expected));
    }
    ```

=== "Node"

    ```js
    import crypto from 'node:crypto';
    import express from 'express';

    function verify(secret, headers, rawBody, toleranceSeconds = 300) {
      const id = headers['webhook-id'], timestamp = headers['webhook-timestamp'], signatures = headers['webhook-signature'] ?? '';
      if (Math.abs(Date.now() / 1000 - Number(timestamp)) > toleranceSeconds) return false;
      const key = secret.startsWith('whsec_') ? Buffer.from(secret.slice(6), 'base64') : Buffer.from(secret);
      const expected = Buffer.from('v1,' + crypto.createHmac('sha256', key).update(`${id}.${timestamp}.`).update(rawBody).digest('base64'));
      return signatures.split(' ').some(s => s.length === expected.length && crypto.timingSafeEqual(Buffer.from(s), expected));
    }

    const app = express();
    app.post('/hooks/coworkee', express.raw({ type: 'application/json' }), (req, res) => {
      if (!verify(process.env.COWORKEE_WEBHOOK_SECRET, req.headers, req.body)) return res.sendStatus(401);
      const event = JSON.parse(req.body);
      console.log(event.type, event.data);
      res.sendStatus(204);
    });
    ```

=== "Python"

    ```python
    import base64, hashlib, hmac, os, time
    from flask import Flask, request, abort

    SECRET = os.environ["COWORKEE_WEBHOOK_SECRET"]

    def verify(secret: str, headers, body: bytes, tolerance: int = 300) -> bool:
        msg_id, timestamp = headers["webhook-id"], headers["webhook-timestamp"]
        if abs(time.time() - int(timestamp)) > tolerance:
            return False
        key = base64.b64decode(secret[6:]) if secret.startswith("whsec_") else secret.encode()
        digest = hmac.new(key, f"{msg_id}.{timestamp}.".encode() + body, hashlib.sha256).digest()
        expected = "v1," + base64.b64encode(digest).decode()
        return any(hmac.compare_digest(expected, s) for s in headers.get("webhook-signature", "").split())

    app = Flask(__name__)

    @app.post("/hooks/coworkee")
    def coworkee():
        if not verify(SECRET, request.headers, request.get_data()):
            abort(401)
        event = request.get_json()
        print(event["type"], event["data"])
        return "", 204
    ```

## n8n und Zapier

**n8n**: Leg einen *Webhook*-Knoten mit der Methode `POST` an und schalte in seinen Optionen *Raw Body* ein. Seine Production-URL trägst du als Adresse des Endpunkts ein (n8n muss über https erreichbar sein). Ein *Code*-Knoten prüft die Signatur, bevor der Workflow weiterläuft; erlaube das Modul mit `NODE_FUNCTION_ALLOW_BUILTIN=crypto`:

```js
const crypto = require('crypto');
const secret = $env.COWORKEE_WEBHOOK_SECRET;
const headers = $input.first().json.headers;
const body = (await this.helpers.getBinaryDataBuffer(0, 'data')).toString('utf8');
const key = Buffer.from(secret.slice(6), 'base64');
const expected = 'v1,' + crypto.createHmac('sha256', key).update(`${headers['webhook-id']}.${headers['webhook-timestamp']}.${body}`).digest('base64');
if (!headers['webhook-signature'].split(' ').includes(expected)) throw new Error('Invalid signature');
return [{ json: JSON.parse(body) }];
```

Ein *IF*-Knoten auf `{{$json.type}}` schickt danach `products.created` in die eine und `order.shipped` in die andere Richtung.

**Zapier**: Nimm den Trigger *Webhooks by Zapier → Catch Raw Hook* und trag seine URL als Adresse ein. Raw Hooks behalten den Body so, wie er gesendet wurde, und reichen die Header durch. Füg *Code by Zapier → Run JavaScript* hinzu, gib den rohen Body und die drei `webhook-*`-Header als Input Data hinein und prüf dort wie im Node-Beispiel; wirf einen Fehler, wenn die Signatur nicht passt, dann hält der Zap an. Danach parst du den Body und nutzt `type` und `data` in den folgenden Schritten. Mit *Testereignis senden* auf der Admin-Seite bekommst du ein erstes Beispiel in den Zap-Editor.

## Endpunkte der API

| Endpunkt | |
|---|---|
| `GET /api/v1/webhooks` | Endpunkte der Organisation |
| `POST /api/v1/webhooks` | anlegen mit `{ "url", "events", "description", "isActive", "secret"? }`, antwortet mit dem Geheimnis |
| `PUT/DELETE /api/v1/webhooks/{id}` | ändern (ein `secret` ersetzt es) / mit seinen Zustellungen löschen |
| `GET /api/v1/webhooks/{id}/secret` | das Signaturgeheimnis |
| `POST /api/v1/webhooks/{id}/test` | ein Testereignis einreihen |
| `GET /api/v1/webhooks/event-types` | bekannte Ereignistypen |
| `GET /api/v1/webhooks/deliveries?subscriptionId=&take=` | die letzten Zustellungen |
| `POST /api/v1/webhooks/deliveries/{id}/redeliver` | eine Zustellung noch einmal senden |

Alle brauchen `Webhooks.Manage`.
