# Winning Animation Design

**Date:** 2026-03-24
**Branch:** fix/winning-animation

## Summary

Add a 2.5-second pause between poker hands at showdown that displays: (1) an inline winner announcement banner in the table center, and (2) animated chip counters on every player seat — counting up for winners, counting down for losers, with colored delta badges showing profit/loss amounts.

---

## Problem

Currently there is no pause between hands at showdown. The engine immediately starts the next hand after a hand ends, so chip updates are instant and winners are never announced. Players have no moment to see who won or how chips moved.

---

## Scope

### In scope
- Backend: 2.5s inter-hand delay after HandComplete broadcast
- Frontend store: pre-hand chip snapshot + `handResultVisible` flag
- `ChipDisplay`: optional animated counter (count up/down) + delta badge
- `GameTable`: inline `WinnerBanner` component + wiring animated chip displays
- Split pot support (multiple winners)
- Mobile viewport (375px+)

### Out of scope
- Sound effects
- Chip-flying particle animations
- Per-street (non-showdown) hand result display
- Non-showdown wins (when everyone folds — no showdown, no animation)

---

## Architecture

### Backend: `GameEngineWrapper.cs`

In `HandleHandEnded`, after broadcasting `OnGameStateChanged` with the `HandComplete` phase state, add a synchronous 2.5-second sleep on the engine background thread:

```csharp
Thread.Sleep(2500);
```

This blocks the engine thread before it starts the next hand, guaranteeing `HandComplete` is the live phase for at least 2.5 seconds on all clients.

**Why synchronous `Thread.Sleep` and not `await Task.Delay`:** The `TcsPlayer` events are wired as synchronous `Action<>` delegates. Using `async void` with `await Task.Delay` in a synchronous delegate produces fire-and-forget behavior and would not actually pause the engine thread.

### Store: `gameStore.ts`

Two additions to `GameStore`:

| Field | Type | Purpose |
|-------|------|---------|
| `prevHandChips` | `Record<string, number> \| null` | Chips per player at start of current hand |
| `handResultVisible` | `boolean` | True during the 2.5s animation window |

Two new actions: `setPrevHandChips`, `setHandResultVisible`.

### SignalR hook: `useSignalR.ts`

Inside the `OnGameStateChanged` handler, add phase-specific logic:

1. **On `HandInProgress`**: if `handNumber` differs from the previous broadcast's handNumber, snapshot `prevHandChips` from the incoming player list. Also clear `handResultVisible`.
2. **On `HandComplete` with `isShowdown === true`**: set `handResultVisible = true`.

No frontend timer is needed — `handResultVisible` is cleared naturally when the next `HandInProgress` arrives.

### Delta computation: `GameTable.tsx`

When `handResultVisible` is true, compute per-player deltas before render:

```typescript
const deltas: Record<string, number> = {};
for (const p of gameState.players) {
  deltas[p.id] = p.chips - (prevHandChips?.[p.id] ?? p.chips);
}
```

- `delta > 0` → winner, animate chip count up
- `delta < 0` → loser, animate chip count down
- `delta === 0` → no change (early fold), static display

Winners for the banner = players where `delta > 0`.

---

## Components

### `ChipDisplay.tsx` — extended

New optional props added to the existing interface:

```typescript
animateTo?: number;  // animate chip count from amount → animateTo over 1.8s
delta?: number;      // show +N (green) or -N (red) badge below the chip pill
```

**When `animateTo` is provided:**
- `useMotionValue(amount)` initialised to the pre-hand chip value
- `animate(motionValue, animateTo, { duration: 1.8, ease: 'linear' })` drives the counter
- `useTransform(motionValue, Math.round)` feeds a `<motion.span>` for the display
- `controls.stop()` returned from `useEffect` cleanup

**When neither prop is provided:** existing static behaviour is completely unchanged.

**Delta badge:** rendered as a `<div className="chip-delta chip-delta--win|loss">` below the chip pill when `delta !== undefined && delta !== 0`.

### `WinnerBanner` — inline in `GameTable.tsx`

