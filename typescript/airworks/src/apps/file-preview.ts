import type { AppContext, Intent } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { errorText, esc } from '../dom.ts';
import { loadMudEx } from '../mudex.ts';

export const filePreviewAppId = 'coworkee.file-preview';

/** One file in a window, shown by MudEx's file display (pdf, images, office documents, archives, markdown, audio, video). */
export const filePreviewApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: filePreviewAppId,
  title: coworkee.t('Preview'),
  icon: 'fn-analysis.png',
  accent: '#b26a00',
  width: 900,
  height: 640,
  hidden: true,
  requiredPermissions: ['Files.View'],
  render: ({ host, intent, onIntent, setTitle, setState }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-preview"><p class="cw-muted">${esc(coworkee.t('MudDataGrid_Loading'))}</p></div>`;
    const show = async (file?: Intent) => {
      if (!file?.id) return;
      setTitle(file.name || coworkee.t('Preview'));
      setState(file, file.name);
      try {
        await loadMudEx(coworkee.mudExUrl, coworkee.mode === 'dark');
        const display = document.createElement('mudex-file-display');
        display.className = 'cw-file-display';
        display.setAttribute('url', `/api/v1/files/${encodeURIComponent(file.id)}/content`);
        if (file.contentType) display.setAttribute('content-type', file.contentType);
        if (file.name) display.setAttribute('file-name', file.name);
        display.setAttribute('show-file-name', 'false');
        host.querySelector('.cw-preview')!.replaceChildren(display);
      } catch (error) {
        host.querySelector('.cw-preview')!.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`;
      }
    };
    onIntent((next) => void show(next));
    void show(intent);
  },
});
