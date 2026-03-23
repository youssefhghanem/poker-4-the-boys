# Phase 2: Real-Time Gameplay & State Synchronization

> **Status:** In Progress (2026-03-23)
> **Prerequisite:** Phase 1 complete (ASP.NET Core backend with SignalR hub, RoomManager, GameEngineWrapper)

---

## Review Amendments (2026-03-23)

The original Phase 2 plan was reviewed against the actual codebase before implementation. **7 issues** were found and corrected:

### Showstoppers Fixed
1. **Hole cards already stored but not exposed** -- Plan said "never populated" but `HandleHandStarted` already stores hole cards in `ActiveGame.HoleCards`. The real gap is `BuildDto()` doesn't include player info or hole cards, and `GetGameState` doesn't filter by requesting player.
2. **`IEndHandContext` has no Winners/TotalPot** -- Plan referenced `context.Winners` and `context.TotalPot` which don't exist. The interface only exposes `ShowdownCards`. Winners must be inferred from chip deltas between hand start and hand end.
3. **Event wiring in Hub constructor would leak handlers** -- SignalR hubs are transient (new instance per call). Plan proposed subscribing to events in constructor. Kept the correct Phase 1 pattern: wire events in `Program.cs` via `IHubContext<GameHub>`.
4. **Timer race condition with TcsPlayer timeout** -- Plan only cancelled timer on `SubmitAction` but not when TcsPlayer's 30s timeout fires auto-fold. Added cancellation on both paths.
5. **Reconnect method can't look up by new ConnectionId** -- New connection gets a new ConnectionId with no player mapping. Changed `Reconnect()` to `Reconnect(playerId)` so client identifies itself.
6. **GameEnded event signature mismatch** -- Current signature is `Action<string, string, int>`. Changing to `Action<string, GameEndDto>` requires updating `Program.cs` wiring.
7. **Tests were incomplete stubs** -- Rewrote with proper assertions that exercise the actual methods.

### Tasks Restructured
Original 9 tasks collapsed to 6 implementation tasks (merged overlapping concerns, removed the broken "wire events in Hub" task):

| Original Tasks | New Task | Reason |
|---|---|---|
| 1 (DTOs) + 4 (Wire Events) | Task 1 | Events are wired in Program.cs, not Hub; merged with DTO work |
| 2 (Timer) | Task 2 | Standalone, fixed race condition |
| 3 (Showdown) + 6 (Play Again) | Task 3 | Game end and play-again are tightly coupled |
| 7 (Host Controls) + 8 (Validation) | Task 4 | Both are small Hub methods |
| 5 (Reconnection) | Task 5 | Standalone, fixed Reconnect signature |
| 9 (Tests) | Task 6 | Depends on all above |

---

## Overview

Phase 2 completes the real-time multiplayer gameplay infrastructure by:
- Exposing hole cards per-player in game state
- Pushing turn timer countdown to clients
- Broadcasting showdown results and game end standings
- Adding action validation, host settings, and play-again flow
- Implementing reconnection with grace period

---

## Current State Assessment (Post-Phase 1)

### What Works
- Room management with 6-char codes, in-memory storage, thread-safe
- SignalR Hub: CreateRoom, JoinRoom, LeaveRoom, KickPlayer, StartGame, SubmitAction, GetGameState, GetLobbyState
- GameEngineWrapper: TcsPlayer integration, game loop on background thread
- Events wired in Program.cs: GameStateChanged, PlayerTurnRequested, GameEnded
- Hole cards stored in `ActiveGame.HoleCards` during HandStarted

### What's Missing
1. `BuildDto()` doesn't include players or hole cards -- clients get no player info from game state broadcasts
2. `GetGameState` doesn't accept a requesting player ID -- can't filter hole cards per-player
3. No turn timer countdown push to clients
4. No showdown result DTO -- `HandleHandEnded` stores ShowdownCards but doesn't broadcast them distinctly
5. `GameEnded` event sends `(roomCode, winnerId, handsPlayed)` -- no standings or game end DTO
6. No reconnection grace period -- immediate auto-fold on disconnect
7. No play-again flow, no host settings update, no action validation

