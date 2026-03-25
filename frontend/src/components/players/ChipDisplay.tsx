import { useEffect, useRef } from 'react'
import { motion, useMotionValue, useTransform, animate } from 'framer-motion'
import type { AnimationPlaybackControls } from 'framer-motion'
import './ChipDisplay.css'

interface ChipDisplayProps {
  amount: number;
  label?: string;
  size?: 'sm' | 'md';
  atStake?: number;
  animateTo?: number;
  delta?: number;
}

export function ChipDisplay({ amount, label, size = 'md', atStake, animateTo, delta }: ChipDisplayProps) {
  const count = useMotionValue(amount);
  const rounded = useTransform(count, Math.round);
  const animationRef = useRef<AnimationPlaybackControls | null>(null);

  useEffect(() => {
    if (animateTo === undefined) {
      count.set(amount);
      return;
    }
    // Stop any in-progress animation before starting a new one
    animationRef.current?.stop();
    count.set(amount);
    animationRef.current = animate(count, animateTo, { duration: 1.8, ease: 'linear' });
    return () => animationRef.current?.stop();
  }, [animateTo, amount, count]);

  return (
    <div className={`chip-display-wrapper chip-wrapper-${size}`}>
      {atStake != null && atStake > 0 && (
        <span className="chip-stake">{atStake.toLocaleString()}</span>
      )}
      <div className={`chip-display chip-${size}`}>
        {label && <span className="chip-label">{label}</span>}
        {animateTo !== undefined
          ? <motion.span className="chip-amount">{rounded}</motion.span>
          : <span className="chip-amount">{amount.toLocaleString()}</span>
        }
      </div>
      {delta !== undefined && delta !== 0 && (
        <div className={`chip-delta ${delta > 0 ? 'chip-delta--win' : 'chip-delta--loss'}`}>
          {delta > 0 ? '+' : ''}{delta.toLocaleString()}
        </div>
      )}
    </div>
  );
}
