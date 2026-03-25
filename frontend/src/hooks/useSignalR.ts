import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { signalRService } from '../services/signalRService'
import { useGameStore } from '../store/gameStore'
import type { GameStateDto, YourTurnDto, GameEndDto, TurnTimerDto } from '../types/api'

export function useSignalREvents() {
  const navigate = useNavigate();
  const {
    setGameState, setMyTurn, setTurnTimer,
    setGameEndResult, setLobbyState, addDisconnected, removeDisconnected, reset,
    setPrevHandChips, setHandResultVisible,
  } = useGameStore();

  const prevHandNumberRef = useRef<number>(-1);

  useEffect(() => {
    const unsubs: (() => void)[] = [];

    // Game state changed — fetch full state with hole cards, navigate on phase change
    unsubs.push(signalRService.on<GameStateDto>('OnGameStateChanged', async (broadcast) => {
      // Update with broadcast first (no hole cards)
      setGameState(broadcast);

      // Clear isMyTurn if it's no longer our turn (e.g. timeout auto-fold)
      const store = useGameStore.getState();
      if (store.isMyTurn && broadcast.currentPlayerToActId !== store.playerId) {
        setMyTurn(false, null);
      }

      // Fetch full state with our hole cards
      const fullState = await signalRService.getGameState();
      if (fullState) setGameState(fullState);

      // Winning animation: snapshot chips at hand start, set/clear result visibility
      if (broadcast.phase === 'HandInProgress') {
        if (broadcast.handNumber !== prevHandNumberRef.current) {
          prevHandNumberRef.current = broadcast.handNumber;
          setPrevHandChips(
            Object.fromEntries(broadcast.players.map((p) => [p.id, p.chips]))
          );
        }
        setHandResultVisible(false);
      } else if (broadcast.phase === 'HandComplete' && broadcast.isShowdown) {
        setHandResultVisible(true);
      }

      // State-driven navigation
      if (broadcast.phase === 'HandInProgress') {
        navigate('/game', { replace: true });
      }
    }));

    // Your turn
    unsubs.push(signalRService.on<YourTurnDto>('OnYourTurn', (info) => {
      setMyTurn(true, info);
      setTurnTimer(info.timeRemaining);
    }));

    // Turn timer countdown
    unsubs.push(signalRService.on<TurnTimerDto>('OnTurnTimer', (timer) => {
      setTurnTimer(timer.timeRemaining);
    }));

    // Game end
    unsubs.push(signalRService.on<GameEndDto>('OnGameEnd', (result) => {
      setGameEndResult(result);
      navigate('/game-end', { replace: true });
    }));

    // Game restarted (play again)
    unsubs.push(signalRService.on('OnGameRestarted', () => {
      setGameState(null);
      setGameEndResult(null);
      setMyTurn(false, null);
      navigate('/lobby', { replace: true });
    }));

    // Room closed
    unsubs.push(signalRService.on('OnRoomClosed', () => {
      reset();
      navigate('/', { replace: true });
    }));

    // Kicked
    unsubs.push(signalRService.on('OnKicked', () => {
      reset();
      navigate('/', { replace: true });
    }));

    // Player joined/left — refresh lobby
    unsubs.push(signalRService.on('OnPlayerJoined', () => {
      signalRService.getLobbyState().then((s) => s && setLobbyState(s));
    }));
    unsubs.push(signalRService.on('OnPlayerLeft', () => {
      signalRService.getLobbyState().then((s) => s && setLobbyState(s));
    }));

    // Settings changed — refresh lobby
    unsubs.push(signalRService.on('OnSettingsChanged', () => {
      signalRService.getLobbyState().then((s) => s && setLobbyState(s));
    }));

    // Disconnection tracking
    unsubs.push(signalRService.on<{ playerId: string }>('OnPlayerDisconnected', (data) => {
      addDisconnected(data.playerId);
    }));
    unsubs.push(signalRService.on<{ playerId: string }>('OnPlayerReconnected', (data) => {
      removeDisconnected(data.playerId);
    }));

    return () => unsubs.forEach((fn) => fn());
  }, [navigate, setGameState, setMyTurn, setTurnTimer, setGameEndResult, setLobbyState,
      addDisconnected, removeDisconnected, reset, setPrevHandChips, setHandResultVisible]);
}