---

## Task 1: Enhanced DTOs, Hole Card Exposure & Event Wiring

**Goal:** Add new DTO types, fix `BuildDto` to include player info and hole cards, update `GetGameState` to filter per-player, and wire new events in `Program.cs`.

**Files to modify:**
- `src/PokerGame.Api/DTOs/GameEndDto.cs` (new)
- `src/PokerGame.Api/DTOs/GameStateDto.cs`
- `src/PokerGame.Api/Services/IGameEngineWrapper.cs`
- `src/PokerGame.Api/Services/GameEngineWrapper.cs`
- `src/PokerGame.Api/Hubs/GameHub.cs`

### Step 1.1: Create GameEndDto.cs

```csharp
// src/PokerGame.Api/DTOs/GameEndDto.cs
namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;

    public class GameEndDto
    {
        public string WinnerPlayerId { get; set; } = string.Empty;
        public string WinnerName { get; set; } = string.Empty;
        public int TotalHandsPlayed { get; set; }
        public List<PlayerStandingDto> Standings { get; set; } = new List<PlayerStandingDto>();
    }

    public class PlayerStandingDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int FinalChips { get; set; }
        public int Position { get; set; }
    }

    public class TurnTimerDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public int TimeRemaining { get; set; }
        public int TotalTime { get; set; } = 30;
    }
}
```

### Step 1.2: Add IsShowdown to GameStateDto

```csharp
// Add to GameStateDto:
public bool IsShowdown { get; set; }
public Dictionary<string, List<CardDto>>? ShowdownHands { get; set; }
```

### Step 1.3: Update IGameEngineWrapper

Change `GetGameState` signature to accept requesting player ID. Change `GameEnded` to use `GameEndDto`. Add `TurnTimerTick` event. Add `ClearGameState` and `ValidateAction`.

```csharp
public interface IGameEngineWrapper : IDisposable
{
    Task StartGameAsync(Room room);
    bool SubmitAction(string playerId, string actionType, int? amount);
    GameStateDto? GetGameState(string roomCode, string? requestingPlayerId = null);
    void ClearGameState(string roomCode);
    (bool Success, string? Error) ValidateAction(string playerId, string actionType, int? amount);

    event Action<string, GameStateDto>? GameStateChanged;
    event Action<string, string, YourTurnDto>? PlayerTurnRequested;
    event Action<string, GameEndDto>? GameEnded;
    event Action<string, TurnTimerDto>? TurnTimerTick;
}
```

### Step 1.4: Update GameEngineWrapper

1. Change `GameEnded` event signature from `Action<string, string, int>` to `Action<string, GameEndDto>`
2. Update `GetGameState` to accept `requestingPlayerId`, populate players from room, and filter hole cards
3. Add `Room` reference to `ActiveGame` so `BuildDto` can include player info
4. Update `StartGameAsync` game-end handler to build `GameEndDto` with standings

### Step 1.5: Update GameHub.GetGameState

Pass `playerId` to `GetGameState` so hole cards are filtered per-player.

### Step 1.6: Update Program.cs event wiring

Update `GameEnded` handler for new `GameEndDto` signature. Add `TurnTimerTick` handler.

- [ ] **Verify**: `dotnet build src/TexasHoldem.sln` -- 0 errors
- [ ] **Verify**: `dotnet test src/TexasHoldem.sln` -- all pass

---

## Task 2: Turn Timer Countdown Push

**Goal:** Push per-second timer countdown to the active player. Cancel on both action submission and TcsPlayer timeout.

**Files to modify:**
- `src/PokerGame.Api/Services/GameEngineWrapper.cs`

### Step 2.1: Add timer tracking to ActiveGame

