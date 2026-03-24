import './Avatar.css'

interface AvatarProps {
  emoji: string;
  name: string;
  size?: 'sm' | 'md';
  isActive?: boolean;
  isFolded?: boolean;
  isDisconnected?: boolean;
}

export function Avatar({ emoji, name, size = 'md', isActive, isFolded, isDisconnected }: AvatarProps) {
  const classes = [
    'avatar', `avatar-${size}`,
    isActive && 'avatar-active',
    isFolded && 'avatar-folded',
    isDisconnected && 'avatar-disconnected',
  ].filter(Boolean).join(' ');

  return (
    <div className={classes}>
      <div className="avatar-emoji">{emoji}</div>
      <div className="avatar-name">{name}</div>
    </div>
  );
}
