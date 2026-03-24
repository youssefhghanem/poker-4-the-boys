import './ChipDisplay.css'

interface ChipDisplayProps {
  amount: number;
  label?: string;
  size?: 'sm' | 'md';
}

export function ChipDisplay({ amount, label, size = 'md' }: ChipDisplayProps) {
  return (
    <div className={`chip-display chip-${size}`}>
      {label && <span className="chip-label">{label}</span>}
      <span className="chip-amount">{amount.toLocaleString()}</span>
    </div>
  );
}
