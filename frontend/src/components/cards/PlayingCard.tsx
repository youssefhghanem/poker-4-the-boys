import { motion } from 'framer-motion'
import type { CardDto } from '../../types/api'
import './PlayingCard.css'

interface PlayingCardProps {
  card?: CardDto | null;
  size?: 'sm' | 'md' | 'lg';
  delay?: number;
}

const redSuits = new Set(['♥', '♦']);

export function PlayingCard({ card, size = 'md', delay = 0 }: PlayingCardProps) {
  if (!card) {
    return <div className={`card card-back card-${size}`}><div className="card-pattern" /></div>;
  }

  const isRed = redSuits.has(card.suit);

  return (
    <motion.div
      className={`card card-front card-${size}`}
      initial={{ y: -40, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      transition={{ type: 'spring', stiffness: 300, damping: 20, delay }}
    >
      <div className="card-corner card-corner-top" style={{ color: isRed ? 'var(--red-suit)' : 'var(--black-suit)' }}>
        <span className="card-rank">{card.rank}</span>
        <span className="card-suit">{card.suit}</span>
      </div>
      <div className="card-center" style={{ color: isRed ? 'var(--red-suit)' : 'var(--black-suit)' }}>
        {card.suit}
      </div>
      <div className="card-corner card-corner-bottom" style={{ color: isRed ? 'var(--red-suit)' : 'var(--black-suit)' }}>
        <span className="card-rank">{card.rank}</span>
        <span className="card-suit">{card.suit}</span>
      </div>
    </motion.div>
  );
}
