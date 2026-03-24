# Winning Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 2.5-second pause between poker hands at showdown that displays a winner banner and animates chip counts up/down per player.

**Architecture:** The backend blocks its engine thread for 2.5s after broadcasting a new `HandComplete` phase, giving all clients a guaranteed animation window. The frontend snapshots pre-hand chip counts, computes deltas at `HandComplete`, drives Framer Motion counters per player seat, and shows an inline winner banner in the table center.

**Tech Stack:** C# / ASP.NET Core (backend), React 18 + TypeScript, Zustand, Framer Motion v12 (frontend)

---

## File Map

| File | Role |
|------|------|
| `src/PokerGame.Api/Services/GameEngineWrapper.cs` | Add `IsHandComplete` flag to `ActiveGame`; wire into `BuildDto()`; set/clear in hand lifecycle; add `Thread.Sleep(2500)` |
| `src/Tests/PokerGame.Api.Tests/GameEngineWrapperTests.cs` | New test: `HandComplete` phase broadcast after showdown |
| `frontend/src/store/gameStore.ts` | Add `prevHandChips`, `handResultVisible`, setters |
| `frontend/src/hooks/useSignalR.ts` | Snapshot chips on `HandInProgress`; set/clear `handResultVisible` |
| `frontend/src/components/players/ChipDisplay.tsx` | Add `animateTo`/`delta` props; Framer Motion counter; delta badge |
| `frontend/src/components/players/ChipDisplay.css` | `.chip-delta` styles |
| `frontend/src/screens/GameTable/GameTable.tsx` | Deltas; WinnerBanner; animated ChipDisplay props |
| `frontend/src/screens/GameTable/GameTable.css` | `.winner-banner` styles |

---

## Task 1: Backend — expose `HandComplete` phase and add inter-hand delay

**Files:**
- Modify: `src/PokerGame.Api/Services/GameEngineWrapper.cs`
- Test: `src/Tests/PokerGame.Api.Tests/GameEngineWrapperTests.cs`

> **Implementation note:** `HandleHandEnded` and `HandleHandStarted` each fire once **per player** per hand (N times for an N-player game). All state mutations here are idempotent, and the broadcast+sleep block is guarded by `if (!gameState.IsHandComplete)` so it executes exactly once regardless of how many players are in the game.

---

- [ ] **Step 1: Write the failing test**

Add this test to `GameEngineWrapperTests.cs`. It polls `GetGameState()` deterministically to find each player's turn before submitting AllIn, avoiding timing races.

```csharp
[Fact]
public void HandleHandEnded_ShowdownHand_BroadcastsHandCompletePhase()
{
    // Arrange
    var roomManager = new RoomManager();
    var wrapper = new GameEngineWrapper();
    var (roomCode, _) = roomManager.CreateRoom("Alice", "😀", 200);
    roomManager.JoinRoom(roomCode, "Bob", "🎯");
    var room = roomManager.GetRoom(roomCode)!;

    var broadcastPhases = new System.Collections.Concurrent.ConcurrentBag<string>();
    wrapper.GameStateChanged += (_, dto) => broadcastPhases.Add(dto.Phase);

    // Act
    wrapper.StartGameAsync(room);

    // Poll until first player needs to act (max 2s)
    GameStateDto? state = null;
    for (int i = 0; i < 20 && state?.CurrentPlayerToActId == null; i++)
    {
        System.Threading.Thread.Sleep(100);
        state = wrapper.GetGameState(roomCode);
    }

    Assert.NotNull(state?.CurrentPlayerToActId);
    wrapper.SubmitAction(state!.CurrentPlayerToActId!, "AllIn", null);

    // Poll until second player needs to act (max 2s)
    state = null;
    for (int i = 0; i < 20 && state?.CurrentPlayerToActId == null; i++)
    {
        System.Threading.Thread.Sleep(100);
        state = wrapper.GetGameState(roomCode);
    }

    if (state?.CurrentPlayerToActId != null)
    {
        wrapper.SubmitAction(state.CurrentPlayerToActId, "AllIn", null);
    }

    // Wait for hand to complete and HandComplete phase to be broadcast
    System.Threading.Thread.Sleep(500);

    // Assert
    Assert.Contains("HandComplete", broadcastPhases);
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test /Users/youssefghanem/Projects/Github/poker-fix-anim/src/Tests/PokerGame.Api.Tests/ --filter "FullyQualifiedName~HandleHandEnded_ShowdownHand_BroadcastsHandCompletePhase" -v
```