```csharp
// In ActiveGame class:
public ConcurrentDictionary<string, CancellationTokenSource> TimerCts { get; }
    = new ConcurrentDictionary<string, CancellationTokenSource>();
```

### Step 2.2: Start countdown in HandleTurnRequested

After invoking `PlayerTurnRequested`, start a `Task.Run` loop that ticks every second and invokes `TurnTimerTick`. Store the CTS in `TimerCts[sessionId]`.

### Step 2.3: Cancel timer on SubmitAction

Before processing the action, look up and cancel/dispose the player's CTS.

### Step 2.4: Cancel timer on TcsPlayer timeout

The tricky part: when TcsPlayer's internal timeout fires, `GetTurn` returns `Fold()` and the engine continues. The next event that fires is either another `TurnRequested` (next player) or `HandEnded`. Add timer cancellation at the START of `HandleTurnRequested` (cancel any existing timer for any player) and at the start of `HandleHandEnded`.

- [ ] **Verify**: `dotnet build src/TexasHoldem.sln` -- 0 errors

---

## Task 3: Showdown Results & Game End with Play Again

**Goal:** Broadcast showdown results after each hand, send proper game-end standings, and allow host to restart.

**Files to modify:**
- `src/PokerGame.Api/Services/GameEngineWrapper.cs`
- `src/PokerGame.Api/Services/IRoomManager.cs`
- `src/PokerGame.Api/Services/RoomManager.cs`
- `src/PokerGame.Api/Hubs/GameHub.cs`

### Step 3.1: Enhance HandleHandEnded

`IEndHandContext.ShowdownCards` is the only data available. To determine winners, snapshot each player's chips at hand start and compare at hand end. The player(s) whose chips increased won the hand.

```
In HandleHandStarted: gameState.ChipsAtHandStart[sessionId] = context.MoneyLeft
In HandleHandEnded:
  - Build ShowdownHands from context.ShowdownCards
  - Compare current chips (from room.Players) to ChipsAtHandStart to find winners
  - Set IsShowdown = true on the broadcasted state
  - Broadcast state with showdown data
  - Then clear for next hand
```

### Step 3.2: Add ResetRoom to IRoomManager and RoomManager

```csharp
bool ResetRoom(string roomCode);
```

Resets room state to Lobby, resets all player chips to StartingChips, clears CurrentBet and Status.

### Step 3.3: Add PlayAgain to GameHub

Host-only method. Checks room state is GameComplete, calls `roomManager.ResetRoom()` and `gameEngine.ClearGameState()`, broadcasts `OnGameRestarted`.

- [ ] **Verify**: `dotnet build src/TexasHoldem.sln` -- 0 errors
- [ ] **Verify**: `dotnet test src/TexasHoldem.sln` -- all pass

---

## Task 4: Action Validation & Host Controls

**Goal:** Validate player actions server-side, allow host to update room settings.

**Files to modify:**
- `src/PokerGame.Api/Services/GameEngineWrapper.cs`
- `src/PokerGame.Api/Hubs/GameHub.cs`

### Step 4.1: Add ValidateAction to GameEngineWrapper

Check: player is in an active game, it's their turn, raise amount is valid (> 0 when action is Raise).

### Step 4.2: Update SubmitAction in GameHub

Call `ValidateAction` before `SubmitAction`. Return error if invalid.

### Step 4.3: Add UpdateSettings to GameHub

Host-only, lobby-only. Accepts optional `smallBlind` and `startingChips`. Updates room and all player chips. Broadcasts `OnSettingsChanged`.

- [ ] **Verify**: `dotnet build src/TexasHoldem.sln` -- 0 errors

---

## Task 5: Reconnection Handling

**Goal:** Grace period for disconnected players instead of immediate auto-fold.

**Files to modify:**
- `src/PokerGame.Api/Services/PlayerSession.cs`
- `src/PokerGame.Api/Hubs/GameHub.cs`

### Step 5.1: Add disconnection state to PlayerSession

```csharp
public bool IsDisconnected { get; set; }
public DateTime? DisconnectedAt { get; set; }
```

