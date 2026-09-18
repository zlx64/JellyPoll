// HTTP helpers: /JellyPoll API + Jellyfin-native endpoints, all with the
// Authorization: MediaBrowser header (doc 04 §1, doc 05 §5).

import { auth, authHeader, logout } from './auth.svelte';
import type { JellyfinSearchResult, PollDetail, PollSummary, Results, Suggestion } from './types';

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string
  ) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit): Promise<T> {
  const headers: Record<string, string> = {
    Authorization: authHeader(),
    'Content-Type': 'application/json',
    ...((init.headers as Record<string, string>) ?? {})
  };
  const res = await fetch(path, { ...init, headers });

  if (res.status === 401) {
    logout();
    throw new ApiError(401, 'unauthenticated', 'Session expired.');
  }

  if (!res.ok) {
    let code = 'error';
    let message = res.statusText;
    try {
      const body = (await res.json()) as { error?: string; message?: string };
      code = body.error ?? code;
      message = body.message ?? message;
    } catch {
      /* non-JSON error */
    }
    throw new ApiError(res.status, code, message);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return (await res.json()) as T;
}

// ---------- JellyPoll API ----------

export const jellypoll = {
  listPolls: () => request<{ Polls: PollSummary[]; IsAdmin: boolean }>('/JellyPoll/Polls', { method: 'GET' }),

  deleteAllClosedPolls: () => request<{ DeletedCount: number }>('/JellyPoll/Polls/Closed', { method: 'DELETE' }),

  createPoll: (title: string, allowEpisodes?: boolean, allowSeries?: boolean) =>
    request<PollDetail>('/JellyPoll/Polls', {
      method: 'POST',
      body: JSON.stringify({ title, allowEpisodes, allowSeries })
    }),

  getPoll: (id: string) => request<PollDetail>(`/JellyPoll/Polls/${id}`, { method: 'GET' }),

  addSuggestion: (pollId: string, itemId: string) =>
    request<{ Suggestion: Suggestion }>(`/JellyPoll/Polls/${pollId}/Suggestions`, {
      method: 'POST',
      body: JSON.stringify({ itemId })
    }),

  removeSuggestion: (pollId: string, suggestionId: string) =>
    request<void>(`/JellyPoll/Polls/${pollId}/Suggestions/${suggestionId}`, { method: 'DELETE' }),

  saveBallot: (pollId: string, suggestionIds: string[]) =>
    request<{ StateVersion: number; SavedCount: number }>(`/JellyPoll/Polls/${pollId}/Ballot`, {
      method: 'PUT',
      body: JSON.stringify({ suggestionIds })
    }),

  closePoll: (pollId: string) =>
    request<PollDetail>(`/JellyPoll/Polls/${pollId}/Close`, { method: 'POST' }),

  reopenPoll: (pollId: string) =>
    request<PollDetail>(`/JellyPoll/Polls/${pollId}/Reopen`, { method: 'POST' }),

  deletePoll: (pollId: string) =>
    request<void>(`/JellyPoll/Polls/${pollId}`, { method: 'DELETE' }),

  results: (pollId: string) => request<Results>(`/JellyPoll/Polls/${pollId}/Results`, { method: 'GET' }),

  /** 204 when unchanged; 200 with new stateVersion when changed. */
  state: (pollId: string, version: number) =>
    request<{ StateVersion: number }>(`/JellyPoll/Polls/${pollId}/State?v=${version}`, { method: 'GET' }),

  installMenuLink: () =>
    request<{ installed: boolean }>('/JellyPoll/Admin/MenuLink', {
      method: 'POST',
      body: JSON.stringify({ install: true })
    })
};

// ---------- Jellyfin-native endpoints (user token enforces permissions) ----------

export const jellyfin = {
  searchItems: async (
    searchTerm: string,
    includeItemTypes: string,
    limit = 24
  ): Promise<JellyfinSearchResult> => {
    const params = new URLSearchParams({
      searchTerm,
      recursive: 'true',
      limit: String(limit),
      includeItemTypes,
      fields: 'ProductionYear,PrimaryImageAspectRatio',
      sortBy: 'SortName'
    });
    return request<JellyfinSearchResult>(`/Items?${params}`, { method: 'GET' });
  },

  me: () => request<{ Id: string; Name: string }>('/Users/Me', { method: 'GET' })
};

// ---------- poster images (blob fetch — img tags cannot send headers) ----------

const imageCache = new Map<string, Promise<string | null>>();

export function posterUrl(itemId: string): Promise<string | null> {
  let entry = imageCache.get(itemId);
  if (!entry) {
    entry = (async () => {
      try {
        const res = await fetch(`/Items/${itemId}/Images/Primary`, {
          headers: { Authorization: authHeader() }
        });
        if (!res.ok) return null;
        const blob = await res.blob();
        return URL.createObjectURL(blob);
      } catch {
        return null;
      }
    })();
    imageCache.set(itemId, entry);
  }
  return entry;
}