Expected: **FAIL** — `broadcastPhases` never contains `"HandComplete"` because `BuildDto()` always returns `"HandInProgress"`.

---

- [ ] **Step 3: Add `IsHandComplete` property to `ActiveGame`**

In `GameEngineWrapper.cs`, inside `private class ActiveGame`, add the property immediately after `IsShowdown` (line ~522):

```csharp
public bool IsShowdown { get; set; }

public bool IsHandComplete { get; set; }    // ← add this line
```

- [ ] **Step 4: Update `BuildDto()` to use the flag**

In `BuildDto()` inside `ActiveGame` (line ~532), change:

```csharp
Phase = "HandInProgress",
```

to:

```csharp
Phase = this.IsHandComplete ? "HandComplete" : "HandInProgress",
```

- [ ] **Step 5: Update `HandleHandEnded` — replace the existing `BroadcastState` call**

The existing `HandleHandEnded` has this sequence near lines 449–458:

```csharp
gameState.CurrentPlayerToActId = null;
gameState.IsShowdown = gameState.ShowdownCards != null && gameState.ShowdownCards.Count > 0;

this.BroadcastState(room.RoomCode, gameState);          // ← REMOVE this line

// Clear hand state for next hand
gameState.HoleCards.Clear();
gameState.ShowdownCards = null;
gameState.IsShowdown = false;
gameState.ChipsAtHandStart.Clear();
```

**Remove** the existing `BroadcastState` call and replace the entire block from `gameState.CurrentPlayerToActId` through the end of the method with:

```csharp
gameState.CurrentPlayerToActId = null;
gameState.IsShowdown = gameState.ShowdownCards != null && gameState.ShowdownCards.Count > 0;

// Guard: HandleHandEnded fires once per player — only broadcast and sleep on first invocation.
if (!gameState.IsHandComplete)
{
    gameState.IsHandComplete = true;
    this.BroadcastState(room.RoomCode, gameState);      // ← HandComplete broadcast
    System.Threading.Thread.Sleep(2500);                // ← pause engine thread; clients animate
}

// Clear hand state for next hand (runs once per player; all assignments are idempotent).
gameState.HoleCards.Clear();
gameState.ShowdownCards = null;
gameState.IsShowdown = false;
gameState.ChipsAtHandStart.Clear();
```

- [ ] **Step 6: Clear the flag in `HandleHandStarted`**

In `HandleHandStarted` (line ~383), add `gameState.IsHandComplete = false;` as the **first line of the method body**.

This also fires once per player per hand start. Setting it to `false` multiple times is safe (idempotent). It ensures that by the time the first `BroadcastState` call in `HandleHandStarted` runs, `IsHandComplete` is already cleared.

```csharp
private void HandleHandStarted(Room room, ActiveGame gameState, string sessionId, IStartHandContext context)
{
    gameState.IsHandComplete = false;       // ← add this line first
    gameState.HandNumber = context.HandNumber;
    // ... rest of method unchanged
```

- [ ] **Step 7: Run test to verify it passes**

```bash
dotnet test /Users/youssefghanem/Projects/Github/poker-fix-anim/src/Tests/PokerGame.Api.Tests/ --filter "FullyQualifiedName~HandleHandEnded_ShowdownHand_BroadcastsHandCompletePhase" -v
```

Expected: **PASS** (the test polls up to 2s per player for a turn, then waits 500ms after the hand — the `Thread.Sleep(2500)` inside `HandleHandEnded` blocks the engine thread but the broadcast happens before the sleep, so `broadcastPhases` is populated before the assertion).

- [ ] **Step 8: Run full backend test suite**

```bash
dotnet test /Users/youssefghanem/Projects/Github/poker-fix-anim/src/Tests/PokerGame.Api.Tests/ -v
```

Expected: **33 tests pass** (32 existing + 1 new). Ignore `TexasHoldem.Logic.Tests` failures — pre-existing ARM64 issue unrelated to this change.

