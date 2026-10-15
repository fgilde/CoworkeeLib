import { postNdjson, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc } from '../dom.ts';
import { confirmDialog } from '../form.ts';
import { icons } from '../icons.ts';
import { settingsPageApp } from './settings.ts';

type Message = Schemas['AiMessageDto'];
type Session = Schemas['AiSessionDto'];
type StreamEvent = { type: 'delta' | 'tool' | 'done' | 'error'; text?: string | null; toolCall?: Schemas['AiToolCallDto'] | null; message?: Message | null };

const ai = '/api/v1/ai';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });

function messageHtml(coworkee: Coworkee, message: Message, last: boolean): string {
  const { t } = coworkee;
  const tools = message.toolCalls.map((call) => `<li class="${call.succeeded ? '' : 'cw-error'}">${esc(call.tool)}${call.error ? `: ${esc(call.error)}` : ''}</li>`).join('');
  const heading = message.decision ? (message.decision === 'approved' ? 'Confirmed:' : 'Declined:') : 'Confirm to run:';
  const pending = message.pending.length > 0 ? `<div class="cw-pending"><p><strong>${esc(t(heading))}</strong></p><ul>${message.pending.map((tool) =>
    `<li><strong>${esc(tool.tool)}</strong><pre class="cw-code">${esc(tool.input)}</pre></li>`).join('')}</ul>
    ${!message.decision && last
      ? `<div class="cw-actions"><button data-confirm="false">${esc(t('Cancel'))}</button><button data-confirm="true" class="cw-primary">${esc(t('Run'))}</button></div>` : ''}</div>` : '';
  return `<li class="cw-bubble ${message.role === 'user' ? 'mine' : ''}"><div class="cw-text">${esc(message.text)}</div>${tools ? `<ul class="cw-tools">${tools}</ul>` : ''}${pending}</li>`;
}

