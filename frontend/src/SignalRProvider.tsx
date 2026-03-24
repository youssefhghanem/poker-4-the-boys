import type { ReactNode } from 'react'
import { useSignalREvents } from './hooks/useSignalR'

export function SignalRProvider({ children }: { children: ReactNode }) {
  useSignalREvents();
  return <>{children}</>;
}
