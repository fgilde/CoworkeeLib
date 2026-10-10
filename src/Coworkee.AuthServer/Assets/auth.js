// Account pages: password reveal, caps lock hint, the last used sign-in and one submit per form. Texts come from data-text-* on <body>.
(() => {
  const texts = document.body.dataset;
  const lastKey = 'coworkee.last-sign-in';
  const eye = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5C6.5 5 2.7 9.1 1.5 12c1.2 2.9 5 7 10.5 7s9.3-4.1 10.5-7C21.3 9.1 17.5 5 12 5Zm0 11.5a4.5 4.5 0 1 1 0-9 4.5 4.5 0 0 1 0 9Zm0-2.5a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z"/></svg>';

  for (const input of document.querySelectorAll('input[type=password]')) {
    const wrapper = document.createElement('div');
    wrapper.className = 'password';
    input.replaceWith(wrapper);
    wrapper.append(input);

    const reveal = document.createElement('button');
    reveal.type = 'button';
    reveal.className = 'reveal';
    reveal.innerHTML = eye;
    reveal.setAttribute('aria-pressed', 'false');
    reveal.setAttribute('aria-label', texts.textShow);
    reveal.addEventListener('click', () => {
      const shown = input.type === 'password';
      input.type = shown ? 'text' : 'password';
      reveal.setAttribute('aria-pressed', String(shown));
      reveal.setAttribute('aria-label', shown ? texts.textHide : texts.textShow);
      input.focus();
    });
    wrapper.append(reveal);

    const caps = document.createElement('p');
    caps.className = 'caps';
    caps.hidden = true;
    caps.setAttribute('role', 'status');
    caps.textContent = texts.textCaps;
    wrapper.after(caps);
    const check = e => { if (e.getModifierState) caps.hidden = !e.getModifierState('CapsLock'); };
    input.addEventListener('keydown', check);
    input.addEventListener('keyup', check);
    input.addEventListener('blur', () => caps.hidden = true);
  }

  const external = document.querySelector('form.external');
  if (external) {
    const remember = value => { try { localStorage.setItem(lastKey, value); } catch { } };
    let last = null;
    try { last = localStorage.getItem(lastKey); } catch { }
    const marked = last === 'password'
      ? document.querySelector('form[data-sign-in=password] button[type=submit]')
      : [...external.querySelectorAll('button[data-provider]')].find(b => b.dataset.provider === last);
    if (marked) {
      marked.classList.add('last-used');
      marked.dataset.lastUsed = texts.textLast;
    }

    external.addEventListener('submit', e => { if (e.submitter?.dataset.provider) remember(e.submitter.dataset.provider); });
    document.querySelector('form[data-sign-in=password]')?.addEventListener('submit', () => remember('password'));
  }

  for (const form of document.forms) {
    form.addEventListener('submit', e => {
      if (form.dataset.busy) {
        e.preventDefault();
        return;
      }

      form.dataset.busy = 'true';
      e.submitter?.setAttribute('aria-busy', 'true');
    });
  }

  // a page restored by the back button may still look busy
  window.addEventListener('pageshow', () => {
    for (const form of document.forms) delete form.dataset.busy;
    for (const button of document.querySelectorAll('[aria-busy]')) button.removeAttribute('aria-busy');
  });
})();
