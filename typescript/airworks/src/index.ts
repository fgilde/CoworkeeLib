import { assistantApp } from './apps/assistant.ts';
import { chatApp } from './apps/chat.ts';
import { dataApp } from './apps/data.ts';
import { filePreviewApp } from './apps/file-preview.ts';
import { filesApp } from './apps/files.ts';
import { apiDocsApp, notificationsApp } from './apps/notifications.ts';
import { profileApp } from './apps/profile.ts';
import { reportApp, reportsApp } from './apps/reports.ts';
import { settingsApp } from './apps/settings.ts';
import type { AppFactory } from './coworkee.ts';

export { useCoworkee, type AirWorksApi, type AppFactory, type Coworkee, type CoworkeeApp, type CoworkeeOptions } from './coworkee.ts';
export { bffAuthProvider, sessionOf } from './auth.ts';
export { loadMudEx, defaultMudExUrl } from './mudex.ts';
export { bytes, display, esc, toast } from './dom.ts';
export { confirmDialog, fieldHtml, formDialog, infoDialog, promptDialog, readFields, type Field, type FieldType, type FormOptions, type Values } from './form.ts';
export { choices, crudApp, renderCrud, type CrudAction, type CrudColumn, type CrudOptions, type CrudQuery, type CrudToolbarAction } from './crud.ts';
export { columnsOf, nextOrder, renderEntityTable, type Column, type EntityTableOptions } from './table.ts';
export { changes, groupsFor, isEnabled, valuesOf } from './settings-model.ts';
export { dataApp, entityApp, type EntityAppOptions } from './apps/data.ts';
export { filePreviewApp, filePreviewAppId } from './apps/file-preview.ts';
export { fileContentUrl, filesApp, uploadFile } from './apps/files.ts';
export { apiDocsApp, notificationsApp } from './apps/notifications.ts';
export { profileApp } from './apps/profile.ts';
export { renderSettings, settingsApp, settingsPageApp, type SettingsPageOptions, type SettingsTest } from './apps/settings.ts';
export { chatApp } from './apps/chat.ts';
export { formatNumber, reportApp, reportAppId, reportsApp } from './apps/reports.ts';
export { aiSettingsApp, assistantApp } from './apps/assistant.ts';
export * from './apps/admin/index.ts';

/** The apps of the library for every user: profile, settings, files with preview, data, chat, reports, assistant, notifications and the API documentation. */
export const defaultApps: AppFactory[] = [profileApp, settingsApp, filesApp, filePreviewApp, dataApp, chatApp, reportsApp, reportApp, assistantApp, notificationsApp, apiDocsApp];