/** The AI assistant: sessions, answers streamed as they arrive, tool calls and confirmations for tools that change data. */
export const assistantApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.assistant', title: coworkee.t('Assistant'), icon: icons.sparkles, accent: '#6a1b9a', width: 900, height: 640,
  requiredPermissions: ['Ai.Chat'], probe: `${ai}/status`,
  render: async ({ host, setState, intent }: AppContext) => {
    const { t, request, culture } = coworkee;
    host.classList.add('cw-host');
    const status = await request<Schemas['AiStatusDto']>(`${ai}/status`);
    host.innerHTML = `<div class="cw-app cw-chat"><aside class="cw-stack"><div class="cw-toolbar"><button data-action="new" class="cw-primary">${esc(t('New conversation'))}</button></div><ul class="cw-list cw-sessions"></ul></aside>
      <section class="cw-stack"><ul class="cw-messages" aria-live="polite"></ul>
      <form class="cw-composer"><textarea name="text" rows="2" placeholder="${esc(t('Ask the assistant…'))}" aria-label="${esc(t('Message'))}" ${status.available ? '' : 'disabled'}></textarea>
        <button type="submit" class="cw-primary" ${status.available ? '' : 'disabled'}>${esc(t('Send'))}</button></form>
      ${status.available ? '' : `<p class="cw-muted">${esc(t('The assistant is not set up yet.'))}</p>`}</section></div>`;
    const sessionsList = host.querySelector<HTMLElement>('.cw-sessions')!;
    const list = host.querySelector<HTMLElement>('.cw-messages')!;
    const form = host.querySelector('form')!;
    let current: string | undefined = intent?.session;
    let messages: Message[] = [];
    let busy = false;

    const renderMessages = (live?: string) => {
      list.innerHTML = messages.map((message, index) => messageHtml(coworkee, message, index === messages.length - 1)).join('')
        + (live !== undefined ? `<li class="cw-bubble"><div class="cw-text">${esc(live) || '&hellip;'}</div></li>` : '');
      list.scrollTop = list.scrollHeight;
    };
    const loadSessions = async () => {
      const sessions = await request<Session[]>(`${ai}/sessions`);
      sessionsList.innerHTML = sessions.map((session) => `<li><button data-session="${esc(session.id)}" aria-current="${session.id === current}" title="${esc(display(session.updatedAt, culture))}">${esc(session.title)}</button>
        <button data-delete="${esc(session.id)}" title="${esc(t('Delete'))}" aria-label="${esc(t('Delete'))}">&#x1f5d1;</button></li>`).join('');
    };
    const open = async (id?: string) => {
      current = id;
      setState(id ? { session: id } : {});
      messages = id ? (await request<Schemas['AiSessionDetailDto']>(`${ai}/sessions/${id}`)).messages : [];
      renderMessages();
      await loadSessions();
    };
    const stream = async (path: string, body: unknown) => {
      busy = true;
      let text = '';
      try {
        await postNdjson<StreamEvent>(path, body, (event) => {
          if (event.type === 'delta') renderMessages(text += event.text ?? '');
          else if (event.type === 'tool' && event.toolCall) renderMessages(text += `\n[${event.toolCall.tool}]\n`);
          else if (event.type === 'error') coworkee.notify(event.text ?? t('Failed'), 'error');
        });
      } catch (error) {
        coworkee.notify(errorText(error), 'error');
      } finally {
        busy = false;
      }
      await open(current);
    };
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const input = form.querySelector('textarea')!;
      const text = input.value.trim();
      if (!text || busy) return;
      input.value = '';
      current ??= (await request<Session>(`${ai}/sessions`, json('POST', { title: text.slice(0, 60) }))).id;
      messages.push({ id: '', role: 'user', text, toolCalls: [], pending: [], decision: null, at: new Date().toISOString() });
      renderMessages('');
      await stream(`${ai}/sessions/${current}/messages`, { text });
    });
    form.querySelector('textarea')!.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); form.requestSubmit(); }
    });
    host.addEventListener('click', async (event) => {
      const target = event.target as Element;
      const session = target.closest<HTMLElement>('[data-session]')?.dataset.session;
      const remove = target.closest<HTMLElement>('[data-delete]')?.dataset.delete;
      const confirm = target.closest<HTMLElement>('[data-confirm]')?.dataset.confirm;
      if (target.closest('[data-action=new]')) await open();
      else if (session) await open(session);
      else if (remove && await confirmDialog(coworkee, t('Delete {0}? This cannot be undone.', target.closest('li')?.querySelector('[data-session]')?.textContent?.trim() ?? ''), t('Delete'))) {
        await request(`${ai}/sessions/${remove}`, json('DELETE'));
        await open(remove === current ? undefined : current);
      } else if (confirm && current && !busy) await stream(`${ai}/sessions/${current}/confirm`, { approve: confirm === 'true' });
    });
    await open(current);
  },
});

const aiFields = ['Provider', 'Model', 'Endpoint', 'ApiKey', 'Headers', 'AuthHeader'];

/** Provider, model and key of the assistant, with a test call to the model. */
export const aiSettingsApp = settingsPageApp({
  id: 'coworkee.admin.ai-settings', group: 'System', title: 'AI', icon: icons.smartToy, accent: '#6a1b9a', page: '/admin/ai-settings', hostOnly: true, scopes: ['Global'], probe: `${ai}/status`,
  test: {
    title: 'Test connection',
    run: async (coworkee, values, changed) => {
      const draft = Object.fromEntries(aiFields.map((name) => [name.charAt(0).toLowerCase() + name.slice(1), (name === 'ApiKey' ? changed : values)[`Ai.${name}`] || null]));
      const result = await coworkee.request<Schemas['AiConnectionTestDto']>(`${ai}/connection/test`, json('POST', draft));
      if (!result.succeeded) throw new Error(result.error ?? coworkee.t('Failed'));
      return `${coworkee.t('Connected in {0} ms', result.latencyMs)}${result.reply ? `: ${result.reply}` : ''}`;
    },
  },
});
