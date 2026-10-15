import type { AppContext, Intent } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { esc } from '../dom.ts';
import { icons } from '../icons.ts';
import { mudExFrame } from '../mudex.ts';

export const filePreviewAppId = 'coworkee.file-preview';

/** One file in a window, shown by MudEx's file display (pdf, images, office documents, archives, markdown, audio, video) in its own frame. */
export const filePreviewApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: filePreviewAppId,
  title: coworkee.t('Preview'),
  icon: icons.description,
  accent: '#b26a00',
  width: 900,
  height: 640,
  hidden: true,
  requiredPermissions: ['Files.View'],
  render: ({ host, intent, onIntent, setTitle, setState }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-preview"><p class="cw-muted">${esc(coworkee.t('MudDataGrid_Loading'))}</p></div>`;
    const show = (file?: Intent) => {
      if (!file?.id) return;
      setTitle(file.name || coworkee.t('Preview'));
      setState(file, file.name);
      const attributes: Record<string, string> = { url: `/api/v1/files/${encodeURIComponent(file.id)}/content`, 'show-file-name': 'false' };
      if (file.contentType) attributes['content-type'] = file.contentType;
      if (file.name) attributes['file-name'] = file.name;
      host.querySelector('.cw-preview')!.replaceChildren(mudExFrame('mudex-file-display', attributes, { frameUrl: coworkee.mudExFrameUrl, dark: coworkee.mode === 'dark', title: file.name }));
    };
    onIntent(show);
    show(intent);
  },
});
