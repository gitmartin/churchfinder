(() => {
  let csrf;
  let ready;
  const apiBase = new URL('/api/', location.origin).href;
  const endSession = async () => {
    const response = await fetch(new URL('/_state/session/end', location.origin), {
      method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'X-Branches-State-End': '1' }
    });
    if (!response.ok) throw new Error('Session could not be ended.');
  };
  const initialize = () => ready ??= fetch(apiBase + 'csrf', { credentials: 'same-origin', cache: 'no-store' })
    .then(async response => {
      if (response.status !== 409) return response;
      // A concurrent sign-in may have replaced the cookie used by the first request.
      // Retry with current cookies without revoking that newly authenticated session.
      return fetch(apiBase + 'csrf', { credentials: 'same-origin', cache: 'no-store' });
    })
    .then(async response => {
      if (response.status === 409) throw new Error('Your session needs to be restarted. Use Start a new session on the sign-in page.');
      if (!response.ok) throw new Error('Session could not be started.');
      csrf = (await response.json()).token;
    }).catch(error => { ready = undefined; throw error; });
  const decode = value => Uint8Array.from(atob(value.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));
  const encode = value => btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const credentialJson = credential => {
    if (credential.toJSON) return credential.toJSON();
    const response = { clientDataJSON: encode(credential.response.clientDataJSON) };
    for (const key of ['authenticatorData', 'signature', 'userHandle', 'attestationObject'])
      if (credential.response[key]) response[key] = encode(credential.response[key]);
    if (credential.response.getTransports) response.transports = credential.response.getTransports();
    return { id: credential.id, rawId: encode(credential.rawId), type: credential.type, response, clientExtensionResults: credential.getClientExtensionResults(), authenticatorAttachment: credential.authenticatorAttachment };
  };
  const safeStorage = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { } }
  };
  window.starter = {
    async resetSession() { ready = undefined; await initialize(); },
    async signOut() { await endSession(); ready = undefined; await initialize(); },
    async request(method, path, body) {
      if (!/^[a-zA-Z0-9/?=&._-]+$/.test(path) || path.includes('..')) throw new Error('Invalid API path.');
      await initialize();
      const response = await fetch(apiBase + path, {
        method, credentials: 'same-origin', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf },
        body: method === 'GET' ? undefined : JSON.stringify(body)
      });
      const text = await response.text();
      if (response.status === 401 || response.status === 403) {
        if (method === 'GET') throw new Error('Sign in is required.');
        return text || JSON.stringify({ success: false, message: 'Sign in is required for this action.' });
      }
      if (!response.ok && !text.startsWith('{')) return JSON.stringify({ success: false, message: response.status === 429 ? 'Too many requests. Please wait a minute.' : 'The request could not be completed.' });
      return text || '{}';
    },
    async passkey(register, email) {
      try {
        if (!window.PublicKeyCredential || !window.isSecureContext) return JSON.stringify({ success: false, message: 'Passkeys require HTTPS and a supported browser.' });
        const begin = JSON.parse(await this.request('POST', register ? 'user/passkeys/begin' : 'auth/passkeys/signin/begin', { email }));
        if (!begin.options) return JSON.stringify(begin);
        const options = begin.options;
        options.challenge = decode(options.challenge);
        if (options.user) options.user.id = decode(options.user.id);
        for (const key of ['allowCredentials', 'excludeCredentials'])
          if (options[key]) options[key] = options[key].map(x => ({ ...x, id: decode(x.id) }));
        const credential = register ? await navigator.credentials.create({ publicKey: options }) : await navigator.credentials.get({ publicKey: options });
        return await this.request('POST', register ? 'user/passkeys/complete' : 'auth/passkeys/signin/complete', { ceremonyId: begin.ceremonyId, credential: credentialJson(credential) });
      } catch (error) {
        return JSON.stringify({ success: false, message: error.name === 'NotAllowedError' ? 'The passkey prompt was cancelled or timed out.' : 'Your device could not complete the passkey request.' });
      }
    },
    theme(value) {
      if (!['Forest', 'Ocean', 'Violet', 'Ember', 'Slate'].includes(value)) value = 'Forest';
      document.documentElement.dataset.theme = value.toLowerCase();
      safeStorage.set('starter-theme', value);
    },
    mode(cycle) {
      const modes = ['system', 'light', 'dark'];
      let mode = safeStorage.get('starter-mode') || 'system';
      if (cycle) mode = modes[(modes.indexOf(mode) + 1) % modes.length];
      safeStorage.set('starter-mode', mode);
      document.documentElement.dataset.mode = mode === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : mode;
      return { system: 'System appearance', light: 'Light appearance', dark: 'Dark appearance' }[mode];
    }
  };
  starter.theme(safeStorage.get('starter-theme') || 'Forest');
  starter.mode(false);
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => starter.mode(false));
})();
