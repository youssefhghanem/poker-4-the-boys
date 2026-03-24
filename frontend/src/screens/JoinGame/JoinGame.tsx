import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Button } from '../../components/common/Button'
import { Input } from '../../components/common/Input'
import { signalRService } from '../../services/signalRService'
import { useGameStore } from '../../store/gameStore'
import './JoinGame.css'

const EMOJIS = ['😀','😎','🤠','👽','🤖','👻','🦊','🐯','🦁','🐸','🐙','🦄'];

export function JoinGameScreen() {
  const navigate = useNavigate();
  const setPlayer = useGameStore((s) => s.setPlayer);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [emoji, setEmoji] = useState('😀');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleJoin = async () => {
    if (!code.trim()) { setError('Enter room code'); return; }
    if (!name.trim()) { setError('Enter your name'); return; }
    setLoading(true);
    setError('');
    try {
      await signalRService.connect();
      const result = await signalRService.joinRoom(code.trim().toUpperCase(), name.trim(), emoji);
      if (result.success && result.playerId) {
        setPlayer(result.playerId, code.trim().toUpperCase());
        navigate('/lobby');
      } else {
        setError(result.errorMessage || 'Failed to join room');
      }
    } catch {
      setError('Connection failed. Try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="join-screen">
      <motion.div className="join-content" initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
        <h2 className="screen-title">Join Game</h2>

        <div className="form-section">
          <Input label="Room Code" value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder="Enter 6-digit code" maxLength={6} autoComplete="off" />
        </div>

        <div className="form-section">
          <Input label="Your Name" value={name} onChange={(e) => setName(e.target.value)}
            placeholder="Enter your name" maxLength={12} />
        </div>

        <div className="form-section">
          <label className="form-label">Choose Avatar</label>
          <div className="emoji-grid">
            {EMOJIS.map((e) => (
              <button key={e} className={`emoji-btn ${emoji === e ? 'emoji-selected' : ''}`}
                onClick={() => setEmoji(e)}>{e}</button>
            ))}
          </div>
        </div>

        {error && <p className="error-msg">{error}</p>}

        <div className="form-actions">
          <Button variant="secondary" onClick={() => navigate('/')}>Back</Button>
          <Button variant="primary" onClick={handleJoin} isLoading={loading}>Join</Button>
        </div>
      </motion.div>
    </div>
  );
}
