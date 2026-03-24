import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Button } from '../../components/common/Button'
import { Avatar } from '../../components/players/Avatar'
import { signalRService } from '../../services/signalRService'
import { useGameStore } from '../../store/gameStore'
import './Lobby.css'

export function LobbyScreen() {
  const navigate = useNavigate();
  const { playerId, roomCode, lobbyState, setLobbyState } = useGameStore();

  // Fetch lobby state on mount; updates come via useSignalREvents in App
  useEffect(() => {
    signalRService.getLobbyState().then((s) => s && setLobbyState(s));
  }, [setLobbyState]);

  if (!lobbyState || !playerId) {
    return <div className="lobby-loading">Loading lobby...</div>;
  }

  const isHost = lobbyState.players.some((p) => p.id === playerId && p.isHost);
  const canStart = isHost && lobbyState.players.length >= 2;

  const handleStart = async () => {
    const result = await signalRService.startGame();
    if (!result.success) alert(result.errorMessage || 'Failed to start');
  };

  const handleLeave = async () => {
    await signalRService.leaveRoom();
    useGameStore.getState().reset();
    navigate('/');
  };

  return (
    <div className="lobby-screen">
      <motion.div className="lobby-content" initial={{ opacity: 0 }} animate={{ opacity: 1 }}>
        <div className="lobby-header">
          <h2 className="screen-title">Lobby</h2>
          <div className="room-code-box">
            <span className="room-code-label">Room Code</span>
            <span className="room-code-value">{roomCode}</span>
          </div>
        </div>

        <div className="lobby-players">
          <h3 className="lobby-players-title">Players ({lobbyState.players.length}/6)</h3>
          {lobbyState.players.map((p) => (
            <div key={p.id} className="lobby-player-row">
              <Avatar emoji={p.emoji} name={p.name} size="sm" />
              {p.isHost && <span className="host-badge">HOST</span>}
            </div>
          ))}
        </div>

        <div className="lobby-settings">
          <span className="settings-item">Blinds: {lobbyState.smallBlind}/{lobbyState.smallBlind * 2}</span>
          <span className="settings-item">Chips: {lobbyState.startingChips.toLocaleString()}</span>
        </div>

        <div className="lobby-actions">
          <Button variant="secondary" onClick={handleLeave}>Leave</Button>
          {isHost ? (
            <Button variant="primary" onClick={handleStart} disabled={!canStart}>
              Start Game
            </Button>
          ) : (
            <p className="waiting-text">Waiting for host...</p>
          )}
        </div>
      </motion.div>
    </div>
  );
}
