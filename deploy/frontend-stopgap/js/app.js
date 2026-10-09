// Temporary stand-in for the Angular application: login, forgot password, reset password, minimal session view.
// It only calls the public same-origin URLs (/auth/*). The access token lives in memory; the refresh cookie
// (HttpOnly, Path=/auth) is managed by the browser.
(() => {
  'use strict';

  const views = ['login', 'forgot', 'reset', 'session'];
  const alertBox = document.getElementById('alert');
  let accessToken = null;

  const show = (name) => {
    for (const view of views) document.getElementById(`view-${view}`).hidden = view !== name;
    notify();
  };

  const notify = (message, kind = 'danger') => {
    alertBox.className = message ? `alert alert-${kind}` : 'alert d-none';
    alertBox.textContent = message || '';
  };

  const route = () => {
    if (accessToken) return show('session');
    const name = location.hash.replace('#/', '');
    show(views.includes(name) && name !== 'session' ? name : 'login');
  };

  const post = (path, body) => fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined,
  });

  const claims = (token) => {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(decodeURIComponent(escape(atob(payload))));
  };

  const startSession = (token, email) => {
    accessToken = token;
    const data = claims(token);
    const roles = [].concat(data.role || data['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || []);
    document.getElementById('session-user').textContent = data.email || email || data.sub;
    document.getElementById('session-roles').textContent = roles.length ? roles.join(', ') : 'Sin roles';
    document.getElementById('session-expires').textContent = new Date(data.exp * 1000).toLocaleString();
    show('session');
  };

  const problem = async (response, fallback) => {
    if (response.status === 429) return 'Demasiados intentos. Espera un momento e inténtalo de nuevo.';
    if (response.status === 503) return 'El servicio no está disponible por ahora. Inténtalo más tarde.';
    try { return (await response.json()).detail || fallback; } catch { return fallback; }
  };

  const submit = (id, handler) => {
    const form = document.getElementById(id);
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!form.reportValidity()) return;
      const button = form.querySelector('button[type=submit]');
      button.disabled = true;
      notify();
      try {
        await handler(Object.fromEntries(new FormData(form)), form);
      } catch {
        notify('No se pudo contactar con el servidor.');
      } finally {
        button.disabled = false;
      }
    });
  };

  submit('form-login', async ({ email, password }, form) => {
    const response = await post('/auth/login', { email, password });
    form.elements.password.value = '';
    if (!response.ok) return notify(await problem(response, 'Credenciales inválidas.'));
    startSession((await response.json()).accessToken, email);
  });

  submit('form-forgot', async ({ email }, form) => {
    const response = await post('/auth/forgot-password', { email });
    if (!response.ok) return notify(await problem(response, 'Revisa el correo ingresado.'));
    form.reset();
    // Same answer whether or not the account exists.
    notify('Si el correo corresponde a una cuenta, recibirás un código para restablecer la contraseña.', 'success');
  });

  submit('form-reset', async ({ email, token, newPassword }, form) => {
    const response = await post('/auth/reset-password', { email, token: token.trim(), newPassword });
    form.elements.newPassword.value = '';
    if (!response.ok) return notify(await problem(response, 'El código no es válido o venció.'));
    form.reset();
    location.hash = '#/login';
    notify('Contraseña actualizada. Ya puedes iniciar sesión.', 'success');
  });

  document.getElementById('btn-logout').addEventListener('click', async () => {
    try { await post('/auth/logout'); } finally {
      accessToken = null;
      location.hash = '#/login';
      route();
      notify('Sesión cerrada.', 'success');
    }
  });

  window.addEventListener('hashchange', route);
  route();
})();
