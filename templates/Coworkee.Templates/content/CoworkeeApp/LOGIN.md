# Sign in to COWORKEE_APP_TITLE

Start the app with `coworkee run` (or `dotnet run --project src/MyApp.AppHost`), open *myapp-web* from the Aspire dashboard and sign in with one of these users:

| User | Password | Roles |
|---|---|---|
| `admin@myapp.local` | `Cw1!SEED_PASSWORD` | Administrator |

<!--#if (keycloak) -->
The *Keycloak* button on the sign-in page takes the same users with the same passwords (realm `myapp`); the app host brings the realm up to date on every start.

<!--#endif -->
<!--#if (airworks) -->
The AirWorks web desktop *myapp-airworks* signs in the same way (it is a second client of the same sign-in) and works on the same data.

<!--#endif -->
<!--#if (withElsaStudio) -->
The workflow designer *myapp-elsastudio* (Elsa Studio, *Workflow designer* in the app's navigation) signs in the same way; it needs the permission `Workflows.Elsa.Manage`, which administrators have.

<!--#endif -->
<!--#if (registration) -->
Visitors can also create an account on the sign-in page (setting `Account.AllowRegistration`).

<!--#endif -->
<!--#if (grafana || webDataStudio || aspireUI || (elasticsearch && kibana) || withOpenWebUI) -->
## Development tools

The administrator signs in with the same email and password to the tools the app host starts next to the app (open them from the Aspire dashboard):

<!--#if (grafana) -->
- *grafana*: dashboards over the metrics, logs and traces of every service and the app database (folder *COWORKEE_APP_TITLE*).
<!--#endif -->
<!--#if (webDataStudio) -->
- *webdatastudio*: the app database in the browser.
<!--#endif -->
<!--#if (elasticsearch && kibana) -->
- *kibana*: the search indexes in Kibana. It signs in at the auth server of the app: only administrators of the system organisation get in (with Keycloak when it is on).
<!--#endif -->
<!--#if (withOpenWebUI) -->
- *openwebui*: a chat over the local models of Ollama.
<!--#endif -->
<!--#if (aspireUI) -->
- *aspireui*: host and try more apps and stacks next to COWORKEE_APP_TITLE; what you host there also shows up on the admin page *Services* of the app. Its data lives in the volume `myapp-aspireui-data`.
<!--#endif -->

They also offer the sign-in at the auth server of the app: only administrators of the system organisation get in, as administrators of the tool.

<!--#endif -->
The users and passwords come from `src/MyApp.Migrations/DemoUsers.cs` and are seeded on the first start of an empty database, in the Development environment only. A deployment (`coworkee deploy`) starts without users and is set up in the browser with its setup token.
