# Sign in to COWORKEE_APP_TITLE

Start the app with `coworkee run` (or `dotnet run --project src/MyApp.AppHost`), open *myapp-web* from the Aspire dashboard and sign in with one of these users:

| User | Password | Roles |
|---|---|---|
| `admin@myapp.local` | `Cw1!SEED_PASSWORD` | Administrator |

<!--#if (keycloak) -->
The *Keycloak* button on the sign-in page takes the same users with the same passwords (realm `myapp`); the app host brings the realm up to date on every start.

<!--#endif -->
<!--#if (registration) -->
Visitors can also create an account on the sign-in page (setting `Account.AllowRegistration`).

<!--#endif -->
<!--#if (grafana || webDataStudio) -->
## Development tools

The administrator signs in with the same email and password to the tools the app host starts next to the app (open them from the Aspire dashboard):

<!--#if (grafana) -->
- *grafana*: dashboards over the metrics, logs and traces of every service and the app database (folder *COWORKEE_APP_TITLE*).
<!--#endif -->
<!--#if (webDataStudio) -->
- *webdatastudio*: the app database in the browser.
<!--#endif -->
<!--#if (keycloak) -->

Both also offer the *Keycloak* button; users with the realm role *Administrator* are administrators there, everybody else may look.
<!--#endif -->

<!--#endif -->
The users and passwords come from `src/MyApp.Migrations/DemoUsers.cs` and are seeded on the first start of an empty database. They are for development only: change them before you deploy anywhere.
