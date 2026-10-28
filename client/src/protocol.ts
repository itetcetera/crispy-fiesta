// The server's messages (docs/PROTOCOL.md).

export const SPECIES = ["Mechtron", "Gollumer", "Packer", "Bonzoid", "Spheroid", "Flapper", "Leggite", "Humanoid"] as const;
export type Species = (typeof SPECIES)[number];

export type Phase = "Lobby" | "Playing" | "Over";

export interface MemberView {
  id: string;
  name: string;
  race: Species;
  connected: boolean;
  seat: number;
}

export interface LobbyView {
  code: string;
  phase: Phase;
  hostId: string;
  members: MemberView[];
}

export interface JoinResult {
  token: string;
  playerId: string;
  room: LobbyView;
}

/** A game event: its type in `t`, then its fields (see the records in server/Mule.Engine). */
export interface GameEvent {
  t: string;
  [field: string]: unknown;
}

export interface FrameMessage {
  tick: number;
  events: GameEvent[];
  state: Record<string, number[]>;
  full: boolean;
  /** The sound chip's registers each frame they changed: [tick, AUDF1, AUDC1 ... AUDF4, AUDC4, AUDCTL]. */
  pokey?: number[][];
}
