# Winning Animation Design

**Date:** 2026-03-24
**Branch:** fix/winning-animation

## Summary

Add a 2.5-second pause between poker hands at showdown to display: (1) an inline winner announcement banner in the table center, and (2) animated chip counters on every player seat — counting up for winners, counting down for losers, with colored delta badges showing profit/loss amounts.

---

## Problem

Currently there is no pause between hands at showdown. The engine immediately starts the next hand after a hand ends, so chip updates are instant and winners are never announced. Players have no moment to see who won or how chips moved.

---

## Scope

### In scope
- Backend: 2.5s inter-hand delay after HandComplete broadcast; expose `Phase = "HandComplete"` in `BuildDto()`
- Frontend store: pre-hand chip snapshot + `handResultVisible` flag
- `ChipDisplay`: optional animated counter (count up/down) + delta badge
- `GameTable`: inline `WinnerBanner` component + wiring animated chip displays
- Split pot support (multiple winners)
- Mobile viewport (375px+)

### Out of scope
- Sound effects
- Chip-flying particle animations
- Per-street (non-showdown) hand result display
- Non-showdown wins (when everyone folds — `isShowdown` is false, no animation)

---

## Architecture

### Backend: `GameEngineWrapper.cs`

**Two changes are required, both in `HandleHandEnded`:**

#### 1. Expose `HandComplete` phase in `BuildDto()`

`ActiveGame.BuildDto()` currently hardcodes `Phase = "HandInProgress"` for every broadcast. This must be changed so that the `HandComplete` phase is surfaced to clients.

Add a boolean flag to `ActiveGame`:
```csharp
public bool IsHandComplete { get; set; } = false;
```

Modify `BuildDto()` to use it:
```csharp
Phase = IsHandComplete ? "HandComplete" : "HandInProgress"
```

In `HandleHandEnded`: set `activeGame.IsHandComplete = true` **before** calling `BroadcastState(...)`.

In `HandleHandStarted` (next hand): set `activeGame.IsHandComplete = false` **before** calling `BroadcastState(...)`.

#### 2. Insert `Thread.Sleep(2500)` at the correct position

The existing `HandleHandEnded` sequence (simplified) is:
```
1. BroadcastState(...)          ← broadcast HandComplete state
2. Clear HoleCards, ShowdownCards, IsShowdown, ChipsAtHandStart
3. (engine resumes next hand)
```

Insert `Thread.Sleep(2500)` **between steps 1 and 2**:
```
1. activeGame.IsHandComplete = true
2. BroadcastState(...)          ← clients receive HandComplete; animation starts
3. Thread.Sleep(2500)           ← NEW: pause engine thread for 2.5s
4. Clear HoleCards, ShowdownCards, IsShowdown, ChipsAtHandStart
5. activeGame.IsHandComplete = false  (handled in HandleHandStarted)
```

This preserves the live showdown state (hole cards, `IsShowdown = true`) for the full 2.5s window so any client calling `GetGameState()` during the animation receives the correct showdown data.

**Why `Thread.Sleep` and not `await Task.Delay`:** The `TcsPlayer` events are wired as synchronous `Action<>` delegates. `async void` + `await Task.Delay` would return immediately (fire-and-forget) and would not pause the engine thread.

**Final hand behaviour:** On the last hand of the game, `Thread.Sleep(2500)` delays `game.Start()` returning by 2.5 seconds before `OnGameEnd` fires. This is acceptable — the delay gives players time to see the final result before the game-end screen.

**`PlayAgain` race:** `PlayAgain` calls `ResetRoom` + `ClearGameState`, which resets the room and clears active game state. It does not interrupt or cancel the engine background thread's sleep. After the sleep completes, `HandleHandEnded` continues executing against stale state. This is safe because `ClearGameState` marks the active game as null — any subsequent broadcasts from the stale handler are no-ops (the SignalR group no longer exists for that room after reset). No additional handling is required.

---

### Store: `gameStore.ts`

Add to `initialState` (both fields must be included in `initialState` so `reset()` covers them):

```typescript
prevHandChips: null as Record<string, number> | null,
handResultVisible: false,
```

Add to `GameStore` interface:
```typescript
prevHandChips: Record<string, number> | null;
handResultVisible: boolean;
setPrevHandChips: (chips: Record<string, number> | null) => void;
setHandResultVisible: (visible: boolean) => void;
```

Add implementations:
```typescript
setPrevHandChips: (prevHandChips) => set({ prevHandChips }),
setHandResultVisible: (handResultVisible) => set({ handResultVisible }),
```

---

### SignalR hook: `useSignalR.ts`

Add a `useRef` inside `useSignalREvents` to track the previous hand number:
```typescript
const prevHandNumberRef = useRef<number>(-1);
```

In the `OnGameStateChanged` handler:

