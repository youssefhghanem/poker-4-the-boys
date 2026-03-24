import type { ButtonHTMLAttributes } from 'react'
import { motion } from 'framer-motion'
import './Button.css'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'fold' | 'check' | 'call' | 'raise' | 'allIn';
  size?: 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  fullWidth?: boolean;
}

export function Button({
  children, variant = 'primary', size = 'md',
  isLoading = false, fullWidth = false, disabled, className = '',
  onClick, onFocus, onBlur, onMouseEnter, onMouseLeave, onKeyDown, onKeyUp,
  type, form, name, value, autoFocus, tabIndex, id, style, 'aria-label': ariaLabel,
}: ButtonProps) {
  return (
    <motion.button
      className={`btn btn-${variant} btn-${size} ${fullWidth ? 'btn-full' : ''} ${className}`}
      disabled={disabled || isLoading}
      whileHover={{ scale: disabled ? 1 : 1.02 }}
      whileTap={{ scale: disabled ? 1 : 0.98 }}
      onClick={onClick as React.MouseEventHandler<HTMLButtonElement>}
      onFocus={onFocus}
      onBlur={onBlur}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      onKeyDown={onKeyDown}
      onKeyUp={onKeyUp}
      type={type}
      form={form}
      name={name}
      value={value as string | number | readonly string[]}
      autoFocus={autoFocus}
      tabIndex={tabIndex}
      id={id}
      style={style}
      aria-label={ariaLabel}
    >
      {isLoading ? <span className="btn-spinner" /> : children}
    </motion.button>
  )
}