### Step 5.2: Update OnDisconnectedAsync

During a game: mark player as disconnected, broadcast `OnPlayerDisconnected` with grace period (60s), start background timer. On expiry, if still disconnected, auto-fold.

In lobby: remove player normally.

### Step 5.3: Add Reconnect(playerId) to GameHub

**Key fix:** Takes `playerId` as parameter (client stores its ID from CreateRoom/JoinRoom). Looks up room by playerId (not connectionId), re-adds to SignalR group, updates connection mapping, clears disconnected state, broadcasts `OnPlayerReconnected`. Returns current game state.

```csharp
public async Task<GameStateDto?> Reconnect(string playerId)
```

- [ ] **Verify**: `dotnet build src/TexasHoldem.sln` -- 0 errors

---

## Task 6: Comprehensive Tests

**Goal:** Unit tests covering all Phase 2 additions.

**Files to create/modify:**
- `src/Tests/PokerGame.Api.Tests/GameEngineWrapperTests.cs` (new)
- `src/Tests/PokerGame.Api.Tests/RoomManagerTests.cs` (add tests)

### Tests to write:

**GameEngineWrapper:**
- `SubmitAction_InvalidPlayerId_ReturnsFalse`
- `GetGameState_NoActiveGame_ReturnsNull`
- `ClearGameState_RemovesActiveGame`
- `ValidateAction_NotPlayersTurn_ReturnsFalse`
- `ValidateAction_PlayerNotInGame_ReturnsFalse`
- `ValidateAction_RaiseWithNoAmount_ReturnsFalse`

**RoomManager (additions):**
- `ResetRoom_ResetsStateToLobby`
- `ResetRoom_ResetsPlayerChips`
- `ResetRoom_NonexistentRoom_ReturnsFalse`

- [ ] **Verify**: `dotnet test src/TexasHoldem.sln` -- all pass

---

## Dependencies

```
Task 1 (DTOs + Events) ──> Task 2 (Timer)
                       ──> Task 3 (Showdown + Play Again)
                       ──> Task 4 (Validation + Host Controls)
Task 3 ──────────────────> Task 5 (Reconnection) [needs game state structure]
Tasks 1-5 ─────────────> Task 6 (Tests)
```

Tasks 2, 3, 4 can proceed in parallel after Task 1. Task 5 depends on Task 3 (game state structure). Task 6 is last.

---

## API Contract (Phase 2 Additions)

### Client -> Server (new methods)

| Method | Parameters | Returns | Notes |
|---|---|---|---|
| `UpdateSettings` | smallBlind?, startingChips? | ActionResultDto | Host only, lobby only |
| `PlayAgain` | -- | ActionResultDto | Host only, after game ends |
| `Reconnect` | playerId | GameStateDto? | Within 60s grace period |

### Server -> Client (new events)

| Event | Payload | Notes |
|---|---|---|
| `OnTurnTimer` | TurnTimerDto | Per-second countdown tick |
| `OnPlayerDisconnected` | { PlayerId, GracePeriodSeconds } | Player lost connection |
| `OnPlayerReconnected` | { PlayerId } | Player reconnected |
| `OnGameEnd` | GameEndDto | Game complete with standings |
| `OnSettingsChanged` | { SmallBlind, StartingChips } | Host changed settings |
| `OnGameRestarted` | -- | Play again started |

### Modified behavior

| Method/Event | Change |
|---|---|
| `GetGameState` | Now returns hole cards for requesting player only |
| `OnGameStateChanged` | GameStateDto now includes IsShowdown and ShowdownHands when applicable |
| `SubmitAction` | Now validates turn order and raise amounts before submitting |

---

## Next Steps (Phase 3)

Phase 3 will focus on frontend development:
- React 18+ with TypeScript
- SignalR client integration
- All screens (Home, Create/Join, Lobby, Game Table, Game End)
- Card animations with Framer Motion
- Mobile-first responsive design
