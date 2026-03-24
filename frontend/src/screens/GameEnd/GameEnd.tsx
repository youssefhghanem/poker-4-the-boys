import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Button } from '../../components/common/Button'
import { useGameStore } from '../../store/gameStore'
import { signalRService } from '../../services/signalRService'
import './GameEnd.css'

export function GameEndScreen() {
  const navigate = useNavigate();
  const { gameEndResult, playerId, gameState } = useGameStore();

  if (!gameEndResult) {
    return <div className="end-loading">Loading results...</div>;
  }

  const isHost = gameState?.players.some((p) => p.id === playerId && p.isHost) ?? false;
  const isWinner = gameEndResult.winnerPlayerId === playerId;

  const handlePlayAgain = async () => {
    const result = await signalRService.playAgain();
    if (!result.success) alert(result.errorMessage || 'Failed');
    // OnGameRestarted event will navigate to /lobby
  };

  const handleHome = async () => {
    await signalRService.leaveRoom();
    useGameStore.getState().reset();
    navigate('/');
  };

  return (
    <div className="end-screen">
      <motion.div className="end-content"
        initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }}>

        <h2 className="end-title">{isWinner ? 'You Win!' : 'Game Over'}</h2>

        <div className="winner-section">
          <span className="winner-label">Winner</span>
          <span className="winner-name">{gameEndResult.winnerName}</span>
        </div>

        <div className="standings">
          <h3 className="standings-title">Final Standings</h3>
          {gameEndResult.standings.map((s, i) => (
            <motion.div key={s.playerId}
              className={`standing-row ${s.playerId === playerId ? 'standing-you' : ''}`}
              initial={{ opacity: 0, x: -20 }} animate={{ opacity: 1, x: 0 }}
              transition={{ delay: i * 0.1 }}>
              <span className="standing-pos">#{s.position}</span>
              <span className="standing-name">{s.name}</span>
              <span className="standing-chips">{s.finalChips.toLocaleString()}</span>
            </motion.div>
          ))}
        </div>

        <p className="hands-played">{gameEndResult.totalHandsPlayed} hands played</p>

        <div className="end-actions">
          {isHost && (
            <Button variant="primary" onClick={handlePlayAgain}>Play Again</Button>
          )}
          <Button variant="secondary" onClick={handleHome}>Home</Button>
        </div>
      </motion.div>
    </div>
  );
}
