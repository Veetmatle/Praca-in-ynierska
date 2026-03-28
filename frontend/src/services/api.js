/**
 * API service — handles all HTTP communication with the backend.
 * Includes automatic token refresh on 401 responses.
 */

const API_BASE = '/api';

let accessToken = null;
let onAuthFailure = null;

export function setAccessToken(token) {
  accessToken = token;
}

export function getAccessToken() {
  return accessToken;
}

export function setOnAuthFailure(callback) {
  onAuthFailure = callback;
}

async function request(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', ...options.headers };
  if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`;

  const response = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (response.status === 401 && !options._isRetry) {
    // Try refresh
    const refreshed = await refreshToken();
    if (refreshed) {
      return request(path, { ...options, _isRetry: true });
    }
    if (onAuthFailure) onAuthFailure();
    throw new Error('Unauthorized');
  }

  return response;
}

// ── Auth ────────────────────────────────────────────────

export async function login(username, password) {
  const res = await fetch(`${API_BASE}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || 'Błąd logowania');
  }
  const data = await res.json();
  accessToken = data.accessToken;
  return data;
}

export async function refreshToken() {
  try {
    const res = await fetch(`${API_BASE}/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
    });
    if (!res.ok) return false;
    const data = await res.json();
    accessToken = data.accessToken;
    return data;
  } catch {
    return false;
  }
}

export async function logout() {
  await request('/auth/logout', { method: 'POST', credentials: 'include' }).catch(() => {});
  accessToken = null;
}

export async function changePassword(currentPassword, newPassword) {
  const res = await request('/auth/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  });
  return res.ok;
}

// ── Chat Sessions ───────────────────────────────────────

export async function getSessions(category) {
  const params = category ? `?category=${category}` : '';
  const res = await request(`/chat/sessions${params}`);
  return res.json();
}

export async function getSession(publicId) {
  const res = await request(`/chat/sessions/${publicId}`);
  return res.json();
}

export async function createSession(title, category) {
  const res = await request('/chat/sessions', {
    method: 'POST',
    body: JSON.stringify({ title, category }),
  });
  return res.json();
}

export async function deleteSession(publicId) {
  return request(`/chat/sessions/${publicId}`, { method: 'DELETE' });
}

// ── Configuration ───────────────────────────────────────

export async function getConfig() {
  const res = await request('/configuration');
  if (!res.ok) return null;
  return res.json();
}

export async function updateConfig(data) {
  const res = await request('/configuration', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
  return res.json();
}

// ── Admin ───────────────────────────────────────────────

export async function getUsers(includeDeleted = false) {
  const res = await request(`/admin/users?includeDeleted=${includeDeleted}`);
  return res.json();
}

export async function createUser(username, displayName, password) {
  const res = await request('/admin/users', {
    method: 'POST',
    body: JSON.stringify({ username, displayName, password }),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || 'Błąd tworzenia konta');
  }
  return res.json();
}

export async function deleteUser(publicId) {
  return request(`/admin/users/${publicId}`, { method: 'DELETE' });
}

export async function restoreUser(publicId) {
  return request(`/admin/users/${publicId}/restore`, { method: 'POST' });
}

export async function togglePin(publicId) {
  const res = await request(`/chat/sessions/${publicId}/pin`, { method: 'POST' });
  return res.json();
}
