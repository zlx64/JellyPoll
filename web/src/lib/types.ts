// DTOs mirroring docs/04-API.md §3 (camelCase JSON).

export type ItemType = 'Movie' | 'Episode' | 'Series';

export interface PollMeta {
  id: string;
  title: string;
  status: 'open' | 'closed';
  createdAt: string;
  closedAt: string | null;
  createdById: string;
  createdByName: string;
  allowEpisodes: boolean;
  allowSeries: boolean;
  stateVersion: number;
}

export interface PollSummary {
  id: string;
  title: string;
  status: 'open' | 'closed';
  suggestionCount: number;
  voterCount: number;
  createdAt: string;
  createdById: string;
  createdByName: string;
  closedAt: string | null;
  myBallotCount: number;
}

export interface Suggestion {
  id: string;
  itemId: string;
  itemType: ItemType;
  name: string;
  year: number | null;
  suggestedById: string;
  suggestedByName: string;
  suggestedAt: string;
  itemMissing: boolean;
}

export interface StandingEntry {
  rank: number;
  suggestionId: string;
  points: number;
  firstPlaceCount: number;
  voterCount: number;
  itemMissing: boolean;
  itemId: string;
  itemType: string;
  name: string;
  year: number | null;
}

export interface PollDetail {
  poll: PollMeta;
  suggestions: Suggestion[];
  myBallot: string[];
  standings: StandingEntry[];
  isCreator: boolean;
  isAdmin: boolean;
}

export interface Results {
  standings: StandingEntry[];
  gold: StandingEntry | null;
  silver: StandingEntry | null;
  bronze: StandingEntry | null;
}

export interface JellyfinSearchItem {
  Id: string;
  Name: string;
  Type: string;
  ProductionYear?: number;
}

export interface JellyfinSearchResult {
  Items: JellyfinSearchItem[];
}
