# Deployment

The app host that runs the app on your machine also describes its deployment. `coworkee deploy` publishes it for a target: Docker Compose for a single Linux host, Azure Container Apps as Bicep, Kubernetes as a Helm chart.

| Target | Command | Status |
|---|---|---|
| Docker Compose | `coworkee deploy compose` | verified: a generated app runs from the published files behind an HTTPS proxy, sign-in through the web app works |
| Azure Container Apps | `coworkee deploy azure` | Bicep generated, every file compiles with `az bicep build`; not deployed to Azure |
| Kubernetes | `coworkee deploy k8s` | Helm chart generated, `helm lint` and `kubeconform -strict` pass; not installed on a cluster |

## What changes in a deployment

Publishing runs the app host in publish mode with the environment `Production`. The app then differs from a local run:

| Local run | Deployment |
|---|---|
| Each service on its https endpoint | the public addresses behind your reverse proxy, as parameters `<app>-auth-url` and `<app>-web-url` (and `<app>-airworks-url`) |
| Development certificates for the tokens | two certificates from the parameters `<app>-signing-certificate` and `<app>-encryption-certificate` (PKCS#12 as Base64), the issuer is the public auth address |
| Demo users from `DemoUsers.cs` | no users: you set the system up in the browser with the setup token (parameter `<app>-setup-token`) |
| Mailpit, LibreTranslate, Ollama, Keycloak, Grafana, WebDataStudio, AspireUI | left out; set the mail server and the translation endpoint on the settings pages, bring your own identity provider |
| Files in `.data/blobs` | a volume `<app>-files`, shared by the services (Docker Compose) |
| Volumes named after the app host's folder | `<app>-postgres-data`, `<app>-redis-data`, ...: publishing again from another checkout finds the same data |

What stays the same: the migrations run once as their own service before the others start (and retry for a minute while the database server comes up; on Azure and Kubernetes they become a job), every service has `/health` and `/alive`, the Data Protection keys of the services with a database live in it (one key ring for api, auth server and workers), the web app keeps its sign-in sessions and their key ring in Redis, and Aspire sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED` so the services read scheme and host from the proxy.

## Docker Compose

```bash
coworkee deploy compose
```

writes `deploy/compose` in the solution (`-o` for another folder):

- `docker-compose.yaml`: Postgres or SQL Server, Redis, the migrations, auth server, api, web app, and the Aspire dashboard for logs and traces.
- `.env`: every parameter. The tool fills what it can: random passwords and the setup token, the two token certificates, the Web Push key pair (with notifications and `pwa`), the images and the ports. It keeps the values of an earlier run, so a second `coworkee deploy` does not change passwords or certificates.
- the images `<app>-migrations`, `<app>-auth`, `<app>-api`, `<app>-web` in your local Docker (`dotnet publish -t:PublishContainer`; `--no-build` skips that).

You set the public addresses in `.env`:

```bash
SHOP_AUTH_URL=https://auth.example.com
SHOP_WEB_URL=https://shop.example.com
```

The services listen on the host's loopback in the order they were added: auth server `127.0.0.1:8080`, api `8081`, web app `8082` (AirWorks `8083`). A reverse proxy terminates HTTPS in front of them, for example Caddy, which gets the certificates from Let's Encrypt:

```caddyfile
auth.example.com {
	reverse_proxy 127.0.0.1:8080
}

shop.example.com {
	reverse_proxy 127.0.0.1:8082
}
```

The services reach the auth server under its public address too (the web app signs in there, the api validates tokens against it), so the host must resolve that name and trust its certificate, which a public certificate does.

Then on the host (copy the folder and load the images there, or build them there with `coworkee deploy compose`):

```bash
docker compose up -d
```

Open the web address: the app shows the setup page. Enter `SHOP_SETUP_TOKEN` from `.env`, the name of your organisation and the first administrator.

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
# in .env: SHOP_AUTH_URL=https://auth.localtest.me:8443 and SHOP_WEB_URL=https://app.localtest.me:8443
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d caddy
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt caddy-root.crt
docker compose -f docker-compose.yaml -f compose.proxy.yaml up -d
```

The services trust Caddy's own authority through `caddy-root.crt`; the browser warns about it, which is expected here. Some routers (a Fritz!Box, for example) refuse to resolve names that point to `127.0.0.1`; then add `127.0.0.1 app.localtest.me auth.localtest.me` to your hosts file.

### Your own identity provider

Keycloak of the app host is a development realm with the demo users and is not published. To sign in through your Keycloak (or any OpenID Connect provider), give the auth server and the api the provider in `docker-compose.yaml` or an override file:

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

The client in the realm redirects to `https://auth.example.com/signin-keycloak`. This path is not verified in a deployment yet.

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

Files need a storage that every replica shares: generate the app with `--storage azureBlob`. Deploy with `azd init` and `azd up`, and set the parameters there (public addresses, the two certificates, the setup token; `coworkee deploy compose` shows how to generate them). The migrations job runs when you start it: `az containerapp job start -n shop-migrations -g <resource group>`, after every deployment. **Not verified:** the Bicep compiles, a real deployment has not been made.

## Kubernetes

```bash
coworkee deploy k8s
helm lint deploy/k8s
helm template deploy/k8s | kubeconform -strict -summary
```

writes a Helm chart (`Chart.yaml`, `values.yaml`, `templates/`) with a deployment and a service per service. Aspire's Kubernetes publisher is a preview; `coworkee deploy` repairs two things in its output: setting names with a dot are read with `index`, and the migrations become a Job that Helm runs after each install and upgrade.

Fill `values.yaml`: the public addresses (`shop_web_url`, `shop_auth_url`, repeated per service), the secrets (connection strings, passwords, certificates, setup token) and the images, which you push to your registry (`dotnet publish -t:PublishContainer -p:ContainerRegistry=...`). Ingress and TLS are your cluster's: point the public addresses at the web app and the auth server.

**Not verified:** the chart lints and validates, it has not run on a cluster. The volumes of the chart are `emptyDir`, so the Postgres and Redis data and the files are lost with the pod: use managed databases (`UseDatabase`, `UseRedis`), Azure Blob storage, or change the volumes to persistent volume claims before you rely on it.

## An environment of your own

`Coworkee:Deploy:Target` (`compose`, `azure`, `k8s`) picks the environment `coworkee deploy` passes. An app host that adds a compute environment itself before `AddCoworkeeApp` keeps it, for example `builder.AddDockerComposeEnvironment("compose").WithDashboard(false)`. `aspire publish` works as well once the Aspire CLI matches the app host's Aspire version; `coworkee deploy` runs the app host directly and does not need it.
