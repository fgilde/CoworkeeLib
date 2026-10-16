# Deployment

The app host that runs the app on your machine also describes its deployment. `coworkee deploy` publishes it for a target: Docker Compose for a single Linux host, Azure Container Apps as Bicep, Kubernetes as a Helm chart. The tools your app host uses (Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI) go along, each with its own volume and, where it has a web interface, a public address.

| Target | Command | Status |
|---|---|---|
| Docker Compose | `coworkee deploy compose` | verified: a generated app with Keycloak, Grafana, WebDataStudio and LibreTranslate runs from the published files behind an HTTPS proxy; the seeded administrator signs in with the password and through Keycloak, Grafana shows the app's data, WebDataStudio opens the database, machine translation works. Ollama and AspireUI are published and checked in the files, not started |
| Azure Container Apps | `coworkee deploy azure` | Bicep generated with the tools, every file compiles with `az bicep build`; not deployed to Azure |
| Kubernetes | `coworkee deploy k8s` | Helm chart generated with the tools, `helm lint` and `kubeconform -strict` pass; not installed on a cluster |

## What changes in a deployment

Publishing runs the app host in publish mode with the environment `Production`. The app then differs from a local run:

| Local run | Deployment |
|---|---|
| Each service on its https endpoint | the public addresses behind your reverse proxy, as parameters `<app>-auth-url` and `<app>-web-url` (and `<app>-airworks-url`) |
| Development certificates for the tokens | two certificates from the parameters `<app>-signing-certificate` and `<app>-encryption-certificate` (PKCS#12 as Base64), the issuer is the public auth address |
| Demo users from `DemoUsers.cs` | one administrator from the parameters `<app>-admin-email` and `<app>-admin-password`, created by the migrations in an empty database (see [The first administrator](#the-first-administrator)) |
| Mailpit | your mail server from the parameters `<app>-smtp-host`, `-smtp-port`, `-smtp-username`, `-smtp-password` and `-smtp-from`; Mailpit only when you ask for it (see [Mail](#mail)) |
| Keycloak, Grafana, WebDataStudio, LibreTranslate, Ollama, AspireUI | deployed as well, unless you leave one out (see [Tools](#tools)) |
| Files in `.data/blobs` | a volume `<app>-files`, shared by the services (Docker Compose) |
| Volumes named after the app host's folder | `<app>-postgres-data`, `<app>-redis-data`, ...: publishing again from another checkout finds the same data |

What stays the same: the migrations run once as their own service before the others start (and retry for a minute while the database server comes up; on Azure and Kubernetes they become a job), every service has `/health` and `/alive`, the Data Protection keys of the services with a database live in it (one key ring for api, auth server and workers), the web app keeps its sign-in sessions and their key ring in Redis, and Aspire sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED` so the services read scheme and host from the proxy.

## Tools

Every tool the app host uses goes into the deployment. You leave one out for a single deployment or for good:

```bash
coworkee deploy compose --without grafana --without ollama
```

```csharp
var app = builder.AddCoworkeeApp("shop", options => options.UseGrafana().DeployTool(CoworkeeTools.Grafana, false));
```

`--without` and `--with` set `Coworkee:Deploy:Tools:<tool>`, which wins over `DeployTool`. The tools are `keycloak`, `mailpit`, `libretranslate`, `grafana`, `webdatastudio`, `ollama` and `aspireui`. Your own app host code asks `app.Deploys(CoworkeeTools.Ollama)`, the way the template adds Ollama and AspireUI.

| Tool | In the deployment |
|---|---|
| Keycloak | the realm of the app with the clients of the auth server and the tools, and the first administrator as user with the role `Administrator`; the migrations bring it in line on every deployment. Public address `<app>-keycloak-url`, data in `<app>-keycloak-data` (Keycloak's own file database), its console as `admin` with `KEYCLOAK_PASSWORD`. The demo users and the login theme stay local |
| Grafana | with Prometheus, Loki, Tempo and the collector; the app's dashboards and the configuration are built into the images (`<app>-grafana`, ...). The administrator signs in with the password or through Keycloak. Public address `<app>-grafana-url`, data in `<app>-grafana-data`, `<app>-prometheus-data` and `<app>-loki-data`; Tempo keeps its traces in the container |
| WebDataStudio | connected to the app database; the administrator signs in with the password or through Keycloak. Public address `<app>-webdatastudio-url`, data in `<app>-webdatastudio-data` |
| LibreTranslate | the translation service of the app, inside the network only; the models live in `<app>-libretranslate-models`, the first start downloads them |
| Ollama | the model server of the app, inside the network only, models in `<app>-ollama-data`. It needs a host with enough memory |
| AspireUI | with the administrator's sign-in (and Keycloak), public address `<app>-aspireui-url`, data in `<app>-aspireui-data`. It drives the host's Docker through `/var/run/docker.sock`, so it suits Docker Compose |
| Mailpit | only with `--with mailpit`, for a test deployment: every mail lands there, its web interface (`<app>-mail-url`) asks for the administrator's email and password |

Each tool with a web interface sits behind your reverse proxy like the services; its public address also shows on the admin page **Services** (`Coworkee:Services`).

## The first administrator

When the database is empty, the migrations create the administrator of `<app>-admin-email` and `<app>-admin-password` with the organisation and the settings of the template, and the realm of a deployed Keycloak gets the same user. `coworkee deploy compose` asks for the email (or takes `--admin-email`, an environment variable `SHOP_ADMIN_EMAIL`, or `admin@<app>.local`), generates the password and prints both at the end of its output, nowhere else. The same email and password sign you in to the app, Keycloak, Grafana, WebDataStudio, AspireUI and Mailpit.

Leave both values empty and nothing is created: the app shows its setup page, where `<app>-setup-token` lets you set up the organisation and the administrator yourself. A database that is set up stays as it is; a changed password in `.env` reaches Keycloak and the tools, not the app's own account.

## Mail

A deployment sends through your mail server. `coworkee deploy compose` asks for host, port, user, password and sender (or reads `SHOP_SMTP_HOST`, ... from the environment) and writes them into `.env`; what you leave empty you fill in later. They become the defaults of the mail settings, which administrators change on the mail settings page. `UseMail(endpoint)` in the app host takes another SMTP resource instead, `--with mailpit` takes Mailpit.

## Docker Compose

```bash
coworkee deploy compose
```

writes `deploy/compose` in the solution (`-o` for another folder):

- `docker-compose.yaml`: Postgres or SQL Server, Redis, the migrations, auth server, api, web app, the tools, and the Aspire dashboard for logs and traces.
- `.env`: every parameter. The tool fills what it can: random passwords and the setup token, the administrator, the two token certificates, the Web Push key pair (with notifications and `pwa`), the images and the ports. The mail server it asks for at the console. An environment variable of the same name (`SHOP_WEB_URL=...`) is taken over. It keeps the values of an earlier run, so a second `coworkee deploy` does not change passwords or certificates.
- the images `<app>-migrations`, `<app>-auth`, `<app>-api`, `<app>-web` (`dotnet publish -t:PublishContainer`) and those of the tools that carry files of the app (`<app>-grafana`, `<app>-prometheus`, ..., `docker build`) in your local Docker; `--no-build` skips that.

At the end it lists every service and tool with its loopback port and public address, and how to sign in.

You set the public addresses in `.env`:

```bash
SHOP_AUTH_URL=https://auth.example.com
SHOP_WEB_URL=https://shop.example.com
SHOP_KEYCLOAK_URL=https://login.example.com
SHOP_GRAFANA_URL=https://grafana.example.com
SHOP_WEBDATASTUDIO_URL=https://data.example.com
```

The services listen on the host's loopback in the order they were added: auth server `127.0.0.1:8080`, api `8081`, web app `8082` (AirWorks `8083`). The tools have fixed ports: Keycloak `8090`, Grafana `8091`, WebDataStudio `8092`, AspireUI `8093`, Mailpit `8094`. A reverse proxy terminates HTTPS in front of them, for example Caddy, which gets the certificates from Let's Encrypt:

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

The services reach the auth server under its public address too (the web app signs in there, the api validates tokens against it), so the host must resolve that name and trust its certificate, which a public certificate does.

Then on the host (copy the folder and load the images there, or build them there with `coworkee deploy compose`):

```bash
docker compose up -d
```

Open the web address and sign in as the administrator `coworkee deploy` printed. With the administrator left empty the app shows the setup page instead: enter `SHOP_SETUP_TOKEN` from `.env`, the name of your organisation and the first administrator.

### Trying it on your machine

Docker Desktop is enough. Names under `localtest.me` point to `127.0.0.1`; a Caddy container with its own certificate authority stands in for the proxy and is known to the other containers under the same names. Next to `docker-compose.yaml`:

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
# the addresses as environment variables: coworkee deploy takes them into .env
export SHOP_AUTH_URL=https://auth.localtest.me:8443 SHOP_WEB_URL=https://app.localtest.me:8443 SHOP_KEYCLOAK_URL=https://keycloak.localtest.me:8443 \
  SHOP_GRAFANA_URL=https://grafana.localtest.me:8443 SHOP_WEBDATASTUDIO_URL=https://data.localtest.me:8443
coworkee deploy compose
cd deploy/compose
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d caddy
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt caddy-root.crt
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d
```

The services trust Caddy's own authority through `caddy-root.crt`; the browser warns about it, which is expected here. Some routers (a Fritz!Box, for example) refuse to resolve names that point to `127.0.0.1`; then add the names with `127.0.0.1` to your hosts file, or start the browser with `--host-resolver-rules="MAP *.localtest.me 127.0.0.1"`. Keycloak, Grafana and WebDataStudio talk to each other over the container network, so only the auth server, api and web app need to trust Caddy.

### Your own identity provider

To sign in through a Keycloak you run elsewhere (or any OpenID Connect provider), deploy without the app host's one (`--without keycloak`) and give the auth server and the api the provider in `docker-compose.yaml` or an override file:

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

The client in the realm redirects to `https://auth.example.com/signin-keycloak`. When the auth server reaches the provider at another address than the browser does, `...__BrowserUrl` names the browser's one (the app host sets it for its own Keycloak).

## Azure Container Apps

```bash
coworkee deploy azure
az bicep build --file deploy/azure/main.bicep
```

writes `main.bicep` with a Container Apps environment and a registry, and a Bicep file per service; the Postgres and Redis containers become container apps with volumes, the migrations a Container Apps job. For production take the managed services and hand them to the app (add `Aspire.Hosting.Azure.PostgreSQL` to the app host):

```csharp
var database = builder.AddAzurePostgresFlexibleServer("postgres").RunAsContainer().AddDatabase("myapp");
var app = builder.AddCoworkeeApp("myapp", options => options.UseDatabase(database));
```

Files need a storage that every replica shares: generate the app with `--storage azureBlob`. Deploy with `azd init` and `azd up`, and set the parameters there (public addresses of the services and tools, the administrator, the mail server, the two certificates, the setup token; `coworkee deploy compose` shows how to generate them). The images of the tools that carry files of the app come from Dockerfiles below `src/<App>.AppHost/observability/.generated/publish`, which azd builds. The migrations job runs when you start it: `az containerapp job start -n shop-migrations -g <resource group>`, after every deployment. **Not verified:** the Bicep compiles, a real deployment has not been made.

## Kubernetes

```bash
coworkee deploy k8s
helm lint deploy/k8s
helm template deploy/k8s | kubeconform -strict -summary
```

writes a Helm chart (`Chart.yaml`, `values.yaml`, `templates/`) with a deployment and a service per service. Aspire's Kubernetes publisher is a preview; `coworkee deploy` repairs two things in its output: setting names with a dot are read with `index`, and the migrations become a Job that Helm runs after each install and upgrade.

Fill `values.yaml`: the public addresses (`shop_web_url`, `shop_auth_url`, `shop_keycloak_url`, ..., repeated per service), the administrator, the mail server, the secrets (connection strings, passwords, certificates, setup token) and the images, which you push to your registry (`dotnet publish -t:PublishContainer -p:ContainerRegistry=...`, `docker build` for the tools). Ingress and TLS are your cluster's: point the public addresses at the web app, the auth server and the tools.

**Not verified:** the chart lints and validates, it has not run on a cluster. The volumes of the chart are `emptyDir`, so the data of Postgres, Redis and the tools and the files are lost with the pod: use managed databases (`UseDatabase`, `UseRedis`), Azure Blob storage, or change the volumes to persistent volume claims before you rely on it.

## An environment of your own

`Coworkee:Deploy:Target` (`compose`, `azure`, `k8s`) picks the environment `coworkee deploy` passes. An app host that adds a compute environment itself before `AddCoworkeeApp` keeps it, for example `builder.AddDockerComposeEnvironment("compose").WithDashboard(false)`. `aspire publish` works as well once the Aspire CLI matches the app host's Aspire version; `coworkee deploy` runs the app host directly and does not need it.
