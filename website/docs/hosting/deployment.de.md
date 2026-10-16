# Deployment

Der App-Host, der die App auf deinem Rechner startet, beschreibt auch ihr Deployment. `coworkee deploy` veröffentlicht ihn für ein Ziel: Docker Compose für einen einzelnen Linux-Host, Azure Container Apps als Bicep, Kubernetes als Helm-Chart. Die Tools deines App-Hosts (Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI) kommen mit, jedes mit eigenem Volume und, wo es eine Weboberfläche hat, einer öffentlichen Adresse.

| Ziel | Befehl | Stand |
|---|---|---|
| Docker Compose | `coworkee deploy compose` | geprüft: eine generierte App mit Keycloak, Grafana, WebDataStudio und LibreTranslate läuft aus den veröffentlichten Dateien hinter einem HTTPS-Proxy; der angelegte Administrator meldet sich mit Passwort und über Keycloak an, Grafana zeigt die Daten der App, WebDataStudio öffnet die Datenbank, die maschinelle Übersetzung funktioniert. Ollama und AspireUI werden veröffentlicht und in den Dateien geprüft, aber nicht gestartet |
| Azure Container Apps | `coworkee deploy azure` | Bicep mit den Tools erzeugt, jede Datei kompiliert mit `az bicep build`; nicht nach Azure deployt |
| Kubernetes | `coworkee deploy k8s` | Helm-Chart mit den Tools erzeugt, `helm lint` und `kubeconform -strict` laufen durch; auf keinem Cluster installiert |

## Was sich im Deployment ändert

Beim Veröffentlichen läuft der App-Host im Publish-Modus mit der Umgebung `Production`. Die App unterscheidet sich dann so vom lokalen Lauf:

