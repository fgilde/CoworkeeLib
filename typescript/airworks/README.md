# @coworkee/airworks

The [AirWorks](https://fgilde.github.io/AirWorks/) web desktop for apps built on [Coworkee](https://fgilde.github.io/CoworkeeLib/): sign-in through the app's BFF, theme, logo and texts of the app, and apps for profile, settings, files (with the MudEx file preview), data tables and notifications.

```ts
import { defaultApps, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop' });
await coworkee.register(...defaultApps);
document.body.append(document.createElement('air-desktop'));
```

`coworkee new --airworks` creates an app with this client in the Aspire app host. Documentation: [AirWorks client](https://fgilde.github.io/CoworkeeLib/web/airworks/)

License: MIT