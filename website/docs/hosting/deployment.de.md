# Deployment

Der App-Host, der die App auf deinem Rechner startet, beschreibt auch ihr Deployment. `coworkee deploy` veröffentlicht ihn für ein Ziel: Docker Compose für einen einzelnen Linux-Host, Azure Container Apps als Bicep, Kubernetes als Helm-Chart.

| Ziel | Befehl | Stand |
|---|---|---|
| Docker Compose | `coworkee deploy compose` | geprüft: eine generierte App läuft aus den veröffentlichten Dateien hinter einem HTTPS-Proxy, die Anmeldung über die Web-App funktioniert |
| Azure Container Apps | `coworkee deploy azure` | Bicep erzeugt, jede Datei kompiliert mit `az bicep build`; nicht nach Azure deployt |
| Kubernetes | `coworkee deploy k8s` | Helm-Chart erzeugt, `helm lint` und `kubeconform -strict` laufen durch; auf keinem Cluster installiert |

## Was sich im Deployment ändert

Beim Veröffentlichen läuft der App-Host im Publish-Modus mit der Umgebung `Production`. Die App unterscheidet sich dann so vom lokalen Lauf:

| Lokaler Lauf | Deployment |
|---|---|
| Jeder Dienst auf seinem https-Endpunkt | die öffentlichen Adressen hinter deinem Reverse Proxy, als Parameter `<app>-auth-url` und `<app>-web-url` (und `<app>-airworks-url`) |
| Entwicklungszertifikate für die Tokens | zwei Zertifikate aus den Parametern `<app>-signing-certificate` und `<app>-encryption-certificate` (PKCS#12 als Base64), Issuer ist die öffentliche Adresse des Auth-Servers |
| Demo-Benutzer aus `DemoUsers.cs` | keine Benutzer: Du richtest das System im Browser mit dem Setup-Token ein (Parameter `<app>-setup-token`) |
| Mailpit, LibreTranslate, Ollama, Keycloak, Grafana, WebDataStudio, AspireUI | bleiben weg; Mailserver und Übersetzungs-Endpunkt stellst du auf den Einstellungsseiten ein, den Identity Provider bringst du selbst mit |
| Dateien in `.data/blobs` | ein Volume `<app>-files`, das sich die Dienste teilen (Docker Compose) |
| Volumes nach dem Ordner des App-Hosts benannt | `<app>-postgres-data`, `<app>-redis-data`, ...: Veröffentlichst du aus einem anderen Checkout, findet das Deployment dieselben Daten |

Gleich bleibt: Die Migrationen laufen einmal als eigener Dienst, bevor die anderen starten (und versuchen es eine Minute lang, solange der Datenbankserver hochfährt; auf Azure und Kubernetes werden sie ein Job), jeder Dienst hat `/health` und `/alive`, die Data-Protection-Schlüssel der Dienste mit Datenbank liegen darin (ein Schlüsselring für API, Auth-Server und Worker), die Web-App hält ihre Anmeldesitzungen und deren Schlüsselring in Redis, und Aspire setzt `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, damit die Dienste Schema und Host vom Proxy übernehmen.

## Docker Compose

```bash
coworkee deploy compose
```

schreibt `deploy/compose` in die Solution (`-o` für einen anderen Ordner):

- `docker-compose.yaml`: Postgres oder SQL Server, Redis, die Migrationen, Auth-Server, API, Web-App und das Aspire-Dashboard für Logs und Traces.
- `.env`: jeder Parameter. Das Tool füllt, was es kann: zufällige Passwörter und das Setup-Token, die beiden Token-Zertifikate, die Images und die Ports. Werte eines früheren Laufs bleiben erhalten, ein zweites `coworkee deploy` ändert also weder Passwörter noch Zertifikate.
- die Images `<app>-migrations`, `<app>-auth`, `<app>-api`, `<app>-web` in deinem lokalen Docker (`dotnet publish -t:PublishContainer`; `--no-build` lässt das weg).

Die öffentlichen Adressen trägst du in `.env` ein:

```bash
SHOP_AUTH_URL=https://auth.example.com
SHOP_WEB_URL=https://shop.example.com
```

Die Dienste lauschen auf dem Loopback des Hosts, in der Reihenfolge, in der sie hinzugefügt wurden: Auth-Server `127.0.0.1:8080`, API `8081`, Web-App `8082` (AirWorks `8083`). Davor beendet ein Reverse Proxy HTTPS, zum Beispiel Caddy, der sich die Zertifikate bei Let's Encrypt holt:

```caddyfile
auth.example.com {
	reverse_proxy 127.0.0.1:8080
}

shop.example.com {
	reverse_proxy 127.0.0.1:8082
}
```

Auch die Dienste erreichen den Auth-Server unter seiner öffentlichen Adresse (die Web-App meldet sich dort an, die API prüft Tokens dagegen). Der Host muss den Namen also auflösen und dem Zertifikat vertrauen, was bei einem öffentlichen Zertifikat gegeben ist.

Dann auf dem Host (kopiere den Ordner und lade die Images dort, oder baue sie dort mit `coworkee deploy compose`):

```bash
docker compose up -d
```

Öffne die Web-Adresse: Die App zeigt die Setup-Seite. Gib `SHOP_SETUP_TOKEN` aus `.env` ein, den Namen deiner Organisation und den ersten Administrator.

### Auf deinem Rechner ausprobieren

Docker Desktop reicht. Namen unter `localtest.me` zeigen auf `127.0.0.1`; ein Caddy-Container mit eigener Zertifizierungsstelle spielt den Proxy und ist den anderen Containern unter denselben Namen bekannt. Neben `docker-compose.yaml`:

```caddyfile title="Caddyfile"
app.localtest.me:8443 {
	tls internal
	reverse_proxy shop-web:8080
}

auth.localtest.me:8443 {
	tls internal
	reverse_proxy shop-auth:8080
}
```

```yaml title="compose.proxy.yaml"
services:
  caddy:
    image: caddy:2.10
    ports:
      - "8443:8443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
    networks:
      aspire:
        aliases: [app.localtest.me, auth.localtest.me]
  shop-auth: &trust
    environment:
      SSL_CERT_FILE: /etc/ssl/caddy-root.crt
    volumes:
      - ./caddy-root.crt:/etc/ssl/caddy-root.crt:ro
    depends_on:
      caddy:
        condition: service_started
  shop-api: *trust
  shop-web: *trust
volumes:
  caddy-data: {}
```

```bash
# in .env: SHOP_AUTH_URL=https://auth.localtest.me:8443 und SHOP_WEB_URL=https://app.localtest.me:8443
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d caddy
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt caddy-root.crt
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d
```

Die Dienste vertrauen Caddys eigener Zertifizierungsstelle über `caddy-root.crt`; der Browser warnt davor, das ist hier so gewollt. Manche Router (zum Beispiel eine Fritz!Box) lösen Namen nicht auf, die auf `127.0.0.1` zeigen; dann trag `127.0.0.1 app.localtest.me auth.localtest.me` in deine hosts-Datei ein.

### Dein eigener Identity Provider

Das Keycloak des App-Hosts ist ein Entwicklungs-Realm mit den Demo-Benutzern und wird nicht veröffentlicht. Willst du dich über dein Keycloak (oder einen anderen OpenID-Connect-Provider) anmelden, gib Auth-Server und API den Provider in `docker-compose.yaml` oder einer Override-Datei mit:

```yaml
  shop-auth:
    environment:
      Coworkee__Auth__External__Mode: Both
      Coworkee__Auth__External__Providers__keycloak__DisplayName: Keycloak
      Coworkee__Auth__External__Providers__keycloak__Authority: https://login.example.com/realms/shop
      Coworkee__Auth__External__Providers__keycloak__ClientId: shop-auth
      Coworkee__Auth__External__Providers__keycloak__ClientSecret: ${KEYCLOAK_CLIENT_SECRET}
  shop-api:
    environment:
      Coworkee__Auth__External__Mode: Both
      Coworkee__Auth__External__Providers__keycloak__DisplayName: Keycloak
```

Der Client im Realm leitet auf `https://auth.example.com/signin-keycloak` zurück. In einem Deployment ist dieser Weg noch nicht geprüft.

## Azure Container Apps

```bash
coworkee deploy azure
az bicep build --file deploy/azure/main.bicep
```

schreibt `main.bicep` mit einer Container-Apps-Umgebung und einer Registry, dazu eine Bicep-Datei pro Dienst; die Postgres- und Redis-Container werden Container-Apps mit Volumes, die Migrationen ein Container-Apps-Job. Für Produktion nimmst du die verwalteten Dienste und gibst sie der App (mit `Aspire.Hosting.Azure.PostgreSQL` im App-Host):

```csharp
var database = builder.AddAzurePostgresFlexibleServer("postgres").RunAsContainer().AddDatabase("myapp");
var app = builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database));
```

Dateien brauchen einen Speicher, den alle Replikate teilen: Erzeuge die App mit `--storage azureBlob`. Deployt wird mit `azd init` und `azd up`; dort setzt du die Parameter (öffentliche Adressen, die beiden Zertifikate, das Setup-Token; wie man sie erzeugt, zeigt `coworkee deploy compose`). Der Migrations-Job läuft, wenn du ihn startest: `az containerapp job start -n shop-migrations -g <ressourcengruppe>`, nach jedem Deployment. **Nicht geprüft:** Das Bicep kompiliert, ein echtes Deployment gab es noch nicht.

## Kubernetes

```bash
coworkee deploy k8s
helm lint deploy/k8s
helm template deploy/k8s | kubeconform -strict -summary
```

schreibt ein Helm-Chart (`Chart.yaml`, `values.yaml`, `templates/`) mit einem Deployment und einem Service pro Dienst. Aspires Kubernetes-Publisher ist eine Preview; `coworkee deploy` repariert zwei Dinge in seiner Ausgabe: Einstellungsnamen mit Punkt werden mit `index` gelesen, und die Migrationen werden ein Job, den Helm nach jeder Installation und jedem Upgrade startet.

Fülle `values.yaml`: die öffentlichen Adressen (`shop_web_url`, `shop_auth_url`, pro Dienst wiederholt), die Secrets (Connection-Strings, Passwörter, Zertifikate, Setup-Token) und die Images, die du in deine Registry schiebst (`dotnet publish -t:PublishContainer -p:ContainerRegistry=...`). Ingress und TLS liefert dein Cluster: Richte die öffentlichen Adressen auf die Web-App und den Auth-Server.

**Nicht geprüft:** Das Chart besteht Lint und Validierung, auf einem Cluster lief es noch nicht. Die Volumes des Charts sind `emptyDir`, Postgres- und Redis-Daten und die Dateien gehen also mit dem Pod verloren: Nimm verwaltete Datenbanken (`UseDatabase`, `UseRedis`), Azure-Blob-Speicher, oder stell die Volumes auf Persistent Volume Claims um, bevor du dich darauf verlässt.

## Eine eigene Umgebung

`Coworkee:Deploy:Target` (`compose`, `azure`, `k8s`) wählt die Umgebung, die `coworkee deploy` übergibt. Fügt der App-Host vor `AddCoworkeeApp` selbst eine Compute-Umgebung hinzu, bleibt es bei ihr, zum Beispiel `builder.AddDockerComposeEnvironment("compose").WithDashboard(false)`. `aspire publish` geht auch, sobald die Aspire-CLI zur Aspire-Version des App-Hosts passt; `coworkee deploy` startet den App-Host direkt und braucht sie nicht.
