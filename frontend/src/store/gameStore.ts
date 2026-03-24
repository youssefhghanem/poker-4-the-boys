import { create } from 'zustand';
import type { GameStateDto, YourTurnDto, GameEndDto, LobbyStateDto } from '../types/api';

interface GameStore {
  // Connection
  isConnected: boolean;
  playerId: string | null;
  roomCode: string | null;

  // Lobby
  lobbyState: LobbyStateDto | null;

  // Game
  gameState: GameStateDto | null;
  isMyTurn: boolean;
  myTurnInfo: YourTurnDto | null;
  turnTimer: number | null;
  gameEndResult: GameEndDto | null;
  disconnectedPlayerIds: Set<string>;
  prevHandChips: Record<string, number> | null;
  handResultVisible: boolean;

  // Actions
  setConnected: (connected: boolean) => void;
  setPlayer: (playerId: string, roomCode: string) => void;
  setLobbyState: (state: LobbyStateDto | null) => void;
  setGameState: (state: GameStateDto | null) => void;
  setMyTurn: (isTurn: boolean, info?: YourTurnDto | null) => void;
  setTurnTimer: (seconds: number | null) => void;
  setGameEndResult: (result: GameEndDto | null) => void;
  addDisconnected: (playerId: string) => void;
  removeDisconnected: (playerId: string) => void;
  setPrevHandChips: (chips: Record<string, number> | null) => void;
  setHandResultVisible: (visible: boolean) => void;
  reset: () => void;
}

const initialState = {
  isConnected: false,
  playerId: null as string | null,
  roomCode: null as string | null,
  lobbyState: null as LobbyStateDto | null,
  gameState: null as GameStateDto | null,
  isMyTurn: false,
  myTurnInfo: null as YourTurnDto | null,
  turnTimer: null as number | null,
  gameEndResult: null as GameEndDto | null,
  disconnectedPlayerIds: new Set<string>(),
  prevHandChips: null as Record<string, number> | null,
  handResultVisible: false,
};

export const useGameStore = create<GameStore>((set) => ({
  ...initialState,

  setConnected: (connected) => set({ isConnected: connected }),
  setPlayer: (playerId, roomCode) => set({ playerId, roomCode }),
  setLobbyState: (lobbyState) => set({ lobbyState }),
  setGameState: (gameState) => set({ gameState }),
  setMyTurn: (isMyTurn, info = null) => set({ isMyTurn, myTurnInfo: info }),
  setTurnTimer: (turnTimer) => set({ turnTimer }),
  setGameEndResult: (gameEndResult) => set({ gameEndResult }),
  addDisconnected: (playerId) => set((s) => {
    const next = new Set(s.disconnectedPlayerIds);
    next.add(playerId);
    return { disconnectedPlayerIds: next };
  }),
  removeDisconnected: (playerId) => set((s) => {
    const next = new Set(s.disconnectedPlayerIds);
    next.delete(playerId);
    return { disconnectedPlayerIds: next };
  }),
  setPrevHandChips: (prevHandChips) => set({ prevHandChips }),
  setHandResultVisible: (handResultVisible) => set({ handResultVisible }),
  reset: () => set({ ...initialState, disconnectedPlayerIds: new Set<string>() }),
}));