| Lokaler Lauf | Deployment |
|---|---|
| Jeder Dienst auf seinem https-Endpunkt | die öffentlichen Adressen hinter deinem Reverse Proxy, als Parameter `<app>-auth-url` und `<app>-web-url` (und `<app>-airworks-url`) |
| Entwicklungszertifikate für die Tokens | zwei Zertifikate aus den Parametern `<app>-signing-certificate` und `<app>-encryption-certificate` (PKCS#12 als Base64), Issuer ist die öffentliche Adresse des Auth-Servers |
| Demo-Benutzer aus `DemoUsers.cs` | ein Administrator aus den Parametern `<app>-admin-email` und `<app>-admin-password`, den die Migrationen in einer leeren Datenbank anlegen (siehe [Der erste Administrator](#der-erste-administrator)) |
| Mailpit | dein Mailserver aus den Parametern `<app>-smtp-host`, `-smtp-port`, `-smtp-username`, `-smtp-password` und `-smtp-from`; Mailpit nur, wenn du es willst (siehe [Mail](#mail)) |
| Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI | werden mit deployt, außer du lässt eins weg (siehe [Tools](#tools)) |
| Dateien in `.data/blobs` | ein Volume `<app>-files`, das sich die Dienste teilen (Docker Compose) |
| Volumes nach dem Ordner des App-Hosts benannt | `<app>-postgres-data`, `<app>-redis-data`, ...: Veröffentlichst du aus einem anderen Checkout, findet das Deployment dieselben Daten |

Gleich bleibt: Die Migrationen laufen einmal als eigener Dienst, bevor die anderen starten (und versuchen es eine Minute lang, solange der Datenbankserver hochfährt; auf Azure und Kubernetes werden sie ein Job), jeder Dienst hat `/health` und `/alive`, die Data-Protection-Schlüssel der Dienste mit Datenbank liegen darin (ein Schlüsselring für API, Auth-Server und Worker), die Web-App hält ihre Anmeldesitzungen und deren Schlüsselring in Redis, und Aspire setzt `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, damit die Dienste Schema und Host vom Proxy übernehmen.

## Tools

Jedes Tool, das der App-Host nutzt, kommt ins Deployment. Du lässt eins für ein einzelnes Deployment oder dauerhaft weg:

```bash
coworkee deploy compose --without grafana --without ollama
```

```csharp
var app = builder.AddCoworkeeApp("shop", options => options.UseGrafana().DeployTool(CoworkeeTools.Grafana, false));
```

`--without` und `--with` setzen `Coworkee:Deploy:Tools:<tool>`, das gewinnt gegen `DeployTool`. Die Tools heißen `keycloak`, `mailpit`, `libretranslate`, `grafana`, `webdatastudio`, `ollama` und `aspireui`. Eigener Code im App-Host fragt `app.Deploys(CoworkeeTools.Ollama)`, so wie das Template Ollama und AspireUI hinzufügt.

| Tool | Im Deployment |
|---|---|
| Keycloak | der Realm der App mit den Clients des Auth-Servers und der Tools, dazu der erste Administrator als Benutzer mit der Rolle `Administrator`; die Migrationen gleichen ihn bei jedem Deployment ab. Öffentliche Adresse `<app>-keycloak-url`, Daten in `<app>-keycloak-data` (Keycloaks eigene Dateidatenbank), seine Konsole als `admin` mit `KEYCLOAK_PASSWORD`. Demo-Benutzer und Login-Theme bleiben lokal |
| Grafana | mit Prometheus, Loki, Tempo und dem Collector; die Dashboards der App und die Konfiguration stecken in den Images (`<app>-grafana`, ...). Der Administrator meldet sich mit Passwort oder über Keycloak an. Öffentliche Adresse `<app>-grafana-url`, Daten in `<app>-grafana-data`, `<app>-prometheus-data` und `<app>-loki-data`; Tempo hält seine Traces im Container |
| WebDataStudio | mit der App-Datenbank verbunden; der Administrator meldet sich mit Passwort oder über Keycloak an. Öffentliche Adresse `<app>-webdatastudio-url`, Daten in `<app>-webdatastudio-data` |
| LibreTranslate | der Übersetzungsdienst der App, nur im internen Netz; die Modelle liegen in `<app>-libretranslate-models`, der erste Start lädt sie herunter |
| Ollama | der Modellserver der App, nur im internen Netz, Modelle in `<app>-ollama-data`. Er braucht einen Host mit genug Speicher |
| AspireUI | mit der Anmeldung des Administrators (und Keycloak), öffentliche Adresse `<app>-aspireui-url`, Daten in `<app>-aspireui-data`. Es steuert das Docker des Hosts über `/var/run/docker.sock` und passt deshalb zu Docker Compose |
| Mailpit | nur mit `--with mailpit`, für ein Test-Deployment: Jede Mail landet dort, seine Weboberfläche (`<app>-mail-url`) fragt nach E-Mail und Passwort des Administrators |

Jedes Tool mit Weboberfläche steht wie die Dienste hinter deinem Reverse Proxy; seine öffentliche Adresse erscheint auch auf der Admin-Seite **Dienste** (`Coworkee:Services`).

## Der erste Administrator

Ist die Datenbank leer, legen die Migrationen den Administrator aus `<app>-admin-email` und `<app>-admin-password` an, mit der Organisation und den Einstellungen des Templates, und der Realm eines mitdeployten Keycloak bekommt denselben Benutzer. `coworkee deploy compose` fragt nach der E-Mail (oder nimmt `--admin-email`, eine Umgebungsvariable `SHOP_ADMIN_EMAIL` oder `admin@<app>.local`), erzeugt das Passwort und gibt beides am Ende seiner Ausgabe aus, sonst nirgends. Mit derselben E-Mail und demselben Passwort meldest du dich an der App, an Keycloak, Grafana, WebDataStudio, AspireUI und Mailpit an.

Lässt du beide Werte leer, wird nichts angelegt: Die App zeigt ihre Setup-Seite, auf der du mit `<app>-setup-token` Organisation und Administrator selbst einrichtest. Eine eingerichtete Datenbank bleibt, wie sie ist; ein geändertes Passwort in `.env` erreicht Keycloak und die Tools, nicht das Konto in der App.

## Mail

Ein Deployment verschickt über deinen Mailserver. `coworkee deploy compose` fragt nach Host, Port, Benutzer, Passwort und Absender (oder liest `SHOP_SMTP_HOST`, ... aus der Umgebung) und schreibt sie in `.env`; was du leer lässt, trägst du später ein. Sie werden die Vorgaben der Mail-Einstellungen, die Administratoren auf der Seite der Mail-Einstellungen ändern. `UseMail(endpoint)` im App-Host nimmt stattdessen eine andere SMTP-Ressource, `--with mailpit` nimmt Mailpit.

## Docker Compose

```bash
coworkee deploy compose
```

schreibt `deploy/compose` in die Solution (`-o` für einen anderen Ordner):

- `docker-compose.yaml`: Postgres oder SQL Server, Redis, die Migrationen, Auth-Server, API, Web-App, die Tools und das Aspire-Dashboard für Logs und Traces.
- `.env`: jeder Parameter. Das Tool füllt, was es kann: zufällige Passwörter und das Setup-Token, den Administrator, die beiden Token-Zertifikate, das Schlüsselpaar für Web-Push (mit Benachrichtigungen und `pwa`), die Images und die Ports. Nach dem Mailserver fragt es in der Konsole. Eine Umgebungsvariable gleichen Namens (`SHOP_WEB_URL=...`) wird übernommen. Werte eines früheren Laufs bleiben erhalten, ein zweites `coworkee deploy` ändert also weder Passwörter noch Zertifikate.
- die Images `<app>-migrations`, `<app>-auth`, `<app>-api`, `<app>-web` (`dotnet publish -t:PublishContainer`) und die der Tools, die Dateien der App mitbringen (`<app>-grafana`, `<app>-prometheus`, ..., `docker build`), in deinem lokalen Docker; `--no-build` lässt das weg.

Am Ende listet es jeden Dienst und jedes Tool mit Loopback-Port und öffentlicher Adresse und sagt, wie du dich anmeldest.

Die öffentlichen Adressen trägst du in `.env` ein:

```bash
SHOP_AUTH_URL=https://auth.example.com
SHOP_WEB_URL=https://shop.example.com
SHOP_KEYCLOAK_URL=https://login.example.com
SHOP_GRAFANA_URL=https://grafana.example.com
SHOP_WEBDATASTUDIO_URL=https://data.example.com
```

Die Dienste lauschen auf dem Loopback des Hosts, in der Reihenfolge, in der sie hinzugefügt wurden: Auth-Server `127.0.0.1:8080`, API `8081`, Web-App `8082` (AirWorks `8083`). Die Tools haben feste Ports: Keycloak `8090`, Grafana `8091`, WebDataStudio `8092`, AspireUI `8093`, Mailpit `8094`. Davor beendet ein Reverse Proxy HTTPS, zum Beispiel Caddy, der sich die Zertifikate bei Let's Encrypt holt:

```caddyfile
auth.example.com {
	reverse_proxy 127.0.0.1:8080
}

shop.example.com {
	reverse_proxy 127.0.0.1:8082
}

login.example.com {
	reverse_proxy 127.0.0.1:8090
}

grafana.example.com {
	reverse_proxy 127.0.0.1:8091
}

data.example.com {
	reverse_proxy 127.0.0.1:8092
}
```

Auch die Dienste erreichen den Auth-Server unter seiner öffentlichen Adresse (die Web-App meldet sich dort an, die API prüft Tokens dagegen). Der Host muss den Namen also auflösen und dem Zertifikat vertrauen, was bei einem öffentlichen Zertifikat gegeben ist.

Dann auf dem Host (kopiere den Ordner und lade die Images dort, oder baue sie dort mit `coworkee deploy compose`):

```bash
docker compose up -d
```

Öffne die Web-Adresse und melde dich als der Administrator an, den `coworkee deploy` ausgegeben hat. Hast du den Administrator leer gelassen, zeigt die App stattdessen die Setup-Seite: Gib `SHOP_SETUP_TOKEN` aus `.env` ein, den Namen deiner Organisation und den ersten Administrator.

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

keycloak.localtest.me:8443 {
	tls internal
	reverse_proxy keycloak:8080
}

grafana.localtest.me:8443 {
	tls internal
	reverse_proxy grafana:3000
}

data.localtest.me:8443 {
	tls internal
	reverse_proxy webdatastudio:8080
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
        aliases: [app.localtest.me, auth.localtest.me, keycloak.localtest.me, grafana.localtest.me, data.localtest.me]
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
# die Adressen als Umgebungsvariablen: coworkee deploy übernimmt sie in .env
export SHOP_AUTH_URL=https://auth.localtest.me:8443 SHOP_WEB_URL=https://app.localtest.me:8443 SHOP_KEYCLOAK_URL=https://keycloak.localtest.me:8443 \
  SHOP_GRAFANA_URL=https://grafana.localtest.me:8443 SHOP_WEBDATASTUDIO_URL=https://data.localtest.me:8443
coworkee deploy compose
cd deploy/compose
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d caddy
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt caddy-root.crt
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d
```

Die Dienste vertrauen Caddys eigener Zertifizierungsstelle über `caddy-root.crt`; der Browser warnt davor, das ist hier so gewollt. Manche Router (zum Beispiel eine Fritz!Box) lösen Namen nicht auf, die auf `127.0.0.1` zeigen; dann trag die Namen mit `127.0.0.1` in deine hosts-Datei ein oder starte den Browser mit `--host-resolver-rules="MAP *.localtest.me 127.0.0.1"`. Keycloak, Grafana und WebDataStudio reden über das Container-Netz miteinander, Caddy vertrauen müssen also nur Auth-Server, API und Web-App.

### Dein eigener Identity Provider

Willst du dich über ein Keycloak anmelden, das woanders läuft (oder einen anderen OpenID-Connect-Provider), deploye ohne das des App-Hosts (`--without keycloak`) und gib Auth-Server und API den Provider in `docker-compose.yaml` oder einer Override-Datei mit:

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

Der Client im Realm leitet auf `https://auth.example.com/signin-keycloak` zurück. Erreicht der Auth-Server den Provider unter einer anderen Adresse als der Browser, nennt `...__BrowserUrl` die des Browsers (für sein eigenes Keycloak setzt der App-Host sie).

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

Dateien brauchen einen Speicher, den alle Replikate teilen: Erzeuge die App mit `--storage azureBlob`. Deployt wird mit `azd init` und `azd up`; dort setzt du die Parameter (öffentliche Adressen der Dienste und Tools, den Administrator, den Mailserver, die beiden Zertifikate, das Setup-Token; wie man sie erzeugt, zeigt `coworkee deploy compose`). Die Images der Tools, die Dateien der App mitbringen, entstehen aus Dockerfiles unter `src/<App>.AppHost/observability/.generated/publish`, die azd baut. Der Migrations-Job läuft, wenn du ihn startest: `az containerapp job start -n shop-migrations -g <ressourcengruppe>`, nach jedem Deployment. **Nicht geprüft:** Das Bicep kompiliert, ein echtes Deployment gab es noch nicht.

## Kubernetes

```bash
coworkee deploy k8s
helm lint deploy/k8s
helm template deploy/k8s | kubeconform -strict -summary
```

schreibt ein Helm-Chart (`Chart.yaml`, `values.yaml`, `templates/`) mit einem Deployment und einem Service pro Dienst. Aspires Kubernetes-Publisher ist eine Preview; `coworkee deploy` repariert zwei Dinge in seiner Ausgabe: Einstellungsnamen mit Punkt werden mit `index` gelesen, und die Migrationen werden ein Job, den Helm nach jeder Installation und jedem Upgrade startet.

Fülle `values.yaml`: die öffentlichen Adressen (`shop_web_url`, `shop_auth_url`, `shop_keycloak_url`, ..., pro Dienst wiederholt), den Administrator, den Mailserver, die Secrets (Connection-Strings, Passwörter, Zertifikate, Setup-Token) und die Images, die du in deine Registry schiebst (`dotnet publish -t:PublishContainer -p:ContainerRegistry=...`, `docker build` für die Tools). Ingress und TLS liefert dein Cluster: Richte die öffentlichen Adressen auf die Web-App, den Auth-Server und die Tools.

**Nicht geprüft:** Das Chart besteht Lint und Validierung, auf einem Cluster lief es noch nicht. Die Volumes des Charts sind `emptyDir`, die Daten von Postgres, Redis und den Tools und die Dateien gehen also mit dem Pod verloren: Nimm verwaltete Datenbanken (`UseDatabase`, `UseRedis`), Azure-Blob-Speicher, oder stell die Volumes auf Persistent Volume Claims um, bevor du dich darauf verlässt.

## Eine eigene Umgebung

`Coworkee:Deploy:Target` (`compose`, `azure`, `k8s`) wählt die Umgebung, die `coworkee deploy` übergibt. Fügt der App-Host vor `AddCoworkeeApp` selbst eine Compute-Umgebung hinzu, bleibt es bei ihr, zum Beispiel `builder.AddDockerComposeEnvironment("compose").WithDashboard(false)`. `aspire publish` geht auch, sobald die Aspire-CLI zur Aspire-Version des App-Hosts passt; `coworkee deploy` startet den App-Host direkt und braucht sie nicht.
