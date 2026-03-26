# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the entire solution
dotnet build src/TexasHoldem.sln

# Run all tests (engine + API)
dotnet test src/TexasHoldem.sln

# Run only the API backend tests
dotnet test src/Tests/PokerGame.Api.Tests/

# Run only the engine unit tests
dotnet test src/Tests/TexasHoldem.Logic.Tests/

# Run a specific test class or method
dotnet test src/Tests/PokerGame.Api.Tests/ --filter "FullyQualifiedName~RoomManagerTests"

# Run the API server (default: http://localhost:5000)
dotnet run --project src/PokerGame.Api/

# Run the console UI
dotnet run --project src/UI/TexasHoldem.UI.Console/

# Frontend (React + Vite)
cd frontend && npm install       # first time only
cd frontend && npm run dev       # dev server at http://localhost:3000 (proxies /gamehub to :5000)
cd frontend && npm run build     # production build (tsc + vite)
```

Note: On ARM64 Macs, .NET 8 is installed via Homebrew at `/opt/homebrew/opt/dotnet@8/libexec`. Set `DOTNET_ROOT` if `dotnet` is not in PATH.

StyleCop.Analyzers is enforced across all projects via `src/Rules.ruleset` and `src/Settings.StyleCop`. Build warnings from StyleCop violations will block the CI pipeline.

## Architecture

The solution is structured into five layers:

### Core engine: `TexasHoldem.Logic` (netstandard2.0)
Published as a NuGet package (`TexasHoldemGameEngine`). Contains all game mechanics:
- **`GameMechanics/`** — `TexasHoldemGame` is the top-level entry point; it wraps `IPlayer` instances in `InternalPlayer` objects and drives the game loop. Each hand is managed by `HandLogic`, which runs the four betting rounds (PreFlop → Flop → Turn → River) via `BettingLogic`. `PotCreator` handles side pot splitting for all-in scenarios.
- **`Helpers/`** — `HandEvaluator` determines the best 5-card hand from any 7-card combination. `Helpers` (static) compares two hands and computes relative hand rank values for multi-player showdowns.
- **`Players/`** — `IPlayer` is the contract every player must implement. `BasePlayer` is the abstract base class that tracks hole cards and community cards. `PlayerDecorator` supports the decorator pattern used by the console UI.
- **`Cards/`** — `Deck`, `Card`, `CardSuit`, `CardType`, `CardExtensions`.

### AI players: `TexasHoldem.AI.*`
- **`DummyPlayer`** — Five trivial strategies: always fold, always call, always raise, always all-in, and a random mix (`DummyPlayer`).
- **`SmartPlayer`** — Uses `HandStrengthValuation.PreFlop()` to classify hole cards as `Unplayable`, `Risky`, or `Recommended`, then raises with randomized sizing. Post-flop logic is not yet implemented (it just calls).

### Console UI: `TexasHoldem.UI.Console` (netcoreapp3.1)
`ConsoleUiDecorator` wraps any `IPlayer` via the decorator pattern to render that player's box to a fixed console layout. `ConsolePlayer` enables a human to play interactively. `Program.Main` hardcodes the player lineup and console dimensions.

### Web API: `PokerGame.Api` (net8.0) -- Phase 1 + 2
ASP.NET Core 8.0 backend with SignalR for real-time multiplayer:
- **`Hubs/GameHub.cs`** — SignalR hub handling room management (create/join/leave/kick), game control (start, submit action with validation), host controls (UpdateSettings, PlayAgain), reconnection (Reconnect), and connection lifecycle (disconnect with 60s grace period during games).
- **`Services/RoomManager.cs`** — Thread-safe in-memory room management with 6-char room codes. Tracks players by ID, room code, and SignalR connection ID via three synchronized dictionaries. Supports `ResetRoom` for play-again flow.
- **`Services/GameEngineWrapper.cs`** — Bridges `TexasHoldemGame` to SignalR using `TcsPlayer`. Creates per-room `ActiveGame` state, wires TcsPlayer events to `Action<>` delegates, syncs engine state back to room sessions. Includes per-second turn timer countdown, showdown state tracking, action validation, and per-player hole card filtering.
- **`DTOs/`** — `GameStateDto`, `PlayerInfoDto`, `CardDto`, `YourTurnDto`, `LobbyStateDto`, `GameEndDto`, `PlayerStandingDto`, `TurnTimerDto`. Serialized as JSON over SignalR.

### Tests: `src/Tests/`
- **`PokerGame.Api.Tests`** (xunit, net8.0) — 32 unit tests: RoomManager (25 tests covering create, join, leave, connection mapping, reset, edge cases) + GameEngineWrapper (7 tests covering submit, state, validation).
- **`TexasHoldem.Logic.Tests`** (xunit + Moq) — Unit tests for cards, hand evaluation, game mechanics, and betting rules.
- **`TexasHoldem.Tests.GameSimulations`** — Console app that runs full game simulations (e.g., SmartPlayer vs AlwaysCallPlayer) and prints win statistics.

## Implementing a New Player

Extend `BasePlayer` and override `PostingBlind` and `GetTurn`. `BuyIn` returning `-1` uses the game's default initial stack.

```csharp
public class MyPlayer : BasePlayer
{
    public override string Name { get; } = "MyPlayer";
    public override int BuyIn { get; } = -1;

