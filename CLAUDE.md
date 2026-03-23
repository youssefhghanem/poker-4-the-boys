# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the entire solution
dotnet build src/TexasHoldem.sln

# Run all tests
dotnet test src/TexasHoldem.sln

# Run a specific test class or method
dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HandEvaluatorTests"

# Run only the unit tests (not simulations)
dotnet test src/Tests/TexasHoldem.Logic.Tests/

# Run the console UI
dotnet run --project src/UI/TexasHoldem.UI.Console/
```

StyleCop.Analyzers is enforced across all projects via `src/Rules.ruleset` and `src/Settings.StyleCop`. Build warnings from StyleCop violations will block the CI pipeline.

## Architecture

The solution is structured into four layers:

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

### Tests: `src/Tests/`
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