Not a separate file. A `motion.div` rendered inside `.table-center` between the pot display and community cards, wrapped in `<AnimatePresence>` (already imported).

**Content:**
- Single winner: `Alex wins 1,200`
- Split pot: `Alex + Bob split 1,200` (total of all positive deltas)
- Stars flanking the text: `* Alex wins 1,200 *`

**Animation:**
```
initial: { scale: 0.8, opacity: 0 }
animate: { scale: 1, opacity: 1 }
exit:    { scale: 0.8, opacity: 0 }
transition: { type: 'spring', stiffness: 300, damping: 20 }
```

---

## CSS

### `GameTable.css` additions

```css
.winner-banner {
  background: var(--cream);
  color: var(--felt-dark);
  font-weight: 700;
  font-size: 15px;
  padding: var(--sp-xs) var(--sp-lg);
  border-radius: var(--radius-full);
  text-align: center;
  box-shadow: 0 2px 12px rgba(0,0,0,0.4);
}

@media (max-width: 380px) {
  .winner-banner { font-size: 12px; }
}
```

### `ChipDisplay.css` additions

```css
.chip-delta {
  font-size: 10px;
  font-family: var(--font-mono);
  font-weight: 700;
  text-align: center;
  margin-top: 1px;
  opacity: 0;
  animation: delta-fade-in 0.3s ease forwards;
}
.chip-delta--win  { color: #4caf50; }
.chip-delta--loss { color: #ef5350; }

@keyframes delta-fade-in {
  to { opacity: 1; }
}
```

---

## Data Flow Diagram

```
Backend (engine thread)
  HandEnded fires
    → broadcast OnGameStateChanged { phase: HandComplete, isShowdown: true, players[chips=post-hand] }
    → Thread.Sleep(2500)                   ← new
    → next hand begins
    → broadcast OnGameStateChanged { phase: HandInProgress, handNumber: N+1 }

Frontend (useSignalR)
  receive HandComplete
    → setHandResultVisible(true)           ← new
    → setGameState(...)
  receive HandInProgress (next hand)
    → setHandResultVisible(false)          ← new
    → setPrevHandChips(snapshot)           ← new
    → setGameState(...)
    → navigate('/game')

GameTable render (handResultVisible=true)
  → compute deltas
  → render WinnerBanner                   ← new
  → pass animateTo+delta into ChipDisplay  ← new (each seat)
```

---

## Edge Cases

| Case | Behaviour |
|------|-----------|
| Non-showdown win (all fold) | `isShowdown` is false → `handResultVisible` stays false → no animation, normal next hand |
| Last hand of game | `OnGameEnd` fires and navigates to `/game-end` before next `HandInProgress` — animation plays for however long it has before navigation |
| Reconnect during HandComplete | Client receives current `HandComplete` state → `handResultVisible` set → animation shows with correct deltas |
| Split pot (2+ winners) | Multiple players have `delta > 0` → all listed in banner; each gets count-up animation |
| Player eliminated (chips → 0) | Treated as a loser (delta < 0), counts down to 0, shown in red |

---

## Files Changed

| File | Change |
|------|--------|
| `src/PokerGame.Api/Services/GameEngineWrapper.cs` | Add `Thread.Sleep(2500)` in `HandleHandEnded` after broadcasting |
| `frontend/src/store/gameStore.ts` | Add `prevHandChips`, `handResultVisible`, `setPrevHandChips`, `setHandResultVisible` |
| `frontend/src/hooks/useSignalR.ts` | Snapshot chips on `HandInProgress`, set/clear `handResultVisible` on `HandComplete` |
| `frontend/src/components/players/ChipDisplay.tsx` | Add `animateTo`, `delta` props with Framer Motion counter + delta badge |
| `frontend/src/components/players/ChipDisplay.css` | Add `.chip-delta` styles |
| `frontend/src/screens/GameTable/GameTable.tsx` | Compute deltas, render `WinnerBanner`, wire animated props to `ChipDisplay` |
| `frontend/src/screens/GameTable/GameTable.css` | Add `.winner-banner` styles |