    public override PlayerAction PostingBlind(IPostingBlindContext context)
        => context.BlindAction;

    public override PlayerAction GetTurn(IGetTurnContext context)
        => PlayerAction.CheckOrCall();
}
```

`GetTurnContext` exposes `RoundType`, `MoneyLeft`, `MoneyToCall`, `MinRaise`, `CanCheck`, `CanRaise`, and pot information. Access hole cards via `this.FirstCard` / `this.SecondCard` (set by `BasePlayer.StartHand`).

## Key Design Notes

- **Blind schedule**: `TexasHoldemGame.SmallBlinds` defines the escalation schedule but the game currently holds blinds constant (`SmallBlinds[0]`). The escalating-blind logic is commented out.
- **No rebuy**: Players bust out at 0 chips. The game terminates when 1 player remains. (Rebuy was removed in Phase 0.)
- **`InternalsVisibleTo`**: The `TexasHoldem.Logic` project exposes internals to `TexasHoldem.Logic.Tests` for testing internal types like `InternalPlayerMoney`.
- **`PlayerAction` is immutable**: `Fold()` and `CheckOrCall()` return shared singletons. `Raise()` and `Post()` return new instances. Never mutate `Money` — create a new action instead.
- **Card dealing uses CSPRNG**: `RandomProvider` uses `System.Security.Cryptography.RandomNumberGenerator` with rejection sampling (no modular bias). The instance is static and thread-safe.

## Phase 0 Changes (Engine Fixes)

Phase 0 fixed 7 critical bugs and added the async bridge prototype. See `docs/superpowers/plans/2026-03-23-phase0-engine-fixes.md` for the full plan.

### Bug Fixes Applied
1. **Full house evaluation** (`HandEvaluator.cs`): Changed `if/if` to `if/else if` to prevent >5 cards when two three-of-a-kinds exist.
2. **Showdown ranking overflow** (`Helpers.cs`): Scaled `HandRankType` by 10000 in `GetHandRankValue` to prevent win-count from overflowing into adjacent rank ranges.
3. **Split pot chip loss** (`HandLogic.cs`): Added `pot % count` remainder distribution to first nominees.
4. **Heads-up showdown** (`HandLogic.cs`): Removed 2-player special case; all showdowns now use the unified multi-player path with proper side pot handling.
5. **Infinite game loop** (`TexasHoldemGame.cs`): Removed `Rebuy()` method and its call in `PlayGame()`.
6. **Constructor validation** (`TexasHoldemGame.cs`): Added null check for individual players in the collection before creating `InternalPlayer` wrappers.
7. **Mutable singleton** (`PlayerAction.cs` + `InternalPlayerMoney.cs`): Removed `internal set` on `Money`; all-in now creates a new `PlayerAction.Raise(amount)` instead of mutating.

### Additional Fix
- **Uncontested side pot**: `HandLogic.cs` line that did `continue` for single-player pots now correctly awards the pot to that player.

### Async Bridge: `TcsPlayer`
`src/TexasHoldem.Logic/Async/TcsPlayer.cs` — An `IPlayer` implementation that bridges the synchronous game engine to async callers using `ManualResetEventSlim`.

- Engine calls `GetTurn()` → blocks on the gate
- External code (e.g., SignalR hub) calls `SubmitAction(action)` → unblocks the gate
- Timeout (default 30s) → auto-folds
- Events: `TurnRequested`, `HandStarted`, `RoundStarted`, `HandEnded`
- Implements `IDisposable` (disposes `ManualResetEventSlim`)

## Phase 1 Changes (Backend Foundation)

Phase 1 added the ASP.NET Core backend with SignalR real-time communication. See `PHASE1_PLAN.md` for the full plan and review amendments.

### Architecture: How the Backend Works
1. **Client connects** to `/gamehub` via SignalR WebSocket.
2. **Room lifecycle**: `CreateRoom` → returns 6-char code. `JoinRoom(code)` → joins SignalR group. Host calls `StartGame`.
3. **Game loop**: `GameEngineWrapper.StartGameAsync` creates `TcsPlayer` per player, runs `TexasHoldemGame.Start()` on a background thread. The engine blocks on `TcsPlayer.GetTurn()` until the client submits an action via `SubmitAction`.
4. **State sync**: TcsPlayer events (`HandStarted`, `RoundStarted`, `TurnRequested`, `HandEnded`) fire on the engine thread, update room session state, and broadcast `GameStateDto` to all clients via SignalR group.
5. **Turn timeout**: TcsPlayer's built-in 30s timeout auto-folds. The wrapper also runs a per-second countdown timer that is cancelled on both action submission and timeout.

### Key Design Decisions
- **Single solution**: `PokerGame.Api` and `PokerGame.Api.Tests` were added to the existing `TexasHoldem.sln` rather than creating a new solution.
- **Action parsing by string**: `SubmitAction` accepts string action types ("Fold", "Check", "Call", "Raise", "AllIn") rather than the engine's `PlayerActionType` enum, since the enum has no AllIn value. AllIn is handled as `Raise(int.MaxValue)`.
- **Per-room game state**: `GameEngineWrapper` uses a nested `ActiveGame` class per room to avoid cross-room state contamination. `ActiveGame` holds a `Room` reference for populating player info in `BuildDto()`.
- **CORS for dev**: Uses `SetIsOriginAllowed(_ => true)` + `AllowCredentials()` for SignalR compatibility. Must be restricted for production.
- **Events wired in `Program.cs`**: All `GameEngineWrapper` events are subscribed in `Program.cs` via `IHubContext<GameHub>`, NOT in the Hub constructor (hubs are transient — subscribing there leaks handlers).
- **Hole cards are hidden info**: Broadcast `OnGameStateChanged` does NOT include hole cards (same DTO for all players). Clients must call `GetGameState()` after receiving `OnGameStateChanged` to get their own hole cards.

## Phase 2 Changes (Real-Time Gameplay & State Sync)

Phase 2 completed the real-time multiplayer infrastructure. See `PHASE2_PLAN.md` for the full plan and 7 review amendments.

### What Was Added
1. **Hole card exposure**: `GetGameState` accepts `requestingPlayerId` and filters hole cards per-player. `BuildDto()` now includes player info from the Room reference.
2. **Turn timer countdown**: Per-second `TurnTimerTick` event broadcast to all clients. Timer cancelled on both `SubmitAction` and TcsPlayer timeout (via `CancelAllTimers` at start of `HandleTurnRequested` and `HandleHandEnded`).
3. **Showdown results**: `GameStateDto.IsShowdown` and `ShowdownHands` populated from `IEndHandContext.ShowdownCards` during `HandleHandEnded`.
4. **Game end with standings**: `GameEndDto` with `WinnerPlayerId`, `WinnerName`, `TotalHandsPlayed`, and `Standings` (ordered by chips). Active game kept alive until `ClearGameState` (called by PlayAgain).
5. **Action validation**: `ValidateAction` checks turn order and raise amounts before `SubmitAction` passes to engine.
6. **Host controls**: `UpdateSettings` (small blind, starting chips) with input validation. Lobby-only, host-only.
7. **Play again**: `PlayAgain` resets room via `ResetRoom` + `ClearGameState`. Host-only, game-complete only.
8. **Reconnection**: `Reconnect(playerId)` takes playerId (not connection-based — new connections have new IDs). 60s grace period during games before auto-fold. `PlayerSession.IsDisconnected` tracking.
9. **Lobby disconnect cleanup**: Disconnecting in lobby removes player from room (not just marks disconnected).
10. **Thread safety**: `ActiveGame` uses `ConcurrentDictionary` for `HoleCards`, `ChipsAtHandStart`, `NameToSessionId`, `ShowdownCards`, `TimerCts`, and `PlayerMap`.

### SignalR Hub Methods (API Contract)
**Client → Server:**
- `CreateRoom(hostName, emoji, startingChips)` → `RoomCreatedResult`
- `JoinRoom(roomCode, playerName, emoji)` → `JoinResult`
- `LeaveRoom()` → void
- `KickPlayer(targetPlayerId)` → bool
- `StartGame()` → `ActionResultDto`
- `SubmitAction(actionType, amount?)` → `ActionResultDto`
- `GetGameState()` → `GameStateDto`
- `GetLobbyState()` → `LobbyStateDto`
- `UpdateSettings(smallBlind?, startingChips?)` → `ActionResultDto` *(Phase 2)*
- `PlayAgain()` → `ActionResultDto` *(Phase 2)*
- `Reconnect(playerId)` → `GameStateDto?` *(Phase 2)*

**Server → Client:**
- `OnPlayerJoined({ PlayerId, Name, Emoji })`
- `OnPlayerLeft({ PlayerId })`
- `OnRoomClosed()`
- `OnKicked()`
- `OnGameStateChanged(GameStateDto)`
- `OnYourTurn(YourTurnDto)`
- `OnPlayerActing({ PlayerId })`
- `OnPlayerDisconnected({ PlayerId, GracePeriodSeconds? })`
- `OnPlayerReconnected({ PlayerId })` *(Phase 2)*
- `OnTurnTimer(TurnTimerDto)` *(Phase 2)*
- `OnSettingsChanged({ SmallBlind, StartingChips })` *(Phase 2)*
- `OnGameEnd(GameEndDto)` *(Phase 2)*
- `OnGameRestarted()` *(Phase 2)*

## Phase 3 Changes (Frontend)

Phase 3 added the React 18 + TypeScript frontend SPA. See `docs/superpowers/plans/2026-03-24-phase3-frontend.md` for the full plan and `docs/superpowers/specs/2026-03-24-phase3-frontend-design.md` for the design spec.

### Frontend Architecture
- **Framework:** React 18 + TypeScript, built with Vite 8, dev server on port 3000
- **State management:** Zustand single store (`frontend/src/store/gameStore.ts`)
- **Real-time:** `@microsoft/signalr` client connecting to `/gamehub` (proxied to backend in dev)
- **Animations:** Framer Motion for card dealing, flipping, screen transitions
- **Routing:** React Router v6 with 6 routes: `/`, `/create`, `/join`, `/lobby`, `/game`, `/game-end`
- **Visual theme:** "Classic Felt" — deep green felt gradient, cream accents (#e8d5a3), pill buttons, warm card shadows

### Frontend File Structure
```
frontend/src/
├── App.tsx                    # BrowserRouter + SignalRProvider + Routes
├── SignalRProvider.tsx         # Mounts useSignalREvents hook inside router context
├── main.tsx                   # React root, imports globals.css
├── types/api.ts               # 13 DTO interfaces (camelCase, matches backend JSON)
├── services/signalRService.ts # Singleton SignalR client with buffered handler pattern
├── store/gameStore.ts         # Zustand: connection, lobby, game, turn, timer, game end state
├── hooks/useSignalR.ts        # 12 SignalR event handlers with state-driven navigation
├── styles/globals.css         # Classic Felt CSS custom properties + responsive breakpoints
├── components/
│   ├── common/                # Button (7 variants), Input (with label/error)
│   ├── cards/                 # PlayingCard (face/back, 3 sizes, spring animation)
│   └── players/               # Avatar (emoji, active pulse, folded dim), ChipDisplay
└── screens/
    ├── Home/                  # Title + Create/Join buttons
    ├── CreateGame/            # Name, emoji picker, chips slider → createRoom
    ├── JoinGame/              # Room code, name, emoji → joinRoom
    ├── Lobby/                 # Room code display, player list, start/leave
    ├── GameTable/             # Opponents, community cards, pot, hole cards, bet controls
    └── GameEnd/               # Winner, standings, play again
