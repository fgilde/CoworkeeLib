import { apiRequest, unwrap, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { bytes, display, errorText, esc } from '../dom.ts';
import { filePreviewAppId } from './file-preview.ts';

type Crumb = { id?: string; name: string };

export const fileContentUrl = (id: string, download = false) => `/api/v1/files/${encodeURIComponent(id)}/content${download ? '?download=true' : ''}`;

/** Uploads one file into a folder (the API takes the raw body). */
export const uploadFile = (file: File, folderId?: string) =>
  apiRequest<Schemas['StoredFileDto']>(`/api/v1/files?${new URLSearchParams({ name: file.name, ...(folderId ? { folderId } : {}) })}`,
    { method: 'POST', body: file, headers: { 'Content-Type': file.type || 'application/octet-stream' } });

/** The folders and files of the organisation: browse, upload (also by dropping files), rename, delete, download and preview. */
export const filesApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.files',
  title: coworkee.t('Files'),
  icon: 'icon-folder.png',
  accent: '#b26a00',
  width: 860,
  height: 580,
  requiredPermissions: ['Files.View'],
  render: ({ host, intent, setState }: AppContext) => {
    const { t, api, culture } = coworkee;
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-files">
      <div class="cw-toolbar">
        <button data-action="up" title="${esc(t('Back'))}">↑</button>
        <nav class="cw-crumbs"></nav>
        <span class="cw-spacer"></span>
        <button data-action="folder">${esc(t('New folder'))}</button>
        <label class="cw-button cw-primary" data-upload>${esc(t('Upload'))}<input type="file" multiple hidden/></label>
        <button data-action="refresh" title="${esc(t('Refresh'))}">⟳</button>
      </div>
      <div class="cw-drop"><table class="cw-table"><thead><tr><th>${esc(t('Name'))}</th><th>${esc(t('Size'))}</th><th>${esc(t('Created'))}</th><th></th></tr></thead><tbody></tbody></table></div>
    </div>`;
    const path: Crumb[] = [{ name: t('Files') }];
    if (intent?.folder) path.push({ id: intent.folder, name: intent.folderName || '…' });
    let content: Schemas['FolderContentDto'] | undefined;
    const folderId = () => path.at(-1)!.id;
    const body = host.querySelector('tbody')!;

    const load = async () => {
      setState(folderId() ? { folder: folderId()!, folderName: path.at(-1)!.name } : {});
      host.querySelector('.cw-crumbs')!.innerHTML = path.map((crumb, index) => `<button data-crumb="${index}">${esc(crumb.name)}</button>`).join('<span>/</span>');
      try {
        content = unwrap(await api.GET('/api/v1/files/folders/content', { params: { query: { folderId: folderId() } } }));
      } catch (error) {
        body.innerHTML = `<tr><td colspan="4" class="cw-error">${esc(errorText(error))}</td></tr>`;
        return;
      }
      host.querySelector<HTMLElement>('[data-upload]')!.hidden = !content.canUpload;
      host.querySelector<HTMLElement>('[data-action="folder"]')!.hidden = !content.canUpload;
      const actions = (kind: string, id: string) => content!.canManage || kind === 'file'
        ? `<td class="cw-row-actions">${kind === 'file' ? `<a href="${esc(fileContentUrl(id, true))}" title="${esc(t('Download'))}">⤓</a>` : ''}
           ${content!.canManage ? `<button data-rename="${kind}:${esc(id)}" title="${esc(t('Rename'))}">✎</button><button data-delete="${kind}:${esc(id)}" title="${esc(t('Delete'))}">🗑</button>` : ''}</td>`
        : '<td></td>';
      const rows = [
        ...content.folders.map((folder) => `<tr data-folder="${esc(folder.id)}" data-name="${esc(folder.name)}"><td><span class="cw-icon">📁</span>${esc(folder.name)}</td><td></td><td>${esc(display(folder.createdAt, culture))}</td>${actions('folder', folder.id)}</tr>`),
        ...content.files.map((file) => `<tr data-file="${esc(file.id)}" data-name="${esc(file.name)}" data-type="${esc(file.contentType)}"><td><span class="cw-icon">📄</span>${esc(file.name)}</td><td>${esc(bytes(file.size, culture))}</td><td>${esc(display(file.createdAt, culture))}</td>${actions('file', file.id)}</tr>`),
      ];
      body.innerHTML = rows.join('') || `<tr><td colspan="4" class="cw-muted">${esc(t('(empty)'))}</td></tr>`;
    };

    const run = async (work: () => Promise<unknown>) => {
      try { await work(); } catch (error) { coworkee.notify(errorText(error), 'error'); }
      await load();
    };

    const upload = (files: FileList | File[]) => run(async () => { for (const file of [...files]) await uploadFile(file, folderId()); });

    // shortcut: names and confirmations through the browser's prompt/confirm, an AirWorks dialog once one is wanted
    host.addEventListener('click', (event) => {
      const target = event.target as Element;
      const action = target.closest<HTMLElement>('[data-action]')?.dataset.action;
      const crumb = target.closest<HTMLElement>('[data-crumb]')?.dataset.crumb;
      const rename = target.closest<HTMLElement>('[data-rename]')?.dataset.rename;
      const remove = target.closest<HTMLElement>('[data-delete]')?.dataset.delete;
      if (action === 'up' && path.length > 1) { path.pop(); void load(); }
      else if (action === 'refresh') void load();
      else if (action === 'folder') {
        const name = prompt(t('New folder'))?.trim();
        if (name) void run(() => api.POST('/api/v1/files/folders', { body: { parentId: folderId() ?? null, name } }).then(unwrap));
      } else if (crumb !== undefined) { path.splice(Number(crumb) + 1); void load(); }
      else if (rename) {
        const [kind, id] = rename.split(':');
        const row = target.closest<HTMLElement>('tr')!;
        const name = prompt(t('Rename'), row.dataset.name)?.trim();
        if (name && name !== row.dataset.name) {
          void run(() => (kind === 'folder'
            ? api.PUT('/api/v1/files/folders/{id}/name', { params: { path: { id } }, body: { name } })
            : api.PUT('/api/v1/files/{id}/name', { params: { path: { id } }, body: { name } })).then(unwrap));
        }
      } else if (remove) {
        const [kind, id] = remove.split(':');
        const name = target.closest<HTMLElement>('tr')!.dataset.name;
        if (confirm(kind === 'folder' ? t("Delete the folder '{0}' and everything in it?", name) : t("Delete the file '{0}'?", name))) {
          void run(() => api.POST('/api/v1/files/delete', { body: kind === 'folder' ? { fileIds: [], folderIds: [id] } : { fileIds: [id], folderIds: [] } }).then(unwrap));
        }
      } else if (!target.closest('a, button')) {
        const row = target.closest<HTMLElement>('tr[data-folder], tr[data-file]');
        if (row?.dataset.folder) { path.push({ id: row.dataset.folder, name: row.dataset.name! }); void load(); }
        else if (row?.dataset.file) coworkee.airWorks.openApp(filePreviewAppId, { id: row.dataset.file, name: row.dataset.name!, contentType: row.dataset.type! });
      }
    });
    host.querySelector<HTMLInputElement>('input[type=file]')!.addEventListener('change', (event) => {
      const input = event.target as HTMLInputElement;
      if (input.files?.length) void upload(input.files);
      input.value = '';
    });
    const drop = host.querySelector<HTMLElement>('.cw-drop')!;
    drop.addEventListener('dragover', (event) => { if (content?.canUpload && event.dataTransfer?.types.includes('Files')) { event.preventDefault(); drop.classList.add('over'); } });
    drop.addEventListener('dragleave', () => drop.classList.remove('over'));
    drop.addEventListener('drop', (event) => {
      drop.classList.remove('over');
      if (!event.dataTransfer?.files.length) return;
      event.preventDefault();
      void upload(event.dataTransfer.files);
    });
    void load();
  },
});
