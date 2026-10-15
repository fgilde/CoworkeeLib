# Webhooks

`Coworkee.Webhooks` lets other systems hear about what happens in your app without polling. Admins of an organisation register endpoints; each endpoint gets a signed `POST` for the events it chose. Events are changes of the entity sets you opt in, workflow transitions, and events your own code publishes.

With the CLI: `coworkee new Shop --webhooks true` (off by default; with the samples, products send events). By hand:

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

The module needs the background jobs and the outbox processor (`AddCoworkeeOutboxProcessing`), both part of every generated app. It adds the tables `cw.WebhookSubscriptions` and `cw.WebhookDeliveries`; create a migration after adding it.

## Events

| Source | Event types | Data |
|---|---|---|
| `Entity<T>()` of an OData set | `products.created`, `products.updated`, `products.deleted` (the set name in snake case) | the entity as its OData set returns it, read as the endpoint's owner |
| `IWebhookPublisher.PublishAsync(type, data)` | what you name, e.g. `order.shipped` | `data` as you pass it |
| Workflow transitions (with `Coworkee.Workflows`) | `workflow.transitioned` | the transition: entity type and id, from, to, transition, user, comment |
| Send test event | `webhook.test` | a short message |

An endpoint lists the events it wants: exact names, `products.*` for every event of a set, or `*` for all. The test event always goes through.

Your own events go out with the unit of work, like everything else written in a command:

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

Outside a command, call `SaveChangesAsync` yourself. The event is written to the outbox in the same transaction, so a rolled back command sends nothing.

Other modules' domain events can become webhook events too, without a reference between the modules: `Coworkee:Webhooks:DomainEvents` maps the type name of a domain event to an event type (`WorkflowTransitioned` → `workflow.transitioned` is the default). A mapped event that carries `EntityType` and `EntityId` of an OData set only reaches owners who can see that entity.

## What an endpoint receives

```http
POST /hooks/coworkee HTTP/1.1
content-type: application/json; charset=utf-8
webhook-id: 0199f0c4-6c55-7a1e-9c1a-5a8b3f0e2d11
webhook-timestamp: 1760620800
webhook-signature: v1,K5oZfzN95Z9UVu1EsfQmfVNQhnkZ2pj9o9NDN/H/pI4=

{"id":"0199f0c4-6c55-7a1e-9c1a-5a8b3f0e2d11","type":"products.updated","time":"2026-10-16T13:20:00+00:00","tenant":"0199…","data":{"id":"0199…","name":"Lamp","price":19.9}}
```

`id` is the event; it stays the same for every retry and for a redelivery, so receivers can drop duplicates. `tenant` is the organisation.

**The data is what the owner of the endpoint may read.** The owner is whoever saved the endpoint last. An entity event is read through the entity set as that person: without the set's permission, or when a row filter (`IODataEntityFilter<T>`) hides the row, the endpoint gets nothing for it; hidden properties of the set and response filters (`ResponseFilter<T>`) apply as in the API, and navigation properties are left out. A hard deleted entity is sent as its id only. Data of `PublishAsync` is sent as it is, so leave out what not every admin may read.

## The admin page

*Administration → System → Webhooks* (`/admin/webhooks`, permission `Webhooks.Manage`) lists the endpoints with their state and the latest deliveries:

- **Add endpoint**: address, description, events (with suggestions of every known event type) and *Active*. The signing secret is generated and shown once; the key button shows it again later.
- **Send test event** queues a `webhook.test` delivery to the endpoint, even when it is switched off.
- **Deliveries** shows time, event, endpoint, status with the HTTP code and the attempts; a row opens the request and the response of the last attempt. **Redeliver** sends the same body again as a new delivery.

The secret, the request and response bodies and the endpoints stay out of the AI tools and the MCP server.

## Delivery

Deliveries leave through the outbox and the background jobs, so a crash between saving and sending loses nothing. Each attempt waits at most `Timeout` for an answer; any 2xx counts as delivered. Redirects are not followed.

A failed attempt is retried with exponential backoff: 30 s, 1 min, 2 min, 4 min, 8 min (doubling, capped at `MaxRetryDelay`), six attempts in all. When a delivery has failed for good, the endpoint counts it; after `DisableAfterFailures` failed deliveries in a row the endpoint is switched off, and its owner and the organisation's administrators get a notification (with `Coworkee.Notifications` in the app). A successful delivery resets the count; switching the endpoint on again does too.

