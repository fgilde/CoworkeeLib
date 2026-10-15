import { realtimeEvents, realtimeTopics, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc } from '../dom.ts';
import { icons } from '../icons.ts';

type Contact = Schemas['ChatContactDto'];
type Message = Schemas['ChatMessageDto'];

const chat = '/api/v1/chat';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });

/** Direct messages with the people of the organisation: who is online, unread counts, messages arriving live. */
export const chatApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.chat', title: coworkee.t('Chat'), icon: icons.chat, accent: '#00897b', width: 860, height: 600,
  requiredPermissions: ['Chat.Use'], probe: `${chat}/unread-count`,
  render: async ({ host, intent, setState, onClose }: AppContext) => {
    const { t, request, culture, user, realtime } = coworkee;
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-chat"><aside class="cw-stack"><input type="search" placeholder="${esc(t('Search'))}" aria-label="${esc(t('Search'))}"/><ul class="cw-list cw-contacts"></ul></aside>
      <section class="cw-stack"><header class="cw-chat-head cw-muted">${esc(t('Pick a person to chat with.'))}</header><ul class="cw-messages" aria-live="polite"></ul>
      <form class="cw-composer" hidden><textarea name="text" rows="2" aria-label="${esc(t('Message'))}" placeholder="${esc(t('Write a message'))}"></textarea><button type="submit" class="cw-primary">${esc(t('Send'))}</button></form></section></div>`;
    const contactsList = host.querySelector<HTMLElement>('.cw-contacts')!;
    const list = host.querySelector<HTMLElement>('.cw-messages')!;
    const head = host.querySelector<HTMLElement>('.cw-chat-head')!;
    const form = host.querySelector('form')!;
    const search = host.querySelector('input')!;
    let contacts: Contact[] = [];
    let online = new Set<string>();
    let partner: string | undefined = intent?.user;
    let messages: Message[] = [];

    const renderContacts = () => {
      const text = search.value.toLowerCase();
      contactsList.innerHTML = contacts.filter((contact) => contact.name.toLowerCase().includes(text)).map((contact) => `<li><button data-user="${esc(contact.userId)}" aria-current="${contact.userId === partner}">
          <span class="cw-presence ${online.has(contact.userId) ? 'on' : ''}" title="${esc(t(online.has(contact.userId) ? 'Online' : 'Offline'))}"></span>
          <span class="cw-grow"><strong>${esc(contact.name)}</strong><br/><small class="cw-muted">${esc(contact.lastMessage ?? '')}</small></span>
          ${Number(contact.unread) > 0 ? `<span class="cw-badge">${esc(contact.unread)}</span>` : ''}</button></li>`).join('')
        || `<li class="cw-muted">${esc(t('Nothing found.'))}</li>`;
    };
    const renderMessages = () => {
      list.innerHTML = messages.map((message) => `<li class="cw-bubble ${message.fromUserId === user.userId ? 'mine' : ''}"><div class="cw-text">${esc(message.text)}</div>
        <small class="cw-muted">${esc(display(message.sentAt, culture))}${message.fromUserId === user.userId && message.readAt ? ' &#x2713;&#x2713;' : ''}</small></li>`).join('');
      list.scrollTop = list.scrollHeight;
    };
    const loadContacts = async () => {
      const [loaded, present] = await Promise.all([request<Contact[]>(`${chat}/contacts`), request<string[]>('/api/v1/realtime/presence').catch(() => [])]);
      contacts = loaded;
      online = new Set(present);
      renderContacts();
    };
    const open = async (userId: string) => {
      partner = userId;
      setState({ user: userId });
      const contact = contacts.find((item) => item.userId === userId);
      head.textContent = contact?.name ?? '';
      head.classList.remove('cw-muted');
      form.hidden = false;
      messages = await request<Message[]>(`${chat}/conversations/${userId}`);
      renderMessages();
      if (Number(contact?.unread) > 0) await request(`${chat}/conversations/${userId}/read`, json('POST'));
      await loadContacts();
    };

    contactsList.addEventListener('click', (event) => {
      const id = (event.target as Element).closest<HTMLElement>('[data-user]')?.dataset.user;
      if (id) void open(id).catch((error) => coworkee.notify(errorText(error), 'error'));
    });
    search.addEventListener('input', renderContacts);
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const input = form.querySelector('textarea')!;
      const text = input.value.trim();
      if (!text || !partner) return;
      try {
        const sent = await request<Message>(`${chat}/conversations/${partner}`, json('POST', { text }));
        input.value = '';
        if (!messages.some((message) => message.id === sent.id)) messages.push(sent);
        renderMessages();
      } catch (error) {
        coworkee.notify(errorText(error), 'error');
      }
    });
    form.querySelector('textarea')!.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); form.requestSubmit(); }
    });

    const stops = [
      realtime.subscribe(realtimeTopics.user(user.userId ?? ''), ({ type, payload }) => {
        if (type === realtimeEvents.chatMessage) {
          const message = payload as Message;
          if (partner && (message.fromUserId === partner || message.toUserId === partner) && !messages.some((item) => item.id === message.id)) {
            messages.push(message);
            renderMessages();
            if (message.fromUserId === partner) void request(`${chat}/conversations/${partner}/read`, json('POST'));
          }
          void loadContacts();
        } else if (type === realtimeEvents.chatRead) {
          void loadContacts();
          if (partner) void request<Message[]>(`${chat}/conversations/${partner}`).then((loaded) => { messages = loaded; renderMessages(); });
        }
      }),
      realtime.subscribe(realtimeTopics.presence, ({ payload }) => {
        const presence = payload as { userId: string; online: boolean };
        if (presence.online) online.add(presence.userId); else online.delete(presence.userId);
        renderContacts();
      }),
    ];
    onClose(() => stops.forEach((stop) => stop()));
    await loadContacts();
    if (partner) await open(partner);
  },
});