```

### Key Design Decisions (Frontend)
- **JSON casing:** Backend uses ASP.NET Core default camelCase serialization. All TypeScript interfaces use camelCase (`playerId`, `roomCode`, not PascalCase).
- **State-driven navigation:** Screen transitions driven by SignalR events (`OnGameStateChanged` phase → `/game`, `OnGameEnd` → `/game-end`, `OnGameRestarted` → `/lobby`), ensuring all players transition together.
- **Hole card fetch pattern:** Broadcast `OnGameStateChanged` does NOT include hole cards. After receiving broadcast, client calls `getGameState()` to get own hole cards (backend filters per-player).
- **Buffered handler pattern:** `signalRService.on()` stores handlers in `pendingHandlers` array. When `connect()` runs, pending handlers are applied to the new connection. This allows handlers to be registered before the connection is established.
- **Event handlers registered once:** All 12 SignalR event handlers are registered in `useSignalREvents()` hook, mounted once via `SignalRProvider` inside `BrowserRouter`. Screens do NOT register their own handlers (prevents duplicate registration).
- **Turn timeout handling:** `OnGameStateChanged` handler checks if `currentPlayerToActId` changed away from the local player and clears `isMyTurn`, handling auto-fold on timeout without needing a second `OnYourTurn` event.
- **Mobile-first responsive:** Default styles target 375px+ width, 44px min touch targets, `viewport-fit: cover` for fullscreen feel. Small phone breakpoint at 380px reduces spacing.

## Bug Fix: Side Pot Display (fix/pot-display → master, PR #3)

Added support for displaying side pots in addition to the main pot on the game table.

**Problem:** The frontend only displayed the main pot (`gameState.mainPot`) without showing side pots that occur when players go all-in at different levels.

**Fix:** Updated `GameTable.tsx` and `GameTable.css` to:
- Display pots in a row below community cards using a new `.pot-row` container
- Show "Main" label when side pots exist, otherwise show "Pot"
- Loop through `gameState.sidePots` and display each with "Side" label
- Increase pot amount font size from 20px to 24px (desktop) and 20px (mobile)

## Bug Fix: At-Stake Display (fix/at-stake-display → master, PR #4)

Added at-stake bet amount display above each player's chip display.

**Problem:** Players couldn't see how much each opponent had bet in the current round - only their own chips were visible.

**Fix:** Updated `ChipDisplay.tsx` and `ChipDisplay.css` to:
- Add `atStake` prop to `ChipDisplay` component
- Display current bet amount above chip stack when `atStake > 0`
- Apply consistent styling using `.chip-stake` class with mono font
- Simplified `GameTable.tsx` by removing inline bet-badge rendering logic

**Files changed:**
- `frontend/src/components/players/ChipDisplay.css` - Added `.chip-display-wrapper`, `.chip-stake` styles
- `frontend/src/components/players/ChipDisplay.tsx` - Added `atStake` prop support
- `frontend/src/screens/GameTable/GameTable.tsx` - Pass `atStake={p.currentBet}` to ChipDisplay
- `frontend/src/screens/GameTable/GameTable.css` - Removed obsolete `.bet-badge` styles

## Feature: Winning Animation (fix/winning-animation → master, PR #51)

Added winning animation with pause, chip counters, and winner banner after each hand showdown.

**What was added:**
- **Backend**: broadcasts `HandComplete` phase after showdown with 2.5s pause before next hand
- **Chip counter animation**: Framer Motion counter animates chips up/down over 1.8s; delta badge (+N/-N) fades in near end
- **Winner banner**: shows winner(s) and pot amount during animation; handles single winner, split pot, and equal-split

**Files changed:**
- `src/PokerGame.Api/Services/GameEngineWrapper.cs` - Added `IsHandComplete` flag, `HandComplete` phase broadcast, 2.5s post-showdown delay
- `frontend/src/store/gameStore.ts` - Added `prevHandChips`, `handResultVisible` state
- `frontend/src/hooks/useSignalR.ts` - Snapshot chips on `HandInProgress`, set/clear `handResultVisible` on phase changes
- `frontend/src/components/players/ChipDisplay.tsx` - Added `animateTo`, `delta` props with Framer Motion counter
- `frontend/src/components/players/ChipDisplay.css` - Added `.chip-delta` styles
- `frontend/src/screens/GameTable/GameTable.tsx` - Added `WinnerBanner` component, delta computation, wired animation props
- `frontend/src/screens/GameTable/GameTable.css` - Added `.winner-banner` styles
