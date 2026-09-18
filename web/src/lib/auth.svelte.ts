// Same-origin auth (doc 05 §5): reuse jellyfin-web's token from localStorage,
// validate it, fall back to a login form.

interface JellyfinCredentials {
  Servers?: Array<{
    Id?: string;
    AccessToken?: string;
    UserId?: string;
  }>;
}

export const auth = $state({
  status: 'checking' as 'checking' | 'logged-in' | 'logged-out',
  token: null as string | null,
  userId: null as string | null,
  serverId: null as string | null,
  deviceId: getOrCreateDeviceId()
});

function getOrCreateDeviceId(): string {
  let id = localStorage.getItem('jellypoll-device-id');
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem('jellypoll-device-id', id);
  }
  return id;
}

export function authHeader(token = auth.token): string {
  return (
    'MediaBrowser Client="JellyPoll Web", Device="Web", DeviceId="' +
    auth.deviceId + '", Version="1.0.0", Token="' + (token ?? '') + '"'
  );
}

/** Boots the auth flow: try stored jellyfin-web credentials, else show login. */
export async function initAuth(): Promise<void> {
  const stored = tryReadStoredCredentials();
  if (stored) {
    const ok = await validateToken(stored.accessToken);
    if (ok) {
      auth.token = stored.accessToken;
      auth.userId = stored.userId;
      auth.status = 'logged-in';
      return;
    }
  }
  auth.status = 'logged-out';
}

function tryReadStoredCredentials(): { accessToken: string; userId: string | null } | null {
  try {
    const raw = localStorage.getItem('jellyfin_credentials');
    if (!raw) return null;
    const parsed = JSON.parse(raw) as JellyfinCredentials;
    const server = parsed?.Servers?.[0];
    if (!server?.AccessToken) return null;
    return { accessToken: server.AccessToken, userId: server.UserId ?? null };
  } catch {
    return null;
  }
}

async function validateToken(token: string): Promise<boolean> {
  try {
    const res = await fetch('/System/Info', { headers: { Authorization: authHeader(token) } });
    return res.ok;
  } catch {
    return false;
  }
}

/** Login fallback (doc 05 §5 step 3). Stores session in sessionStorage only. */
export async function login(username: string, password: string): Promise<void> {
  const res = await fetch('/Users/AuthenticateByName', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: authHeader(null)
    },
    body: JSON.stringify({ Username: username, Pw: password })
  });
  if (!res.ok) {
    throw new Error('Login failed (' + res.status + '). Check your username and password.');
  }
  const data = (await res.json()) as { AccessToken: string; User: { Id: string; SessionInfo?: { ServerId?: string } } };
  auth.token = data.AccessToken;
  auth.userId = data.User.Id;
  auth.serverId = data.User.SessionInfo?.ServerId ?? null;
  sessionStorage.setItem('jellypoll-session', JSON.stringify({ token: data.AccessToken, userId: data.User.Id }));
  auth.status = 'logged-in';
}

/** Restore a login-form session after page reload. */
export function restoreSession(): void {
  try {
    const raw = sessionStorage.getItem('jellypoll-session');
    if (!raw) return;
    const parsed = JSON.parse(raw) as { token: string; userId: string };
    if (parsed.token) {
      auth.token = parsed.token;
      auth.userId = parsed.userId;
    }
  } catch {
    /* ignore */
  }
}

export function logout(): void {
  auth.token = null;
  auth.userId = null;
  sessionStorage.removeItem('jellypoll-session');
  auth.status = 'logged-out';
}
