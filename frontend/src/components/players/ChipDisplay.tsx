import './ChipDisplay.css'

interface ChipDisplayProps {
  amount: number;
  label?: string;
  size?: 'sm' | 'md';
  atStake?: number;
}

export function ChipDisplay({ amount, label, size = 'md', atStake }: ChipDisplayProps) {
  return (
    <div className={`chip-display-wrapper chip-wrapper-${size}`}>
      {atStake != null && atStake > 0 && (
        <span className="chip-stake">{atStake.toLocaleString()}</span>
      )}
      <div className={`chip-display chip-${size}`}>
        {label && <span className="chip-label">{label}</span>}
        <span className="chip-amount">{amount.toLocaleString()}</span>
      </div>
    </div>
  );
}
