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
- **Rebuy**: After each hand, players at zero chips are reset to their initial stack via `Rebuy()`.
- **`InternalsVisibleTo`**: The `TexasHoldem.Logic` project exposes internals to `TexasHoldem.Logic.Tests` for testing internal types like `InternalPlayerMoney`.