- [ ] **Step 9: Commit**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim
git add src/PokerGame.Api/Services/GameEngineWrapper.cs src/Tests/PokerGame.Api.Tests/GameEngineWrapperTests.cs
git commit -m "feat: broadcast HandComplete phase with 2.5s inter-hand delay"
```

---

## Task 2: Store — add `prevHandChips` and `handResultVisible`

**Files:**
- Modify: `frontend/src/store/gameStore.ts`

---

- [ ] **Step 1: Add fields to interface, initialState, and implementations**

In `GameStore` interface (after `disconnectedPlayerIds`), add:

```typescript
prevHandChips: Record<string, number> | null;
handResultVisible: boolean;
setPrevHandChips: (chips: Record<string, number> | null) => void;
setHandResultVisible: (visible: boolean) => void;
```

In `initialState` (after `disconnectedPlayerIds`), add:

```typescript
prevHandChips: null as Record<string, number> | null,
handResultVisible: false,
```

In the `create<GameStore>((set) => ({ ... }))` body (after `removeDisconnected`), add:

```typescript
setPrevHandChips: (prevHandChips) => set({ prevHandChips }),
setHandResultVisible: (handResultVisible) => set({ handResultVisible }),
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim
git add frontend/src/store/gameStore.ts
git commit -m "feat: add prevHandChips and handResultVisible to game store"
```

---

## Task 3: SignalR hook — snapshot chips and set `handResultVisible`

**Files:**
- Modify: `frontend/src/hooks/useSignalR.ts`

---

- [ ] **Step 1: Add `useRef` to the React import**

The current first line is:

```typescript
import { useEffect } from 'react'
```

Change to:

```typescript
import { useEffect, useRef } from 'react'
```

- [ ] **Step 2: Add the hand-number ref inside `useSignalREvents`**

Inside `useSignalREvents()`, before the `useEffect` call, add:

```typescript
const prevHandNumberRef = useRef<number>(-1);
```

- [ ] **Step 3: Destructure new store actions**

Change the existing `useGameStore()` destructure from:

```typescript
const {
  setGameState, setMyTurn, setTurnTimer,
  setGameEndResult, setLobbyState, addDisconnected, removeDisconnected, reset,
} = useGameStore();
```

to:

```typescript
const {
  setGameState, setMyTurn, setTurnTimer,
  setGameEndResult, setLobbyState, addDisconnected, removeDisconnected, reset,
  setPrevHandChips, setHandResultVisible,
} = useGameStore();
```

- [ ] **Step 4: Add phase-specific logic in the `OnGameStateChanged` handler**

Inside the `OnGameStateChanged` handler, **before** the existing `// State-driven navigation` comment, add:

```typescript
  // Winning animation: snapshot chips at hand start, set/clear result visibility
  if (broadcast.phase === 'HandInProgress') {
    if (broadcast.handNumber !== prevHandNumberRef.current) {
      prevHandNumberRef.current = broadcast.handNumber;
      setPrevHandChips(
        Object.fromEntries(broadcast.players.map((p) => [p.id, p.chips]))
      );
    }
    setHandResultVisible(false);
  } else if (broadcast.phase === 'HandComplete' && broadcast.isShowdown) {
    setHandResultVisible(true);
  }
```

- [ ] **Step 5: Add new actions to the `useEffect` dependency array**

The closing `useEffect` dependency array (last line of the hook) currently is:

```typescript
  }, [navigate, setGameState, setMyTurn, setTurnTimer, setGameEndResult, setLobbyState, addDisconnected, removeDisconnected, reset]);
```

Change to:

```typescript
  }, [navigate, setGameState, setMyTurn, setTurnTimer, setGameEndResult, setLobbyState,
      addDisconnected, removeDisconnected, reset, setPrevHandChips, setHandResultVisible]);
```

- [ ] **Step 6: Verify TypeScript compiles**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 7: Commit**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim
git add frontend/src/hooks/useSignalR.ts
git commit -m "feat: snapshot pre-hand chips and expose handResultVisible in SignalR hook"
```

---

## Task 4: ChipDisplay — animated counter and delta badge

**Files:**
- Modify: `frontend/src/components/players/ChipDisplay.tsx`
- Modify: `frontend/src/components/players/ChipDisplay.css`

---

- [ ] **Step 1: Replace `ChipDisplay.tsx` with the animated version**

Full file replacement. The existing static behaviour is preserved when `animateTo` is `undefined` — `motion.span` is only rendered when animating.

```typescript
import { useEffect, useRef } from 'react'
import { motion, useMotionValue, useTransform, animate } from 'framer-motion'
import type { AnimationPlaybackControls } from 'framer-motion'
import './ChipDisplay.css'