**When `broadcast.phase === 'HandInProgress'`:**
1. If `broadcast.handNumber !== prevHandNumberRef.current`:
   - Update `prevHandNumberRef.current = broadcast.handNumber`
   - Snapshot `prevHandChips`: `setPrevHandChips(Object.fromEntries(broadcast.players.map(p => [p.id, p.chips])))`
2. Clear `handResultVisible`: `setHandResultVisible(false)`

**When `broadcast.phase === 'HandComplete'` and `broadcast.isShowdown === true`:**
- Set `setHandResultVisible(true)`

No frontend timer is needed. `handResultVisible` is cleared naturally when the next `HandInProgress` arrives (step above).

Pull `setPrevHandChips` and `setHandResultVisible` from `useGameStore()` alongside existing destructured actions.

---

### Delta computation: `GameTable.tsx`

Pull `prevHandChips` and `handResultVisible` from the store.

When `handResultVisible` is true, compute deltas before rendering:

```typescript
const deltas: Record<string, number> = {};
if (handResultVisible && prevHandChips) {
  for (const p of gameState.players) {
    deltas[p.id] = p.chips - (prevHandChips[p.id] ?? p.chips);
  }
}
```

- `deltas[p.id] > 0` → winner, animate chip count up
- `deltas[p.id] < 0` → loser, animate chip count down
- `deltas[p.id] === 0` → no change (e.g. early fold), static display

**Winner identification for `WinnerBanner`:**

Primary: players where `deltas[p.id] > 0`.

Fallback (split pot where all deltas are zero due to equal chip distribution): use `Object.keys(gameState.showdownHands ?? {})` to identify players who went to showdown, and display "split pot" without a chip amount.

---

### `ChipDisplay.tsx` — extended

New optional props added to the existing interface:

```typescript
animateTo?: number;  // when set, animate chip count from amount → animateTo over 1.8s linear
delta?: number;      // when set, show +N (green) or -N (red) badge below the chip pill
```

**Framer Motion API used:** `useMotionValue` + standalone `animate` imported from `framer-motion` (not `useAnimate` hook, not `motion` variants). This is consistent with the existing `framer-motion` import in the project.

**When `animateTo` is provided:**

```typescript
const count = useMotionValue(amount);
const rounded = useTransform(count, Math.round);
const animationRef = useRef<AnimationPlaybackControls | null>(null);

useEffect(() => {
  animationRef.current?.stop();  // stop any in-progress animation
  count.set(amount);             // reset to start value
  animationRef.current = animate(count, animateTo, { duration: 1.8, ease: 'linear' });
  return () => animationRef.current?.stop();
}, [animateTo, amount]);
```

Display: replace `<span className="chip-amount">` with `<motion.span className="chip-amount">{rounded}</motion.span>`.

**When `animateTo` is NOT provided:** render `<span className="chip-amount">{amount.toLocaleString()}</span>` — existing static behaviour unchanged.

**Delta badge:** rendered as a sibling `<div>` below the chip pill when `delta !== undefined && delta !== 0`:

```tsx
{delta !== undefined && delta !== 0 && (
  <div className={`chip-delta ${delta > 0 ? 'chip-delta--win' : 'chip-delta--loss'}`}>
    {delta > 0 ? '+' : ''}{delta.toLocaleString()}
  </div>
)}
```

**`ChipDisplay` prop usage in `GameTable.tsx`:**

When `handResultVisible` is true, pass pre-hand chips as `amount` and post-hand chips as `animateTo`:

```tsx
<ChipDisplay
  amount={handResultVisible && prevHandChips ? (prevHandChips[p.id] ?? p.chips) : p.chips}
  animateTo={handResultVisible ? p.chips : undefined}
  delta={handResultVisible ? deltas[p.id] : undefined}
  size="sm"
/>
```

When `handResultVisible` is false, the `animateTo` and `delta` props are `undefined`, so `ChipDisplay` renders statically as before.

---

### `WinnerBanner` — inline in `GameTable.tsx`

Not a separate file. A `motion.div` rendered inside `.table-center` between the pot display and the community cards, wrapped in `<AnimatePresence>` (already imported).

**Rendering condition:** `handResultVisible && gameState.isShowdown`

**Content:**
- Single winner: `* Alex wins 1,200 *`
- Split pot (multiple delta > 0): `* Alex + Bob split 1,200 *` (amount = sum of positive deltas)
- Split pot fallback (all deltas zero): `* Split pot *`

