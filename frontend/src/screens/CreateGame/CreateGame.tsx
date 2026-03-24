import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Button } from '../../components/common/Button'
import { Input } from '../../components/common/Input'
import { signalRService } from '../../services/signalRService'
import { useGameStore } from '../../store/gameStore'
import './CreateGame.css'

const EMOJIS = ['😀','😎','🤠','👽','🤖','👻','🦊','🐯','🦁','🐸','🐙','🦄'];

export function CreateGameScreen() {
  const navigate = useNavigate();
  const setPlayer = useGameStore((s) => s.setPlayer);
  const [name, setName] = useState('');
  const [emoji, setEmoji] = useState('😀');
  const [chips, setChips] = useState(1000);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleCreate = async () => {
    if (!name.trim()) { setError('Enter your name'); return; }
    setLoading(true);
    setError('');
    try {
      await signalRService.connect();
      const result = await signalRService.createRoom(name.trim(), emoji, chips);
      if (result.success) {
        setPlayer(result.hostPlayerId, result.roomCode);
        navigate('/lobby');
      } else {
        setError(result.errorMessage || 'Failed to create room');
      }
    } catch {
      setError('Connection failed. Try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="create-screen">
      <motion.div className="create-content" initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
        <h2 className="screen-title">Create Game</h2>

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

        <div className="form-section">
          <label className="form-label">Starting Chips: {chips.toLocaleString()}</label>
          <input type="range" className="chips-slider" min={500} max={5000} step={100}
            value={chips} onChange={(e) => setChips(Number(e.target.value))} />
        </div>

        {error && <p className="error-msg">{error}</p>}

        <div className="form-actions">
          <Button variant="secondary" onClick={() => navigate('/')}>Back</Button>
          <Button variant="primary" onClick={handleCreate} isLoading={loading}>Create</Button>
        </div>
      </motion.div>
    </div>
  );
}