interface ChipDisplayProps {
  amount: number;
  label?: string;
  size?: 'sm' | 'md';
  animateTo?: number;
  delta?: number;
}

export function ChipDisplay({ amount, label, size = 'md', animateTo, delta }: ChipDisplayProps) {
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
    <div className={`chip-display chip-${size}`}>
      {label && <span className="chip-label">{label}</span>}
      {animateTo !== undefined
        ? <motion.span className="chip-amount">{rounded}</motion.span>
        : <span className="chip-amount">{amount.toLocaleString()}</span>
      }
      {delta !== undefined && delta !== 0 && (
        <div className={`chip-delta ${delta > 0 ? 'chip-delta--win' : 'chip-delta--loss'}`}>
          {delta > 0 ? '+' : ''}{delta.toLocaleString()}
        </div>
      )}
    </div>
  );
}
```

> **Framer Motion v12 note:** `useMotionValue`, `useTransform`, `animate` (imperative function), and `AnimationPlaybackControls` are all top-level exports of `framer-motion` v12. No sub-path imports required.

- [ ] **Step 2: Append delta badge styles to `ChipDisplay.css`**

```css
.chip-delta {
  font-size: 10px;
  font-family: var(--font-mono);
  font-weight: 700;
  text-align: center;
  margin-top: 1px;
  opacity: 0;
  animation: delta-fade-in 0.3s ease 1.5s forwards;
}

