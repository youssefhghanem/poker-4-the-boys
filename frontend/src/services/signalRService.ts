import * as signalR from '@microsoft/signalr';
import type {
  GameStateDto, RoomCreatedResult, JoinResult, ActionResultDto,
  LobbyStateDto,
} from '../types/api';

type Callback<T> = (data: T) => void;

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private pendingHandlers: Array<{ event: string; callback: Callback<unknown> }> = [];

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;

    // Stop any existing connection before creating a new one
    if (this.connection) {
      await this.connection.stop();
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/gamehub')
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Apply any handlers that were registered before connect()
    for (const { event, callback } of this.pendingHandlers) {
      this.connection.on(event, callback);
    }

    await this.connection.start();
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  // Returns unsubscribe function for cleanup.
  // If called before connect(), buffers the handler and applies it when connect() runs.
  on<T>(event: string, callback: Callback<T>): () => void {
    const entry = { event, callback: callback as Callback<unknown> };
    this.pendingHandlers.push(entry);
    this.connection?.on(event, callback);
    return () => {
      this.pendingHandlers = this.pendingHandlers.filter((e) => e !== entry);
      this.connection?.off(event, callback);
    };
  }

  // Hub invocations
  createRoom(hostName: string, emoji: string, startingChips: number): Promise<RoomCreatedResult> {
    return this.connection!.invoke('CreateRoom', hostName, emoji, startingChips);
  }

  joinRoom(roomCode: string, playerName: string, emoji: string): Promise<JoinResult> {
    return this.connection!.invoke('JoinRoom', roomCode, playerName, emoji);
  }

  leaveRoom(): Promise<void> {
    return this.connection!.invoke('LeaveRoom');
  }

  kickPlayer(playerId: string): Promise<boolean> {
    return this.connection!.invoke('KickPlayer', playerId);
  }

  startGame(): Promise<ActionResultDto> {
    return this.connection!.invoke('StartGame');
  }

  submitAction(actionType: string, amount?: number): Promise<ActionResultDto> {
    return this.connection!.invoke('SubmitAction', actionType, amount ?? null);
  }

  getGameState(): Promise<GameStateDto | null> {
    return this.connection!.invoke('GetGameState');
  }

  getLobbyState(): Promise<LobbyStateDto | null> {
    return this.connection!.invoke('GetLobbyState');
  }

  updateSettings(smallBlind?: number, startingChips?: number): Promise<ActionResultDto> {
    return this.connection!.invoke('UpdateSettings', smallBlind ?? null, startingChips ?? null);
  }

  playAgain(): Promise<ActionResultDto> {
    return this.connection!.invoke('PlayAgain');
  }

  reconnect(playerId: string): Promise<GameStateDto | null> {
    return this.connection!.invoke('Reconnect', playerId);
  }
}

export const signalRService = new SignalRService();
