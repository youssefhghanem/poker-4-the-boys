import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Button } from '../../components/common/Button'
import './Home.css'

export function HomeScreen() {
  const navigate = useNavigate();

  return (
    <div className="home-screen">
      <motion.div
        className="home-content"
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
      >
        <h1 className="home-title">Poker 4 The Boys</h1>
        <p className="home-subtitle">Texas Hold'em</p>
        <div className="home-actions">
          <Button variant="primary" size="lg" fullWidth onClick={() => navigate('/create')}>
            Create Game
          </Button>
          <Button variant="secondary" size="lg" fullWidth onClick={() => navigate('/join')}>
            Join Game
          </Button>
        </div>
      </motion.div>
    </div>
  );
}
