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

### Web API: `PokerGame.Api` (net8.0) -- Phase 1
ASP.NET Core 8.0 backend with SignalR for real-time multiplayer:
- **`Hubs/GameHub.cs`** — SignalR hub handling room management (create/join/leave/kick), game control (start, submit action), and connection lifecycle (disconnect auto-folds).
- **`Services/RoomManager.cs`** — Thread-safe in-memory room management with 6-char room codes. Tracks players by ID, room code, and SignalR connection ID via three synchronized dictionaries.
- **`Services/GameEngineWrapper.cs`** — Bridges `TexasHoldemGame` to SignalR using `TcsPlayer`. Creates per-room `ActiveGame` state, wires TcsPlayer events to `Action<>` delegates, and syncs engine state back to room sessions.
- **`DTOs/`** — `GameStateDto`, `PlayerInfoDto`, `CardDto`, `YourTurnDto`, `LobbyStateDto`, etc. Serialized as JSON over SignalR.

### Tests: `src/Tests/`
- **`PokerGame.Api.Tests`** (xunit, net8.0) — Unit tests for RoomManager (22 tests covering create, join, leave, connection mapping, edge cases).
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
5. **Turn timeout**: TcsPlayer's built-in 30s timeout auto-folds. No separate timer in the wrapper.

### Key Design Decisions
- **Single solution**: `PokerGame.Api` and `PokerGame.Api.Tests` were added to the existing `TexasHoldem.sln` rather than creating a new solution.
- **Action parsing by string**: `SubmitAction` accepts string action types ("Fold", "Check", "Call", "Raise", "AllIn") rather than the engine's `PlayerActionType` enum, since the enum has no AllIn value. AllIn is handled as `Raise(int.MaxValue)`.
- **Per-room game state**: `GameEngineWrapper` uses a nested `ActiveGame` class per room to avoid cross-room state contamination.
- **CORS for dev**: Uses `SetIsOriginAllowed(_ => true)` + `AllowCredentials()` for SignalR compatibility. Must be restricted for production.
- **No separate turn timer**: TcsPlayer owns the timeout (30s). The wrapper does not run a competing timer.

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

**Server → Client:**
- `OnPlayerJoined({ PlayerId, Name, Emoji })`
- `OnPlayerLeft({ PlayerId })`
- `OnRoomClosed()`
- `OnKicked()`
- `OnGameStateChanged(GameStateDto)`
- `OnYourTurn(YourTurnDto)`
- `OnPlayerActing({ PlayerId })`
- `OnPlayerDisconnected({ PlayerId })`
- `OnGameEnded({ WinnerPlayerId, HandsPlayed })`