**Animation:**
```tsx
<motion.div
  className="winner-banner"
  initial={{ scale: 0.8, opacity: 0 }}
  animate={{ scale: 1, opacity: 1 }}
  exit={{ scale: 0.8, opacity: 0 }}
  transition={{ type: 'spring', stiffness: 300, damping: 20 }}
>
  {bannerText}
</motion.div>
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
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.4);
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
  animation: delta-fade-in 0.3s ease 1.5s forwards;  /* delay until counter nears end */
}
.chip-delta--win  { color: #4caf50; }
.chip-delta--loss { color: #ef5350; }

@keyframes delta-fade-in {
  to { opacity: 1; }
}
```

The `1.5s` delay ensures the delta badge appears near the end of the 1.8s counter animation rather than immediately.

---

## Data Flow — Correct Timing

```
Hand N lifecycle:

  HandInProgress broadcast (handNumber=N, chips=pre-hand)
    → prevHandNumberRef.current !== N → snapshot prevHandChips={p.id: p.chips} for hand N
    → clear handResultVisible
    → setGameState(...)
    → navigate('/game')

  ... hand N plays out ...

  HandleHandEnded fires:
    → activeGame.IsHandComplete = true
    → BroadcastState: OnGameStateChanged { phase: HandComplete, isShowdown: true,
                                           players[chips=post-hand-N] }
    → Thread.Sleep(2500)
    → clear HoleCards, ShowdownCards, etc.

  Frontend receives HandComplete:
    → setHandResultVisible(true)
    → setGameState(HandComplete state with post-hand chips)

  GameTable renders with handResultVisible=true:
    → deltas = post-hand-N chips - prevHandChips (pre-hand-N chips)  ← CORRECT: uses hand-N snapshot
    → WinnerBanner shown
    → ChipDisplay animates from prevHandChips[p.id] → p.chips

  2.5s elapses...

  HandleHandStarted fires (hand N+1):
    → activeGame.IsHandComplete = false
    → BroadcastState: OnGameStateChanged { phase: HandInProgress, handNumber=N+1,
                                           chips=pre-hand-N+1 }

  Frontend receives HandInProgress (handNumber=N+1):
    → prevHandNumberRef.current !== N+1 → snapshot prevHandChips for hand N+1
    → setHandResultVisible(false)        ← animation clears
    → setGameState(...)
```

---

## Edge Cases

| Case | Behaviour |
|------|-----------|
| Non-showdown win (all fold) | `isShowdown` is false → `handResultVisible` stays false → no animation, normal next hand |
| Last hand of game | `Thread.Sleep(2500)` delays `game.Start()` return by 2.5s before `OnGameEnd` fires. Animation plays, then navigation to `/game-end` occurs. |
| `PlayAgain` during sleep | `ClearGameState` marks active game null; stale handler broadcasts after sleep are no-ops. Safe. |
| Reconnect during HandComplete | Client receives current `HandComplete` state → `handResultVisible` set → animation shows with correct deltas |
| Split pot, unequal distribution | Multiple players with `delta > 0` → all listed in banner; each gets count-up animation |
| Split pot, equal distribution (all deltas zero) | Fallback: use `showdownHands` keys to identify participants, show "Split pot" without amount |
| Player eliminated (chips → 0) | Treated as loser (delta < 0), counts down to 0, shown in red |
| `animateTo` prop changes mid-animation | Previous animation stopped via `animationRef.current?.stop()` before starting new one |
| `reset()` called (room closed/kicked) | `prevHandChips: null` and `handResultVisible: false` are in `initialState`, so reset clears both |

---

## Files Changed

| File | Change |
|------|--------|
| `src/PokerGame.Api/Services/GameEngineWrapper.cs` | Add `IsHandComplete` flag to `ActiveGame`; modify `BuildDto()` to return correct phase; add `Thread.Sleep(2500)` in `HandleHandEnded` after broadcast and before clear block; clear flag in `HandleHandStarted` |
| `frontend/src/store/gameStore.ts` | Add `prevHandChips`, `handResultVisible` to `initialState` and interface; add `setPrevHandChips`, `setHandResultVisible` actions |
| `frontend/src/hooks/useSignalR.ts` | Add `useRef<number>` for prev hand number; snapshot chips on new `HandInProgress`; set/clear `handResultVisible` on `HandComplete`/`HandInProgress` |
| `frontend/src/components/players/ChipDisplay.tsx` | Add `animateTo`, `delta` props; implement Framer Motion counter with `useMotionValue` + `animate` + `useRef` cleanup; add delta badge |
| `frontend/src/components/players/ChipDisplay.css` | Add `.chip-delta`, `.chip-delta--win`, `.chip-delta--loss`, `@keyframes delta-fade-in` |
| `frontend/src/screens/GameTable/GameTable.tsx` | Compute deltas; render `WinnerBanner` in `AnimatePresence` in `.table-center`; pass `amount`/`animateTo`/`delta` to `ChipDisplay` for each player |
| `frontend/src/screens/GameTable/GameTable.css` | Add `.winner-banner` styles with mobile breakpoint |