.chip-delta--win  { color: #4caf50; }
.chip-delta--loss { color: #ef5350; }

@keyframes delta-fade-in {
  to { opacity: 1; }
}
```

The `1.5s` delay means the badge appears near the end of the 1.8s counter animation.

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim
git add frontend/src/components/players/ChipDisplay.tsx frontend/src/components/players/ChipDisplay.css
git commit -m "feat: add animated counter and delta badge to ChipDisplay"
```

---

## Task 5: GameTable — winner banner and animated chip wiring

**Files:**
- Modify: `frontend/src/screens/GameTable/GameTable.tsx`
- Modify: `frontend/src/screens/GameTable/GameTable.css`

---

- [ ] **Step 1: Pull new store fields in `GameTable.tsx`**

Change the existing destructure (line 12) from:

```typescript
const { gameState, isMyTurn, myTurnInfo, turnTimer, playerId, disconnectedPlayerIds } = useGameStore();
```

to:

```typescript
const {
  gameState, isMyTurn, myTurnInfo, turnTimer, playerId,
  disconnectedPlayerIds, prevHandChips, handResultVisible,
} = useGameStore();
```

- [ ] **Step 2: Compute deltas and winner banner text**

After the `me` / `others` derivations (~line 25), add:

```typescript
// Per-player chip deltas during the HandComplete animation window
const deltas: Record<string, number> = {};
if (handResultVisible && prevHandChips) {
  for (const p of gameState.players) {
    deltas[p.id] = p.chips - (prevHandChips[p.id] ?? p.chips);
  }
}

// Winner banner text (only used when handResultVisible && gameState.isShowdown)
let winnerBannerText = '';
if (handResultVisible && gameState.isShowdown) {
  const winners = gameState.players.filter((p) => (deltas[p.id] ?? 0) > 0);
  if (winners.length > 0) {
    const totalWon = winners.reduce((sum, p) => sum + deltas[p.id], 0);
    const names = winners.map((p) => p.name).join(' + ');
    winnerBannerText = winners.length === 1
      ? `* ${names} wins ${totalWon.toLocaleString()} *`
      : `* ${names} split ${totalWon.toLocaleString()} *`;
  } else {
    // Fallback: all deltas zero (perfectly equal split) — derive names from showdownHands keys
    const showdownIds = Object.keys(gameState.showdownHands ?? {});
    const showdownNames = gameState.players
      .filter((p) => showdownIds.includes(p.id))
      .map((p) => p.name)
      .join(' + ');
    winnerBannerText = showdownNames ? `* ${showdownNames} split pot *` : '* Split pot *';
  }
}
```

- [ ] **Step 3: Add `WinnerBanner` inside `.table-center` JSX**

Inside the `.table-center` div, **between** the `pot-display` div and the `community-cards` div, add:

```tsx
<AnimatePresence>
  {handResultVisible && gameState.isShowdown && (
    <motion.div
      className="winner-banner"
      initial={{ scale: 0.8, opacity: 0 }}
      animate={{ scale: 1, opacity: 1 }}
      exit={{ scale: 0.8, opacity: 0 }}
      transition={{ type: 'spring', stiffness: 300, damping: 20 }}
    >
      {winnerBannerText}
    </motion.div>
  )}
</AnimatePresence>
```

`AnimatePresence` is already imported in `GameTable.tsx` — no new import needed.

- [ ] **Step 4: Wire animated props into opponent `ChipDisplay` instances**

Replace the existing opponent `ChipDisplay` (~line 50):

```tsx
<ChipDisplay amount={p.chips} size="sm" />
```

with:

```tsx
<ChipDisplay
  amount={handResultVisible && prevHandChips ? (prevHandChips[p.id] ?? p.chips) : p.chips}
  animateTo={handResultVisible ? p.chips : undefined}
  delta={handResultVisible && (deltas[p.id] ?? 0) !== 0 ? deltas[p.id] : undefined}
  size="sm"
/>
```

- [ ] **Step 5: Wire animated props into the local player `ChipDisplay`**

Replace the existing local player `ChipDisplay` (~line 96):

```tsx
<ChipDisplay amount={me?.chips || 0} label="You" size="md" />
```

with:

```tsx
<ChipDisplay
  amount={handResultVisible && prevHandChips && me
    ? (prevHandChips[me.id] ?? me.chips ?? 0)
    : (me?.chips ?? 0)}
  animateTo={handResultVisible && me ? me.chips : undefined}
  delta={handResultVisible && me && (deltas[me.id] ?? 0) !== 0 ? deltas[me.id] : undefined}
  label="You"
  size="md"
/>
```

- [ ] **Step 6: Add `.winner-banner` styles to `GameTable.css`**

```css
/* Winner banner */
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

- [ ] **Step 7: Verify TypeScript compiles**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 8: Verify production build succeeds**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npm run build
```

Expected: build completes with no errors.

- [ ] **Step 9: Commit**

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim
git add frontend/src/screens/GameTable/GameTable.tsx frontend/src/screens/GameTable/GameTable.css
git commit -m "feat: add WinnerBanner and animated chip counters to GameTable"
```

---

## Task 6: Integration verification

**Manual test checklist — run both backend and frontend together.**

- [ ] **Step 1: Start the backend**

```bash
dotnet run --project /Users/youssefghanem/Projects/Github/poker-fix-anim/src/PokerGame.Api/
```

Expected: server starts on `http://localhost:5000`.

- [ ] **Step 2: Start the frontend dev server**

In a separate terminal:

```bash
cd /Users/youssefghanem/Projects/Github/poker-fix-anim/frontend && npm run dev
```

Expected: Vite dev server starts on `http://localhost:3000`.

- [ ] **Step 3: Play a hand to showdown**

Open two browser tabs. Tab 1: create game. Tab 2: join game. Both players check/call to river without folding, then let the hand resolve at showdown.

Expected sequence:
1. Community cards appear normally during the hand.
2. At showdown: winner banner slides into the table center with player name and chip amount.
3. Both players' chip counts animate over ~1.8s (count up for winner, count down for loser).
4. Delta badges (`+N` green / `-N` red) fade in at ~1.5s.
5. After 2.5s total, next hand starts automatically and animation clears.

- [ ] **Step 4: Test split pot scenario**

Both players go all-in with equal stacks → both chip counts reach 0, then split. Expected: banner shows both names and displays "split X" or "split pot".

- [ ] **Step 5: Test mobile viewport**

Open DevTools → responsive mode → set viewport to 375px width. Expected: winner banner font is 12px (not 15px), chip displays are readable.

- [ ] **Step 6: Verify non-showdown hand has no animation**

One player folds before showdown → expected: next hand starts immediately with no banner and no counter animation.

- [ ] **Step 7: Run final backend test suite**

```bash
dotnet test /Users/youssefghanem/Projects/Github/poker-fix-anim/src/Tests/PokerGame.Api.Tests/ -v
```

Expected: 33 tests pass.
