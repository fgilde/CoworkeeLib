import { defaultApps, entityApp, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';
import './styles.css';
import logo from '../../../MyApp.Web.Client/wwwroot/coworkee-icon.svg?url';

// sign-in, theme, logo and texts of the app; AirWorks options (wallpapers, initialShortcuts, ...) go to airWorks: { ... }
const coworkee = await useCoworkee(AirWorks, { title: 'COWORKEE_APP_TITLE', logo });

// the library's apps and your own: leave some out, replace them or add more (https://fgilde.github.io/AirWorks/)
await coworkee.register(
  ...defaultApps,
//#if (samples)
  entityApp({ entitySet: 'Products', icon: 'ratio-product.png', requiredPermissions: ['Catalog.Products.View'] }),
  entityApp({ entitySet: 'Brands', icon: 'ratio-shoppingCart.png', requiredPermissions: ['Catalog.Brands.View'] }),
//#endif
);

document.body.append(document.createElement('air-desktop'));