The log keeps the request with its headers (the signature masked) and the answer, both cut after `LogBodyLimit` characters, for `LogRetentionDays` days.

## Protection against SSRF

Endpoints may not reach into your own network:

- only `https` and port 443, more ports through `AllowedPorts`;
- no user name or password in the address;
- the host name is resolved when sending and the connection goes only to public addresses, so loopback, private (10/8, 172.16/12, 192.168/16, fc00::/7), link-local (169.254/16 with the cloud metadata services, fe80::/10), carrier-grade NAT, multicast and reserved ranges are refused, also behind IPv4-mapped, NAT64 and 6to4 addresses; a DNS answer cannot change between check and connect;
- addresses that are loopback or private literally are already refused when saving.

## Configuration

`Coworkee:Webhooks` in the appsettings or the environment:

| Key | Default | |
|---|---|---|
| `RequireHttps` | `true` | `false` only for local tests |
| `AllowPrivateNetworks` | `false` | lets endpoints reach loopback and private networks, for tests and closed networks |
| `AllowedPorts` | `[]` | ports besides 443 (and 80 without `RequireHttps`) |
| `Timeout` | `00:00:10` | per attempt |
| `MaxAttempts` | `6` | attempts per delivery, the first included |
| `FirstRetryDelay`, `MaxRetryDelay` | `00:00:30`, `06:00:00` | backoff |
| `DisableAfterFailures` | `5` | failed deliveries in a row before the endpoint is switched off |
| `LogBodyLimit` | `4096` | characters of request and response in the log |
| `LogRetentionDays` | `30` | a daily job deletes older deliveries |
| `DomainEvents` | `{ "WorkflowTransitioned": "workflow.transitioned" }` | domain events of other modules that become webhook events |

Tests that send to a local receiver set `RequireHttps=false`, `AllowPrivateNetworks=true` and the receiver's port in `AllowedPorts`.

## Verifying the signature

The signature follows the [Standard Webhooks](https://www.standardwebhooks.com/) spec, so its libraries for many languages work as they are. The signed content is `{webhook-id}.{webhook-timestamp}.{raw body}`, the key is the base64 part of the `whsec_…` secret, the algorithm HMAC-SHA256, and the header holds `v1,{base64 signature}` (several separated by spaces while a secret changes). Check the **raw** body before you parse it, compare in constant time and reject old timestamps (five minutes is common).

=== "C#"

    With the package, `WebhookSignature.Verify` does it all:

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

    Without it:

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

## n8n and Zapier

**n8n**: add a *Webhook* node with the method `POST` and switch on *Raw Body* in its options. Enter its production URL as the endpoint's address (n8n must be reachable over https). A *Code* node verifies the signature before the workflow goes on; allow the module with `NODE_FUNCTION_ALLOW_BUILTIN=crypto`:

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

An *IF* node on `{{$json.type}}` then sends `products.created` one way and `order.shipped` another.

**Zapier**: use the trigger *Webhooks by Zapier → Catch Raw Hook* and enter its URL as the address. Raw hooks keep the body as it was sent and pass the headers along. Add *Code by Zapier → Run JavaScript*, map the raw body and the three `webhook-*` headers into its input data, and verify there as in the Node example; throw when the signature does not match, so the Zap stops. Then parse the body and use `type` and `data` in the following steps. With *Send test event* on the admin page you get a first sample into the Zap editor.

## Endpoints

| Endpoint | |
|---|---|
| `GET /api/v1/webhooks` | endpoints of the organisation |
| `POST /api/v1/webhooks` | add with `{ "url", "events", "description", "isActive", "secret"? }`, answers with the secret |
| `PUT/DELETE /api/v1/webhooks/{id}` | change (a `secret` replaces it) / delete with its deliveries |
| `GET /api/v1/webhooks/{id}/secret` | the signing secret |
| `POST /api/v1/webhooks/{id}/test` | queue a test event |
| `GET /api/v1/webhooks/event-types` | known event types |
| `GET /api/v1/webhooks/deliveries?subscriptionId=&take=` | the latest deliveries |
| `POST /api/v1/webhooks/deliveries/{id}/redeliver` | send a delivery again |

All of them need `Webhooks.Manage`.
