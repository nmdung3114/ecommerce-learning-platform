/**
 * HTTP API Client - fetch wrapper with JWT auth
 */
const BASE_URL = 'http://localhost:8000/api';

async function request(method, path, body = null, auth = false) {
  const headers = { 'Content-Type': 'application/json' };
  const token = localStorage.getItem('el_token');
  if (auth && token) headers['Authorization'] = `Bearer ${token}`;

  const config = { method, headers };
  if (body !== null) config.body = JSON.stringify(body);

  let response;
  try {
    response = await fetch(`${BASE_URL}${path}`, config);
  } catch {
    throw { status: 0, message: 'Không thể kết nối đến server. Vui lòng thử lại.' };
  }

  let data;
  try { data = await response.json(); } catch { data = {}; }

  if (!response.ok) {
    if (response.status === 401) {
      localStorage.removeItem('el_token');
      localStorage.removeItem('el_user');
    }
    throw { status: response.status, message: data.detail || 'Có lỗi xảy ra. Vui lòng thử lại.' };
  }
  return data;
}

export const api = {
  get:    (path, auth = false)         => request('GET',    path, null, auth),
  post:   (path, body, auth = false)   => request('POST',   path, body, auth),
  put:    (path, body, auth = false)   => request('PUT',    path, body, auth),
  delete: (path, auth = false)         => request('DELETE', path, null, auth),
  patch:  (path, body, auth = false)   => request('PATCH',  path, body, auth),
};
