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
The users and passwords come from `src/MyApp.Migrations/DemoUsers.cs` and are seeded on the first start of an empty database. They are for development only: change them before you deploy anywhere.
