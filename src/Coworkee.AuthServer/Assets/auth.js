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

  // documents step of the registration: each slot becomes a drop zone that checks type and size as the server does (RegistrationDocuments.Validate);
  // the file field itself lies transparent over the zone, so click, keyboard and dropping a file stay native. Registered before the busy marker below.
  const documents = document.querySelector('[data-documents]');
  if (documents) {
    const icons = {
      upload: documents.querySelector('.drop-icon path')?.getAttribute('d'),
      image: 'M21 19V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2ZM8.5 13.5l2.5 3 3.5-4.5 4.5 6H5l3.5-4.5Z',
      pdf: 'M20 2H8a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2Zm-8.5 7.5c0 .83-.67 1.5-1.5 1.5H9v2H7.5V7H10c.83 0 1.5.67 1.5 1.5v1Zm5 2c0 .83-.67 1.5-1.5 1.5h-2.5V7H15c.83 0 1.5.67 1.5 1.5v3Zm4-3H19v1h1.5V11H19v2h-1.5V7h3v1.5ZM9 9.5h1v-1H9v1ZM4 6H2v14a2 2 0 0 0 2 2h14v-2H4V6Zm10 5.5h1v-3h-1v3Z',
      file: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Zm2 16H8v-2h8v2Zm0-4H8v-2h8v2Zm-3-5V3.5L18.5 9H13Z',
      remove: 'M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 19 6.41Z',
    };
    const svg = d => `<svg viewBox="0 0 24 24"><path d="${d}"/></svg>`;
    const lang = document.documentElement.lang || undefined;
    const size = bytes => bytes >= 1048576
      ? `${(bytes / 1048576).toLocaleString(lang, { maximumFractionDigits: 1 })} MB`
      : `${Math.ceil(bytes / 1024).toLocaleString(lang)} KB`;
    // * is the only wildcard, without case, like Wildcards.Allows; an empty list allows every type
    const allows = (patterns, type) => !patterns.length || patterns.some(p =>
      new RegExp(`^${p.trim().replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*')}$`, 'i').test(type));
    const missing = document.querySelector('[data-documents-missing]');
    const required = s => s.slot.dataset.required === 'true';

    const summarize = () => {
      const open = slots.filter(s => required(s) && !s.file);
      missing.querySelector('ul').replaceChildren(...open.map(s => Object.assign(document.createElement('li'), { textContent: s.slot.dataset.name })));
      missing.hidden = !open.length;
      if (!open.length) missing.classList.remove('blocked');
    };

    const slots = [...documents.querySelectorAll('[data-document]')].map(slot => {
      const input = slot.querySelector('input[type=file]');
      const zone = slot.querySelector('.drop');
      const name = slot.querySelector('[data-file-name]');
      const empty = name.textContent;
      const error = slot.querySelector('[data-document-error]');
      const patterns = slot.dataset.contentTypes ? slot.dataset.contentTypes.split(',') : [];
      const max = Number(slot.dataset.maxSize) || 0;
      // the summary names what is missing; the browser's own bubble on the transparent field would point nowhere
      input.required = false;
      error.id = `${input.id}-error`;
      input.setAttribute('aria-describedby', error.id);

      const state = {
        slot, input,
        get file() { return input.files[0]; },
        get message() {
          const file = this.file;
          if (!file) return null;
          if (!allows(patterns, file.type || 'application/octet-stream')) return slot.dataset.textType;
          return max && file.size > max ? slot.dataset.textSize : null;
        },
        update() {
          const file = this.file;
          const message = this.message;
          slot.classList.toggle('has-file', !!file);
          slot.classList.toggle('invalid', !!message);
          name.textContent = file ? file.name : empty;
          slot.querySelector('[data-file-size]').textContent = file ? size(file.size) : '';
          slot.querySelector('.drop-icon').innerHTML = svg(!file ? icons.upload
            : file.type.startsWith('image/') ? icons.image : file.type === 'application/pdf' ? icons.pdf : icons.file);
          remove.hidden = !file;
          error.textContent = message ?? '';
          error.hidden = !message;
          input.setAttribute('aria-invalid', String(!!message));
          summarize();
        },
      };

      const remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'drop-remove';
      remove.title = documents.dataset.textRemove;
      remove.setAttribute('aria-label', `${documents.dataset.textRemove}: ${slot.dataset.name}`);
      remove.innerHTML = svg(icons.remove);
      remove.addEventListener('click', () => { input.value = ''; state.update(); input.focus(); });
      zone.append(remove);

      input.addEventListener('change', () => state.update());
      for (const type of ['dragenter', 'dragover']) input.addEventListener(type, () => zone.classList.add('dragging'));
      for (const type of ['dragleave', 'drop']) input.addEventListener(type, () => zone.classList.remove('dragging'));
      return state;
    });

    documents.classList.add('enhanced');
    // also shows the files a page restored by the back button still holds
    for (const s of slots) s.update();

    documents.closest('form').addEventListener('submit', e => {
      if (e.submitter?.formNoValidate) return;
      const wrong = slots.find(s => s.message) ?? slots.find(s => required(s) && !s.file);
      if (!wrong) return;
      e.preventDefault();
      missing.classList.toggle('blocked', !missing.hidden);
      wrong.input.focus({ preventScroll: true });
      (wrong.message || missing.hidden ? wrong.slot : missing).scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    });
  }

  for (const form of document.forms) {
    form.addEventListener('submit', e => {
      if (e.defaultPrevented) return;
      if (form.dataset.busy) {
        e.preventDefault();
        return;
      }

      form.dataset.busy = 'true';
      e.submitter?.setAttribute('aria-busy', 'true');
    });
  }

  // the panel's and the page's videos stand still when the system asks for reduced motion
  if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
    for (const video of document.querySelectorAll('video[autoplay]')) {
      video.removeAttribute('autoplay');
      video.pause();
    }
  }

  // a page restored by the back button may still look busy
  window.addEventListener('pageshow', () => {
    for (const form of document.forms) delete form.dataset.busy;
    for (const button of document.querySelectorAll('[aria-busy]')) button.removeAttribute('aria-busy');
  });
})();
