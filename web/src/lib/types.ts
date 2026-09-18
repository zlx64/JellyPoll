// DTOs mirroring the live Jellyfin 12.1 wire format (PascalCase — verified against a
// production 12.1 server; the camelCase assumption from docs/04 was wrong).

export type ItemType = 'Movie' | 'Episode' | 'Series';

export interface PollMeta {
  Id: string;
  Title: string;
  Status: 'open' | 'closed';
  CreatedAt: string;
  ClosedAt: string | null;
  CreatedById: string;
  CreatedByName: string;
  AllowEpisodes: boolean;
  AllowSeries: boolean;
  StateVersion: number;
}

export interface PollSummary {
  Id: string;
  Title: string;
  Status: 'open' | 'closed';
  SuggestionCount: number;
  VoterCount: number;
  CreatedAt: string;
  CreatedById: string;
  CreatedByName: string;
  ClosedAt: string | null;
  MyBallotCount: number;
}

export interface Suggestion {
  Id: string;
  ItemId: string;
  ItemType: ItemType;
  Name: string;
  Year: number | null;
  SuggestedById: string;
  SuggestedByName: string;
  SuggestedAt: string;
  ItemMissing: boolean;
}

export interface StandingEntry {
  Rank: number;
  SuggestionId: string;
  Points: number;
  FirstPlaceCount: number;
  VoterCount: number;
  ItemMissing: boolean;
  ItemId: string;
  ItemType: string;
  Name: string;
  Year: number | null;
}

export interface PollDetail {
  Poll: PollMeta;
  Suggestions: Suggestion[];
  MyBallot: string[];
  Standings: StandingEntry[];
  IsCreator: boolean;
  IsAdmin: boolean;
}

export interface Results {
  Standings: StandingEntry[];
  Gold: StandingEntry | null;
  Silver: StandingEntry | null;
  Bronze: StandingEntry | null;
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
