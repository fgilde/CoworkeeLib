import { unwrap } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { errorText, esc } from '../dom.ts';

/** Name and phone of the signed-in user, the account pages of the auth server and sign out. */
export const profileApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.profile',
  title: coworkee.t('Profile'),
  icon: 'ratio-personal.png',
  accent: '#00777a',
  width: 520,
  height: 520,
  singleInstance: true,
  render: async ({ host }: AppContext) => {
    const { t, api } = coworkee;
    host.classList.add('cw-host');
    const profile = unwrap(await api.GET('/api/v1/identity/me'));
    const input = (name: string, label: string, value?: string | null) =>
      `<div class="cw-field"><label for="cw-profile-${name}">${esc(t(label))}</label><input id="cw-profile-${name}" name="${name}" value="${esc(value ?? '')}"/></div>`;
    host.innerHTML = `<div class="cw-app cw-profile"><header class="cw-profile-head">
        ${profile.avatarUrl ? `<img class="cw-avatar" src="${esc(profile.avatarUrl)}" alt=""/>` : `<span class="cw-avatar">${esc((coworkee.user.name ?? profile.email).charAt(0).toUpperCase())}</span>`}
        <div><strong>${esc(coworkee.user.name ?? '')}</strong><br/><span class="cw-muted">${esc(profile.email)}</span></div></header>
      <form>${input('firstName', 'First name', profile.firstName)}${input('lastName', 'Last name', profile.lastName)}${input('phoneNumber', 'Phone', profile.phoneNumber)}
        <div class="cw-actions">
          ${coworkee.user.manageUrl ? `<a class="cw-button" href="${esc(coworkee.user.manageUrl)}" target="_blank" rel="noopener">${esc(t('Account'))}</a>` : ''}
          <button type="button" data-action="logout">${esc(t('Sign out'))}</button>
          <button type="submit" class="cw-primary">${esc(t('Save'))}</button>
        </div></form></div>`;
    const form = host.querySelector('form')!;
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const values = Object.fromEntries(new FormData(form)) as Record<string, string>;
      try {
        unwrap(await api.PUT('/api/v1/identity/me', { body: { firstName: values.firstName || null, lastName: values.lastName || null, phoneNumber: values.phoneNumber || null, address: profile.address ?? null } }));
        coworkee.notify(t('Saved'));
      } catch (error) {
        coworkee.notify(errorText(error), 'error');
      }
    });
    host.querySelector('[data-action="logout"]')!.addEventListener('click', () => void coworkee.airWorks.logout());
  },
});
