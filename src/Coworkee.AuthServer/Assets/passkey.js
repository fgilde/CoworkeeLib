// Passkeys: sign-in by button or autofill (conditional UI) and adding one on the account page. Texts come from data-text-* on <body>.
(() => {
  if (!window.PublicKeyCredential || !navigator.credentials) return;

  const texts = document.body.dataset;
  const bytes = text => Uint8Array.from(atob(text.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0)).buffer;
  const text = buffer => btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const ids = list => list?.map(c => ({ ...c, id: bytes(c.id) }));

  const requestOptions = json => PublicKeyCredential.parseRequestOptionsFromJSON
    ? PublicKeyCredential.parseRequestOptionsFromJSON(json)
    : { ...json, challenge: bytes(json.challenge), allowCredentials: ids(json.allowCredentials) };
  const creationOptions = json => PublicKeyCredential.parseCreationOptionsFromJSON
    ? PublicKeyCredential.parseCreationOptionsFromJSON(json)
    : { ...json, challenge: bytes(json.challenge), user: { ...json.user, id: bytes(json.user.id) }, excludeCredentials: ids(json.excludeCredentials) };

  const encode = credential => {
    if (credential.toJSON) return credential.toJSON();
    const r = credential.response, response = {};
    for (const key of ['clientDataJSON', 'attestationObject', 'authenticatorData', 'signature', 'userHandle']) if (r[key]) response[key] = text(r[key]);
    if (r.getTransports) response.transports = r.getTransports();
    return { id: credential.id, rawId: text(credential.rawId), type: credential.type, authenticatorAttachment: credential.authenticatorAttachment,
      clientExtensionResults: credential.getClientExtensionResults(), response };
  };

  const options = async form => {
    const response = await fetch(form.dataset.options, {
      method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: form.elements.__RequestVerificationToken.value },
    });
    if (!response.ok) throw new Error(`options ${response.status}`);
    return response.json();
  };

  // a dismissed prompt or a request replaced by a newer one is no error
  const failed = (form, error) => {
    if (error?.name === 'AbortError') return;
    form.querySelector('button')?.removeAttribute('aria-busy');
    if (error?.name === 'NotAllowedError') return;
    const alert = form.querySelector('.error');
    alert.textContent = texts.textPasskeyFailed;
    alert.hidden = false;
  };

  const submit = (form, credential) => {
    form.elements.credential.value = JSON.stringify(encode(credential));
    form.requestSubmit();
  };

  for (const hint of document.querySelectorAll('[data-passkey=unsupported]')) hint.hidden = true;

  const signIn = document.querySelector('form[data-passkey=sign-in]');
  if (signIn) {
    signIn.hidden = false;
    const button = signIn.querySelector('button');
    let pending = null;
    const run = async mediation => {
      pending?.abort();
      const abort = pending = new AbortController();
      const publicKey = requestOptions(await options(signIn));
      submit(signIn, await navigator.credentials.get({ publicKey, mediation, signal: abort.signal }));
    };

    button.addEventListener('click', () => {
      button.setAttribute('aria-busy', 'true');
      run('optional').catch(e => failed(signIn, e));
    });
    PublicKeyCredential.isConditionalMediationAvailable?.().then(available => available && run('conditional')).catch(e => failed(signIn, e));
  }

  const add = document.querySelector('form[data-passkey=add]');
  if (add) {
    add.hidden = false;
    const name = add.elements.namedItem('name');
    const ua = navigator.userAgent;
    const browser = /Edg\//.test(ua) ? 'Edge' : /Firefox\//.test(ua) ? 'Firefox' : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : '';
    const platform = navigator.userAgentData?.platform
      || (/iPhone|iPad/.test(ua) ? 'iOS' : /Android/.test(ua) ? 'Android' : /Windows/.test(ua) ? 'Windows' : /Mac/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : '');
    name.value ||= [browser, platform].filter(Boolean).join(' · ');

    const button = add.querySelector('button');
    const create = async () => {
      button.setAttribute('aria-busy', 'true');
      const publicKey = creationOptions(await options(add));
      submit(add, await navigator.credentials.create({ publicKey }));
    };

    button.addEventListener('click', () => create().catch(e => failed(add, e)));
    // Enter in the name field would send the form without a passkey
    name.addEventListener('keydown', e => {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      button.click();
    });
  }
})();
