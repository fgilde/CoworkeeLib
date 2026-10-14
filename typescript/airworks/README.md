# @coworkee/airworks

The [AirWorks](https://fgilde.github.io/AirWorks/) web desktop for apps built on [Coworkee](https://fgilde.github.io/CoworkeeLib/): sign-in through the app's BFF, theme, logo and texts of the app, realtime, and apps for profile, settings, files (with the MudEx file preview), data tables, chat, reports, the AI assistant, notifications and the whole administration (users, roles, applications, themes, languages, translations, mail templates, campaigns, mail log, backups, audit log, ...).

```bash
npm install @coworkee/airworks @coworkee/client github:fgilde/AirWorks
```

```ts
import { adminApps, defaultApps, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop' });
await coworkee.register(...defaultApps, ...adminApps);
document.body.append(document.createElement('air-desktop'));
```

Apps are hidden when the user lacks their permission, like the menu of the Blazor app. `crudApp(...)` builds list apps with create, edit, delete and row actions; `coworkee.realtime.subscribe(topic, handler)` follows the realtime hub.

`coworkee new --airworks` creates an app with this client in the Aspire app host; it references the version of the Coworkee release on npm. Documentation: [AirWorks client](https://fgilde.github.io/CoworkeeLib/web/airworks/)

License: MIT
