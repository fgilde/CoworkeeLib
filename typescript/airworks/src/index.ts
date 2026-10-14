import { dataApp } from './apps/data.ts';
import { filePreviewApp } from './apps/file-preview.ts';
import { filesApp } from './apps/files.ts';
import { apiDocsApp, notificationsApp } from './apps/notifications.ts';
import { profileApp } from './apps/profile.ts';
import { settingsApp } from './apps/settings.ts';
import type { AppFactory } from './coworkee.ts';

export { useCoworkee, type AirWorksApi, type AppFactory, type Coworkee, type CoworkeeApp, type CoworkeeOptions } from './coworkee.ts';
export { bffAuthProvider, sessionOf } from './auth.ts';
export { loadMudEx, defaultMudExUrl } from './mudex.ts';
export { bytes, display, esc, toast } from './dom.ts';
export { columnsOf, nextOrder, renderEntityTable, type Column, type EntityTableOptions } from './table.ts';
export { changes, groupsFor, isEnabled, valuesOf } from './settings-model.ts';
export { dataApp, entityApp, type EntityAppOptions } from './apps/data.ts';
export { filePreviewApp, filePreviewAppId } from './apps/file-preview.ts';
export { fileContentUrl, filesApp, uploadFile } from './apps/files.ts';
export { apiDocsApp, notificationsApp } from './apps/notifications.ts';
export { profileApp } from './apps/profile.ts';
export { settingsApp } from './apps/settings.ts';

/** The apps of the library: profile, settings, files with preview, data, notifications and the API documentation. */
export const defaultApps: AppFactory[] = [profileApp, settingsApp, filesApp, filePreviewApp, dataApp, notificationsApp, apiDocsApp];
