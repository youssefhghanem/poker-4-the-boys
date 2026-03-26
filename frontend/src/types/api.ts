// All property names are camelCase — matches ASP.NET Core's default JSON serialization.

export interface CardDto {
  suit: string;    // "♣" | "♦" | "♥" | "♠"
  rank: string;    // "2"-"10" | "J" | "Q" | "K" | "A"
  display: string; // "K♠", "10♥", etc.
}

export interface PlayerInfoDto {
  id: string;
  name: string;
  emoji: string;
  chips: number;
  status: string; // "SittingOut" | "Active" | "Folded" | "AllIn" | "Eliminated"
  currentBet: number;
  totalBetThisHand: number;
  isHost: boolean;
  position: number;
  holeCards?: CardDto[];
}

export interface GameStateDto {
  stateVersion: number;
  phase: string;                // "Lobby" | "HandInProgress" | "HandComplete" | "GameComplete"
  currentRound?: string;        // "PreFlop" | "Flop" | "Turn" | "River"
  communityCards?: CardDto[];
  mainPot: number;
  sidePots?: SidePotDto[];
  dealerPosition: number;
  currentPlayerToActId?: string;
  players: PlayerInfoDto[];
  handNumber: number;
  smallBlind: number;
  minRaise: number;
  isShowdown: boolean;
  showdownHands?: Record<string, CardDto[]>;
}

export interface SidePotDto {
  amount: number;
  eligiblePlayerIds: string[];
}

export interface LobbyStateDto {
  roomCode: string;
  state: string; // "Lobby" | "HandInProgress" | "GameComplete"
  startingChips: number;
  smallBlind: number;
  players: LobbyPlayerDto[];
}

export interface LobbyPlayerDto {
  id: string;
  name: string;
  emoji: string;
  isHost: boolean;
}

export interface YourTurnDto {
  availableActions: string[]; // "Fold" | "Check" | "Call" | "Raise" | "AllIn"
  minRaise: number;
  maxRaise: number;
  amountToCall: number;
  timeRemaining: number;
}

export interface TurnTimerDto {
  playerId: string;
  timeRemaining: number;
  totalTime: number;
}

export interface GameEndDto {
  winnerPlayerId: string;
  winnerName: string;
  totalHandsPlayed: number;
  standings: PlayerStandingDto[];
}

export interface PlayerStandingDto {
  playerId: string;
  name: string;
  finalChips: number;
  position: number;
}

export interface RoomCreatedResult {
  roomCode: string;
  hostPlayerId: string;
  success: boolean;
  errorMessage?: string;
}

export interface JoinResult {
  playerId?: string;
  success: boolean;
  errorMessage?: string;
}

export interface ActionResultDto {
  success: boolean;
  errorMessage?: string;
}
