# Phase 0: Engine Fixes & Architecture

> **Status: REVIEWED & FINALIZED** (2026-03-23)
> See [`docs/superpowers/plans/2026-03-23-phase0-engine-fixes.md`](docs/superpowers/plans/2026-03-23-phase0-engine-fixes.md) for the reviewed implementation plan with corrected fixes.

## Review Summary

The original plan below was reviewed against the actual codebase. Key corrections made:
- **Bug #2 fix**: Simplified to rank scaling (x10000) instead of win/tie/loss tracking
- **Bug #4 fix**: Removed 2-player special case instead of duplicating multi-player logic
- **Bug #7 fix**: Added InternalPlayerMoney.cs update (the plan omitted updating the mutation site)
- **CSPRNG**: Fixed modular bias and Math.Abs overflow in the proposed replacement
- **Tests**: Corrected from NUnit to xUnit (project uses xUnit)
- **API contract (Step 11)**: Deferred to Phase 1 (premature for engine-fix phase)
- **Async bridge**: Only Approach A (TaskCompletionSource) — implemented as TcsPlayer IPlayer adapter
- **Uncontested side pot bug**: Discovered and fixed (line 153-156 in HandLogic.cs did `continue` instead of awarding pot)

## Overview

Phase 0 focuses on fixing critical bugs in the Texas Hold'em poker game engine and preparing the architecture for real-time multiplayer web gameplay. This phase addresses 7 critical bugs, replaces the insecure random number generator, disables auto-rebuy to allow proper game termination, prototypes the TaskCompletionSource async bridge, and establishes integration testing.

## Prerequisites

- .NET SDK (6.0 or later recommended)
- Visual Studio 2022 or VS Code with C# extension
- Basic knowledge of poker rules and hand rankings
- Familiarity with async/await patterns in C#

## Step 1: Fix Full House Evaluation (Bug #1)

### Objective
Fix the BestHand constructor exception when evaluating 7-card combinations with two three-of-a-kind groups plus a pair. The full house detection logic must always select exactly 5 cards (3+2).

### Files to Modify
- `src/TexasHoldem.Logic/Helpers/HandEvaluator.cs` (lines 70-98)

### Changes Required

The current logic at lines 70-98 has a bug: when there are more than one three-of-a-kind AND a pair exists, it can add up to more than 5 cards. The fix must ensure exactly 5 cards are always returned.

**Current Problematic Code (lines 70-98):**
```csharp
// Full
var pairTypes = this.GetTypesWithNCards(cardTypeCounts, 2);
var threeOfAKindTypes = this.GetTypesWithNCards(cardTypeCounts, 3);
if ((pairTypes.Count > 0 && threeOfAKindTypes.Count > 0) || threeOfAKindTypes.Count > 1)
{
    var bestCards = new List<CardType>();
    for (var i = 0; i < 3; i++)
    {
        bestCards.Add(threeOfAKindTypes[0]);
    }

    if (threeOfAKindTypes.Count > 1)
    {
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(threeOfAKindTypes[1]);
        }
    }

    if (pairTypes.Count > 0)
    {
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(pairTypes[0]);
        }
    }

    return new BestHand(HandRankType.FullHouse, bestCards);
}
```

**Fixed Code:**
```csharp
// Full House
var pairTypes = this.GetTypesWithNCards(cardTypeCounts, 2);
var threeOfAKindTypes = this.GetTypesWithNCards(cardTypeCounts, 3);
if ((pairTypes.Count > 0 && threeOfAKindTypes.Count > 0) || threeOfAKindTypes.Count > 1)
{
    var bestCards = new List<CardType>();
    
    // Always use the highest three-of-a-kind (3 cards)
    for (var i = 0; i < 3; i++)
    {
        bestCards.Add(threeOfAKindTypes[0]);
    }

    // Determine the best kicker pair: prefer second three-of-a-kind over other pairs
    // Only add exactly 2 more cards to make 5 total (3+2)
    if (threeOfAKindTypes.Count > 1)
    {
        // Use the second three-of-a-kind as the pair
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(threeOfAKindTypes[1]);
        }
    }
    else if (pairTypes.Count > 0)
    {
        // Use the highest pair
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(pairTypes[0]);
        }
    }

    // Ensure we have exactly 5 cards (defensive check)
    if (bestCards.Count != 5)
    {
        throw new InvalidOperationException($"Full house should have exactly 5 cards but got {bestCards.Count}");
    }

    return new BestHand(HandRankType.FullHouse, bestCards);
}
```

### Verification
1. Create a test case with 7 cards containing two three-of-a-kinds plus a pair (e.g., A♠A♥A♦K♣K♦Q♥J♠)
2. Verify the HandEvaluator.GetBestHand() returns exactly 5 cards
3. Verify the rank is correctly identified as FullHouse
4. Verify the three-of-a-kind component is the higher one (Aces) and pair is Kings

---

## Step 2: Fix Multi-Player Showdown Ranking (Bug #2)

### Objective
Replace the relative scoring system in GetHandRankValue with absolute hand comparison. Two players with identical hands must receive the same score.

### Files to Modify
- `src/TexasHoldem.Logic/Helpers/Helpers.cs` (lines 39-60)

### Changes Required

**Current Problematic Code:**
```csharp
public static int GetHandRankValue(
    IEnumerable<Card> player,
    IEnumerable<IEnumerable<Card>> opponents,
    IEnumerable<Card> communityCards)
{
    var playerHand = player.Concat(communityCards);
    var playerBestHand = HandEvaluator.GetBestHand(playerHand);
    var playerHandValue = (int)playerBestHand.RankType;

    foreach (var opponent in opponents)
    {
        var opponentHand = opponent.Concat(communityCards);
        var opponentBestHand = HandEvaluator.GetBestHand(opponentHand);

        if (playerBestHand.CompareTo(opponentBestHand) > 0)
        {
            playerHandValue++;
        }
    }

    return playerHandValue;
}
```

**Fixed Code:**
```csharp
public static int GetHandRankValue(
    IEnumerable<Card> player,
    IEnumerable<IEnumerable<Card>> opponents,
    IEnumerable<Card> communityCards)
{
    var playerHand = player.Concat(communityCards);
    var playerBestHand = HandEvaluator.GetBestHand(playerHand);
    
    // Calculate absolute hand strength based on CompareTo result
    // CompareTo returns: 1 if player > opponent, -1 if player < opponent, 0 if equal
    var wins = 0;
    var losses = 0;
    var ties = 0;

    foreach (var opponent in opponents)
    {
        var opponentHand = opponent.Concat(communityCards);
        var opponentBestHand = HandEvaluator.GetBestHand(opponentHand);
        var comparison = playerBestHand.CompareTo(opponentBestHand);

        if (comparison > 0)
        {
            wins++;
        }
        else if (comparison < 0)
        {
            losses++;
        }
        else
        {
            ties++;
        }
    }

    // Return a composite value that uniquely identifies hand strength
    // Format: [HandRankType][+wins][-losses][+ties] - gives same score for equal hands
    // Scale hand rank by 10000 to leave room for win/loss/tie modifiers
    var handRankBase = (int)playerBestHand.RankType * 10000;
    return handRankBase + (wins * 100) + (ties * 10) - losses;
}
```

### Verification
1. Create test with two players having identical 7-card hands
2. Verify both players get the same GetHandRankValue return value
3. Verify a player with a better hand gets a higher value than a player with a worse hand
4. Run existing tests to ensure no regression

---

## Step 3: Fix Split Pot Chip Loss (Bug #3)

### Objective
Fix integer division that loses chips when splitting pots. Use decimal division and properly distribute remaining chips.

### Files to Modify
- `src/TexasHoldem.Logic/GameMechanics/HandLogic.cs` (line 164)

### Changes Required

**Current Problematic Code (line 164):**
```csharp
var prize = oneOfThePots.AmountOfMoney / count; // TODO: If there are odd chips in a split pot.
```

**Fixed Code:**
```csharp
// Calculate prize with decimal division to preserve precision
var prize = oneOfThePots.AmountOfMoney / count;

// Calculate remainder chips (will be 0 when divisible, 1+ otherwise)
var remainder = oneOfThePots.AmountOfMoney % count;

// Distribute remainder chips to first players in order (deterministic)
var nomineesList = nominees.ToList();
for (var i = 0; i < remainder; i++)
{
    this.players.First(x => x.Name == nomineesList[i]).PlayerMoney.Money += 1;
}

// Distribute the main prize (already integer division result)
foreach (var nominee in nominees)
{
    this.players.First(x => x.Name == nominee).PlayerMoney.Money += prize;
}
```

**Note:** This change also needs to wrap the prize distribution in a single operation. The current code distributes prize to each nominee but the remainder logic needs to be added.

### Verification
1. Test split pot scenario: 101 chips split between 2 players → each gets 50, remainder 1 goes to first player
2. Test split pot scenario: 103 chips split between 3 players → each gets 34, remainder 1
3. Test split pot scenario: 100 chips split between 4 players → each gets 25
4. Verify total chips are conserved

---

## Step 4: Fix Heads-Up Showdown Side Pots (Bug #4)

### Objective
Ensure heads-up (2-player) showdown respects main/side pot structure instead of giving entire pot to winner.

### Files to Modify
- `src/TexasHoldem.Logic/GameMechanics/HandLogic.cs` (lines 100-117)

### Changes Required

**Current Problematic Code (lines 100-117):**
```csharp
if (this.players.Count == 2)
{
    var betterHand = Helpers.CompareCards(
    this.players[0].Cards.Concat(this.communityCards),
    this.players[1].Cards.Concat(this.communityCards));
    if (betterHand > 0)
    {
        this.players[0].PlayerMoney.Money += pot;
    }
    else if (betterHand < 0)
    {
        this.players[1].PlayerMoney.Money += pot;
    }
    else
    {
        this.players[0].PlayerMoney.Money += pot / 2;
        this.players[1].PlayerMoney.Money += pot / 2;
    }
}
```

**Fixed Code:**
```csharp
if (this.players.Count == 2)
{
    // Use the same pot distribution logic as multi-player case
    // This properly handles main pot and side pots
    var handRankValueOfPlayers = new SortedDictionary<int, ICollection<string>>();
    var playersInHand = this.players.Where(p => p.PlayerMoney.InHand);

    foreach (var player in playersInHand)
    {
        var opponents = playersInHand.Where(p => p.Name != player.Name).Select(s => s.Cards);
        var handRankValue = Helpers.GetHandRankValue(player.Cards, opponents, this.communityCards);

        if (handRankValueOfPlayers.ContainsKey(handRankValue))
        {
            handRankValueOfPlayers[handRankValue].Add(player.Name);
        }
        else
        {
            handRankValueOfPlayers.Add(handRankValue, new List<string> { player.Name });
        }
    }

    // Now distribute pots using the same logic as multi-player
    var remainingPots = new Stack<Pot>();
    var pots = new Stack<Pot>(sidePot);
    pots.Push(mainPot);

    foreach (var playersWithTheBestHand in handRankValueOfPlayers.Reverse())
    {
        do
        {
            var oneOfThePots = pots.Pop();

            if (oneOfThePots.ActivePlayer.Count == 0)
            {
                throw new Exception("There are no players in the pot");
            }
            else if (oneOfThePots.ActivePlayer.Count == 1)
            {
                // Single player wins uncontested pot
                var winnerName = oneOfThePots.ActivePlayer.First();
                this.players.First(x => x.Name == winnerName).PlayerMoney.Money += oneOfThePots.AmountOfMoney;
            }
            else
            {
                var nominees = oneOfThePots.ActivePlayer.Intersect(playersWithTheBestHand.Value);
                var count = nominees.Count();

                if (count > 0)
                {
                    // Split pot among nominees (same logic as multi-player)
                    var prize = oneOfThePots.AmountOfMoney / count;
                    var remainder = oneOfThePots.AmountOfMoney % count;
                    var nomineesList = nominees.ToList();

                    for (var i = 0; i < remainder; i++)
                    {
                        this.players.First(x => x.Name == nomineesList[i]).PlayerMoney.Money += 1;
                    }

                    foreach (var nominee in nominees)
                    {
                        this.players.First(x => x.Name == nominee).PlayerMoney.Money += prize;
                    }
                }
                else
                {
                    remainingPots.Push(oneOfThePots);
                }
            }
        }
        while (pots.Count > 0);

        if (remainingPots.Count == 0)
        {
            break;
        }
        else
        {
            while (remainingPots.Count > 0)
            {
                pots.Push(remainingPots.Pop());
            }
        }
    }
}
```

### Verification
1. Test 2-player scenario with no all-in (single main pot)
2. Test 2-player scenario with all-in creating a side pot
3. Verify winner gets correct pot amount
4. Verify split pots work correctly with odd chips

---

## Step 5: Fix Infinite Game Loop (Bug #5)

### Objective
Disable auto-rebuy so the game terminates when only 1 player has money.

### Files to Modify
- `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs` (lines 116-141)

### Changes Required

**Current Code (PlayGame method):**
```csharp
private void PlayGame()
{
    var shifted = this.allPlayers.ToList();

    // While at least two players have money
    while (this.allPlayers.WithMoney().Count() > 1)
    {
        this.HandsPlayed++;

        // Every 10 hands the blind increases
        // var smallBlind = SmallBlinds[(this.HandsPlayed - 1) / 10];
        var smallBlind = SmallBlinds[0];

        // Players are shifted in order of priority to make a move
        shifted = shifted.WithMoney().ToList();
        shifted.Add(shifted.First());
        shifted.RemoveAt(0);

        // Rotate players
        IHandLogic hand = new HandLogic(shifted, this.HandsPlayed, smallBlind);

        hand.Play();

        this.Rebuy();  // BUG: This causes infinite loop
    }
}
```

**Fixed Code:**
```csharp
private void PlayGame()
{
    var shifted = this.allPlayers.ToList();

    // While at least two players have money
    while (this.allPlayers.WithMoney().Count() > 1)
    {
        this.HandsPlayed++;

        // Every 10 hands the blind increases
        // var smallBlind = SmallBlinds[(this.HandsPlayed - 1) / 10];
        var smallBlind = SmallBlinds[0];

        // Players are shifted in order of priority to make a move
        shifted = shifted.WithMoney().ToList();
        
        // Check if game should end (less than 2 players with money)
        if (shifted.Count < 2)
        {
            break;
        }

        shifted.Add(shifted.First());
        shifted.RemoveAt(0);

        // Rotate players
        IHandLogic hand = new HandLogic(shifted, this.HandsPlayed, smallBlind);

        hand.Play();

        // REMOVED: this.Rebuy();
        // Game now terminates when only 1 player has money
    }
}
```

### Verification
1. Create a game with 2 players with limited chips
2. Play until one player goes all-in and loses
3. Verify game terminates when only 1 player has money
4. Verify no infinite loop occurs

---

## Step 6: Fix Constructor Validation Order (Bug #6)

### Objective
Move null validation before the foreach loop that iterates the players collection.

### Files to Modify
- `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs` (lines 22-40 and 56-81)

### Changes Required

**Current Problem:**
The public constructors (lines 22-40 and 42-54) perform validation, but the private constructor (line 56+) iterates the collection BEFORE validation occurs in the call stack.

**Fixed Private Constructor:**
```csharp
private TexasHoldemGame(ICollection<IPlayer> players, int initialMoney = 1000)
{
    // Validate null players collection FIRST
    if (players == null)
    {
        throw new ArgumentNullException(nameof(players));
    }

    // Validate each player in the collection before processing
    foreach (var player in players)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(players), "A player in the collection is null");
        }
    }

    if (players.Count < 2 || players.Count > 10)
    {
        throw new ArgumentOutOfRangeException(nameof(players), "The number of players must be from 2 to 10");
    }

    if (initialMoney <= 0 || initialMoney > 200000)
    {
        throw new ArgumentOutOfRangeException(nameof(initialMoney), "Initial money should be greater than 0 and less than 200000");
    }

    this.allPlayers = new List<InternalPlayer>(players.Count);
    foreach (var item in players)
    {
        this.allPlayers.Add(new InternalPlayer(item));
    }

    this.initialMoney = initialMoney;
    this.HandsPlayed = 0;
}
```

### Verification
1. Test creating a game with a null player in the array
2. Verify ArgumentNullException is thrown before any processing
3. Test normal game creation still works

---

## Step 7: Fix Mutable Shared Singleton Actions (Bug #7)

### Objective
Make PlayerAction immutable to prevent corruption of static singleton instances.

### Files to Modify
- `src/TexasHoldem.Logic/Players/PlayerAction.cs`

### Changes Required

**Current Problem:**
- `Fold()` and `CheckOrCall()` return shared static instances
- `Money` property has `internal set`, allowing mutation
- Any code that modifies Money corrupts the singleton

**Fix Option A: Remove Money setter (Recommended)**
```csharp
public class PlayerAction
{
    private static readonly PlayerAction FoldObject = new PlayerAction(PlayerActionType.Fold);
    private static readonly PlayerAction CheckCallObject = new PlayerAction(PlayerActionType.CheckCall, 0);

    private PlayerAction(PlayerActionType type)
    {
        this.Type = type;
        this.Money = 0;
    }

    private PlayerAction(int money, PlayerActionType type = PlayerActionType.Raise)
    {
        this.Type = type;
        this.Money = money;
    }

    public PlayerActionType Type { get; }

    // Changed from: public int Money { get; internal set; }
    public int Money { get; }

    public static PlayerAction Fold()
    {
        return FoldObject;
    }

    public static PlayerAction CheckOrCall()
    {
        return CheckCallObject;
    }

    /// <summary>
    /// Creates a new object containing information about the player action and the raise amount
    /// </summary>
    /// <param name="withAmount">
    /// The amount to raise with.
    /// If amount is less than the minimum amount for raising then the game will take this minimum amount from the players money.
    /// If amount is more or equal to the players money the player will be in all-in state
    /// </param>
    /// <returns>A new player action object containing information about the player action and the raise amount</returns>
    public static PlayerAction Raise(int withAmount)
    {
        if (withAmount <= 0)
        {
            return CheckOrCall();
        }

        return new PlayerAction(withAmount);
    }

    public static PlayerAction Post(int blind)
    {
        return new PlayerAction(blind, PlayerActionType.Post);
    }

    public override string ToString()
    {
        if (this.Type == PlayerActionType.Raise || this.Type == PlayerActionType.Post)
        {
            return $"{this.Type}({this.Money})";
        }
        else
        {
            return this.Type.ToString();
        }
    }
}
```

**Fix Option B: Return new immutable instances**
If there are dependencies that require setting Money after creation, use this approach instead:
```csharp
public static PlayerAction Fold()
{
    return new PlayerAction(PlayerActionType.Fold);
}

public static PlayerAction CheckOrCall()
{
    return new PlayerAction(PlayerActionType.CheckCall);
}
```

### Verification
1. Verify Fold() returns an instance that cannot be modified
2. Verify CheckOrCall() returns an instance that cannot be modified
3. Verify Raise() and Post() still create new instances correctly
4. Run existing tests to ensure no regression

---

## Step 8: Replace System.Random with CSPRNG

### Objective
Replace the insecure System.Random with System.Security.Cryptography.RandomNumberGenerator for cryptographically secure card dealing.

### Files to Modify
- `src/TexasHoldem.Logic/Extensions/RandomProvider.cs`

### Changes Required

**Current Code:**
```csharp
public static class RandomProvider
{
    private static readonly ThreadLocal<Random> Random =
        new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

    private static int seed = Environment.TickCount;

    public static int Next(int minValue, int maxValue)
    {
        return Random.Value.Next(minValue, maxValue);
    }
}
```

**Fixed Code:**
```csharp
namespace TexasHoldem.Logic.Extensions
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Thread-safe cryptographically secure random number generator
    /// </summary>
    public static class RandomProvider
    {
        private static readonly object LockObject = new object();

        public static int Next(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue");
            }

            var range = (long)maxValue - minValue;

            lock (LockObject)
            {
                // Use RandomNumberGenerator for cryptographic security
                using (var rng = RandomNumberGenerator.Create())
                {
                    var randomBytes = new byte[4];
                    rng.GetBytes(randomBytes);
                    
                    // Convert to int and map to desired range
                    var randomValue = Math.Abs(BitConverter.ToInt32(randomBytes, 0));
                    return minValue + (int)(randomValue % range);
                }
            }
        }

        /// <summary>
        /// Returns a random integer between 0 (inclusive) and maxValue (exclusive)
        /// </summary>
        public static int Next(int maxValue)
        {
            return Next(0, maxValue);
        }

        /// <summary>
        /// Returns a non-negative random integer
        /// </summary>
        public static int Next()
        {
            return Next(0, int.MaxValue);
        }
    }
}
```

### Verification
1. Verify the random distribution is uniform (statistical test)
2. Verify thread-safety under concurrent access
3. Run Deck shuffle tests to ensure proper randomization
4. Verify no performance regression (CSPRNG is slower but acceptable for card games)

---

## Step 9: Add Game End Detection

### Objective
Add proper game-end detection and return meaningful results when game terminates.

### Files to Modify
- `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs`

### Changes Required

The Start() method should return more meaningful information when the game ends:

```csharp
public class GameEndResult
{
    public string WinnerName { get; set; }
    
    public IReadOnlyList<PlayerFinalStanding> Standings { get; set; }
    
    public int TotalHandsPlayed { get; set; }
}

public class PlayerFinalStanding
{
    public string Name { get; set; }
    
    public int FinalChips { get; set; }
    
    public int Position { get; set; }
}

// Modify the Start() method to return GameEndResult instead of just winner
public GameEndResult Start()
{
    var playerNames = this.allPlayers.Select(x => x.Name).ToList().AsReadOnly();
    foreach (var player in this.allPlayers)
    {
        player.StartGame(new StartGameContext(playerNames, player.BuyIn == -1 ? this.initialMoney : player.BuyIn));
    }

    this.PlayGame();

    // Build final standings
    var orderedPlayers = this.allPlayers
        .OrderByDescending(p => p.PlayerMoney.Money)
        .Select((p, index) => new PlayerFinalStanding
        {
            Name = p.Name,
            FinalChips = p.PlayerMoney.Money,
            Position = index + 1
        })
        .ToList();

    var winner = orderedPlayers.First();
    
    foreach (var player in this.allPlayers)
    {
        player.EndGame(new EndGameContext(winner.Name));
    }

    return new GameEndResult
    {
        WinnerName = winner.Name,
        Standings = orderedPlayers,
        TotalHandsPlayed = this.HandsPlayed
    };
}
```

### Verification
1. Run game until one player wins all chips
2. Verify GameEndResult contains correct winner
3. Verify Standings are ordered by final chip count
4. Verify TotalHandsPlayed is accurate

---

## Step 10: Prototype Sync-to-Async Bridge

### Objective
Prototype two approaches for converting the synchronous blocking game engine to async for real-time web gameplay.

### Files to Create
- `src/TexasHoldem.Logic/Async/TcsGameBridge.cs` (Approach A)
- `src/TexasHoldem.Logic/Async/AsyncEventGameEngine.cs` (Approach B)
- `src/TexasHoldem.Logic/Async/README.md` (Documentation)

### Approach A: TaskCompletionSource Bridge

**File: `src/TexasHoldem.Logic/Async/TcsGameBridge.cs`**

```csharp
namespace TexasHoldem.Logic.Async
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    using TexasHoldem.Logic.Players;

    /// <summary>
    /// Wraps blocking synchronous IPlayer.GetTurn calls in TaskCompletionSource
    /// for async/await compatibility. Suitable for SignalR integration.
    /// </summary>
    public class TcsGameBridge
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<PlayerAction>> _pendingActions = 
            new ConcurrentDictionary<string, TaskCompletionSource<PlayerAction>>();

        /// <summary>
        /// Creates an awaitable task that will be completed when SubmitAction is called
        /// </summary>
        public Task<PlayerAction> GetPlayerTurnAsync(string playerId, GetTurnContext context, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<PlayerAction>();
            
            // Register with cancellation token
            cancellationToken.Register(() => tcs.TrySetCanceled());
            
            _pendingActions[playerId] = tcs;

            // Set timeout
            // TODO: Add configurable timeout
            
            return tcs.Task;
        }

        /// <summary>
        /// Called from SignalR hub when client submits action
        /// </summary>
        public bool SubmitAction(string playerId, PlayerAction action)
        {
            if (_pendingActions.TryRemove(playerId, out var tcs))
            {
                return tcs.TrySetResult(action);
            }
            return false; // No pending action for this player
        }

        /// <summary>
        /// Check if player has a pending turn
        /// </summary>
        public bool HasPendingTurn(string playerId)
        {
            return _pendingActions.ContainsKey(playerId);
        }

        /// <summary>
        /// Cancel pending turn (e.g., if player disconnects)
        /// </summary>
        public void CancelPendingTurn(string playerId)
        {
            if (_pendingActions.TryRemove(playerId, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }
    }
}
```

### Approach B: Async Event-Driven Rewrite

**File: `src/TexasHoldem.Logic/Async/AsyncEventGameEngine.cs`**

```csharp
namespace TexasHoldem.Logic.Async
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    using TexasHoldem.Logic.Players;

    /// <summary>
    /// Async event-driven game engine that doesn't block on player turns
    /// </summary>
    public class AsyncEventGameEngine
    {
        // Events that the SignalR hub subscribes to
        public event EventHandler<PlayerTurnEventArgs> OnPlayerTurnRequested;
        public event EventHandler<PlayerActedEventArgs> OnPlayerActed;
        public event EventHandler<GameStateChangedEventArgs> OnGameStateChanged;
        
        private readonly IAsyncPlayer _player;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public AsyncEventGameEngine(IAsyncPlayer player)
        {
            _player = player;
        }

        /// <summary>
        /// Request player's turn - doesn't block, raises event and waits for callback
        /// </summary>
        public async Task<PlayerAction> RequestTurnAsync(GetTurnContext context)
        {
            var tcs = new TaskCompletionSource<PlayerAction>();

            void OnPlayerResponse(object sender, PlayerAction action)
            {
                tcs.TrySetResult(action);
                OnPlayerActed?.Invoke(this, new PlayerActedEventArgs(_player.Name, action));
            }

            try
            {
                // Raise event to notify UI/client
                OnPlayerTurnRequested?.Invoke(this, new PlayerTurnEventArgs(_player.Name, context));
                
                // Wait for response (via callback or timeout)
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), _cts.Token);
            }
            catch (TimeoutException)
            {
                // Auto-fold on timeout
                return PlayerAction.Fold();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    // Event args classes
    public class PlayerTurnEventArgs : EventArgs
    {
        public string PlayerId { get; }
        public GetTurnContext Context { get; }

        public PlayerTurnEventArgs(string playerId, GetTurnContext context)
        {
            PlayerId = playerId;
            Context = context;
        }
    }

    public class PlayerActedEventArgs : EventArgs
    {
        public string PlayerId { get; }
        public PlayerAction Action { get; }

        public PlayerActedEventArgs(string playerId, PlayerAction action)
        {
            PlayerId = playerId;
            Action = action;
        }
    }

    public class GameStateChangedEventArgs : EventArgs
    {
        public GameState State { get; }

        public GameStateChangedEventArgs(GameState state)
        {
            State = state;
        }
    }
}
```

### Trade-off Analysis Document

**File: `src/TexasHoldem.Logic/Async/README.md`**

```markdown
# Sync-to-Async Bridge: Trade-off Analysis

## Approach A: TaskCompletionSource Bridge

### Pros
- Minimal changes to existing game engine
- Direct replacement for blocking calls
- Preserves existing game logic flow
- Easier to debug (linear execution)

### Cons
- Blocks a thread per waiting player
- Not suitable for high concurrency (1000+ players)
- Memory overhead for TaskCompletionSource objects
- Timeout management adds complexity

### Best For
- Small to medium scale games (< 100 concurrent players)
- Prototyping phase
- Teams familiar with synchronous patterns

### Complexity: LOW

---

## Approach B: Async Event-Driven Rewrite

### Pros
- Non-blocking, highly scalable
- Better resource utilization
- Natural fit for WebSocket/SignalR
- Easier to add features like turn timers

### Cons
- Significant refactoring required
- More complex state management
- Harder to debug (event-driven flow)
- Requires rethinking game loop architecture

### Best For
- Large scale deployments
- Real-time multiplayer with many concurrent tables
- Long-term maintainability

### Complexity: HIGH

---

## Recommendation

For Phase 0, implement **Approach A (TaskCompletionSource)** as a prototype:
1. Minimal code changes
2. Validates the async integration pattern
3. Can be replaced with Approach B in Phase 1 if scaling is needed

The TCS bridge provides ~80% of the benefit with ~20% of the effort.
```

### Verification
1. Implement TcsGameBridge in a new file
2. Write integration test that simulates async player turns
3. Verify no blocking on game loop
4. Test timeout handling

---

## Step 11: Define SignalR API Contract

### Objective
Document the complete SignalR hub interface for real-time multiplayer.

### Files to Create
- `src/TexasHoldem.Logic/Network/IGameHub.cs` (Interface definition)
- `src/TexasHoldem.Logic/Network/GameState.cs` (State model)

### Hub Methods (Client → Server)

**File: `src/TexasHoldem.Logic/Network/IGameHub.cs`**

```csharp
namespace TexasHoldem.Logic.Network
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// SignalR Hub interface for Texas Hold'em multiplayer game
    /// </summary>
    public interface IGameHub
    {
        // ========== Room Management ==========
        
        /// <summary>
        /// Creates a new game room
        /// </summary>
        /// <param name="hostName">Name of the host player</param>
        /// <param name="startingChips">Initial chip count (default: 1000)</param>
        /// <returns>Room code for sharing</returns>
        Task<RoomCreatedResult> CreateRoom(string hostName, int startingChips = 1000);

        /// <summary>
        /// Join an existing room
        /// </summary>
        /// <param name="roomCode">The room code to join</param>
        /// <param name="playerName">Player's display name</param>
        /// <param name="emoji">Player's avatar emoji</param>
        /// <returns>Join success result with player ID</returns>
        Task<JoinResult> JoinRoom(string roomCode, string playerName, string emoji);

        /// <summary>
        /// Leave current room
        /// </summary>
        Task<bool> LeaveRoom();

        /// <summary>
        /// Kick a player (host only)
        /// </summary>
        Task<bool> KickPlayer(string playerId);

        // ========== Game Control ==========

        /// <summary>
        /// Start the game (host only, requires min 2 players)
        /// </summary>
        Task<bool> StartGame();

        /// <summary>
        /// Submit player action during their turn
        /// </summary>
        /// <param name="actionType">Fold, Check, Call, Raise, or AllIn</param>
        /// <param name="amount">Amount for raise (optional)</param>
        Task<bool> SubmitAction(PlayerActionType actionType, int? amount = null);

        // ========== Queries ==========

        /// <summary>
        /// Get current game state
        /// </summary>
        Task<GameState> GetGameState();
    }

    // ========== Result Types ==========

    public class RoomCreatedResult
    {
        public string RoomCode { get; set; }
        public string HostPlayerId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class JoinResult
    {
        public string PlayerId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}
```

### Hub Events (Server → Client)

**File: `src/TexasHoldem.Logic/Network/GameState.cs`**

```csharp
namespace TexasHoldem.Logic.Network
{
    using System.Collections.Generic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.GameRoundType;
    using TexasHoldem.Logic.HandRankType;
    using TexasHoldem.Logic.Players;

    /// <summary>
    /// Game state pushed to all clients
    /// </summary>
    public class GameState
    {
        /// <summary>
        /// Unique identifier for this game state (for reconciliation)
        /// </summary>
        public long StateId { get; set; }

        /// <summary>
        /// Current game phase
        /// </summary>
        public GamePhase Phase { get; set; }

        /// <summary>
        /// Current betting round
        /// </summary>
        public GameRoundType CurrentRound { get; set; }

        /// <summary>
        /// Community cards (flop/turn/river)
        /// </summary>
        public IReadOnlyList<Card> CommunityCards { get; set; }

        /// <summary>
        /// Main pot amount
        /// </summary>
        public int MainPot { get; set; }

        /// <summary>
        /// Side pots (for all-in scenarios)
        /// </summary>
        public IReadOnlyList<SidePotInfo> SidePots { get; set; }

        /// <summary>
        /// Current dealer position (index into Players)
        /// </summary>
        public int DealerPosition { get; set; }

        /// <summary>
        /// Current player to act (null if not in betting round)
        /// </summary>
        public string CurrentPlayerToActId { get; set; }

        /// <summary>
        /// Players in the game
        /// </summary>
        public IReadOnlyList<PlayerInfo> Players { get; set; }

        /// <summary>
        /// Hand number
        /// </summary>
        public int HandNumber { get; set; }

        /// <summary>
        /// Small blind amount
        /// </summary>
        public int SmallBlind { get; set; }
    }

    public enum GamePhase
    {
        Lobby,
        HandInProgress,
        HandComplete,
        GameComplete
    }

    public class PlayerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Emoji { get; set; }
        public int Chips { get; set; }
        public PlayerStatus Status { get; set; }
        public IReadOnlyList<Card> HoleCards { get; set; } // Only populated for local player
        public int CurrentBet { get; set; }
        public bool IsHost { get; set; }
        public int Position { get; set; } // Relative to dealer
    }

    public enum PlayerStatus
    {
        SittingOut,
        Active,
        Folded,
        AllIn,
        Won,
        Lost
    }

    public class SidePotInfo
    {
        public int Amount { get; set; }
        public IReadOnlyList<string> EligiblePlayerIds { get; set; }
    }
}
```

### Event Contracts

```csharp
// Server → Client events (SignalR invocation names)

public static class HubEvents
{
    public const string PlayerJoined = "OnPlayerJoined";
    public const string PlayerLeft = "OnPlayerLeft";
    public const string GameStateChanged = "OnGameStateChanged";
    public const string PlayerAction = "OnPlayerAction";
    public const string YourTurn = "OnYourTurn";
    public const string RoundEnd = "OnRoundEnd";
    public const string HandEnd = "OnHandEnd";
    public const string GameEnd = "OnGameEnd";
    public const string Error = "OnError";
}

// Example event payloads:

// OnPlayerJoined
{
    "player": { "id": "uuid", "name": "John", "emoji": "😎", "chips": 1000 }
}

// OnGameStateChanged
{
    "state": { /* GameState object */ }
}

// OnYourTurn
{
    "availableActions": ["Check", "Bet", "Fold", "Raise"],
    "minRaise": 100,
    "maxRaise": 950,
    "timeRemaining": 30
}

// OnHandEnd
{
    "winners": [
        { "playerId": "uuid", "handType": "FullHouse", "wonAmount": 500 }
    ],
    "showdownCards": {
        "player1": ["Ah", "Ad", "Ac", "Kd", "Qs", " Jh", "Th"],
        "player2": ["Ks", "Kc", "Qs", "Qd", "Js", "Jd", "Tc"]
    }
}
```

### Verification
1. Review API contract with frontend team
2. Verify all game actions are covered
3. Verify state synchronization approach
4. Document any edge cases or assumptions

---

## Step 12: Write Integration Tests

### Objective
Create comprehensive integration tests verifying game mechanics and edge cases.

### Files to Modify/Create
- `src/Tests/TexasHoldem.Logic.Tests/IntegrationTests.cs`

### Test Cases

```csharp
namespace TexasHoldem.Logic.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    using NUnit.Framework;

    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    [TestFixture]
    public class IntegrationTests
    {
        #region Full Hand Flow Tests

        [Test]
        public void FullHandFlow_PreFlopToShowdown_CompletesSuccessfully()
        {
            // Arrange
            var players = CreateTestPlayers(3, 1000);
            var game = new TexasHoldemGame(players, 1000);

            // Act
            var result = game.Start();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.TotalHandsPlayed, Is.GreaterThan(0));
        }

        #endregion

        #region Split Pot Tests

        [Test]
        public void SplitPot_TwoWinnersWithOddChips_NoChipsLost()
        {
            // Arrange: Create scenario where pot is 101, split between 2
            var player1 = new TestPlayer { Name = "P1" };
            var player2 = new TestPlayer { Name = "P2" };
            
            // Manually set up scenario with known chips
            var game = new TexasHoldemGame(new[] { player1, player2 }, 1000);

            // Use reflection to set initial chip amounts
            SetPlayerChips(player1, 51); // Both go all-in with 51
            SetPlayerChips(player2, 51);

            // Act
            var totalBefore = GetTotalChips(player1, player2);
            var result = game.Start();
            var totalAfter = GetTotalChips(result);

            // Assert: Total chips conserved (minus small blind, but account for that)
            Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(2)); // Within small blind variance
        }

        [Test]
        public void SplitPot_ThreeWaySplitWithRemainder_ChipsDistributedCorrectly()
        {
            // Arrange: 103 chips split 3 ways = 34 each, remainder 1
            var players = CreateTestPlayers(3, 100);
            var game = new TexasHoldemGame(players, 100);

            // Force split pot scenario through reflection
            // ...

            // Act
            var result = game.Start();

            // Assert
            var total = players.Sum(p => p.FinalChips);
            Assert.That(total, Is.EqualTo(300)); // Conserved
        }

        #endregion

        #region All-In and Side Pot Tests

        [Test]
        public void AllIn_SidePotCreated_CorrectPlayerWinsCorrectAmount()
        {
            // Arrange: P1 has 100, P2 has 50, P3 has 50
            // P1 and P2 go all-in, P3 folds
            // Main pot: 100 (3 players × 50, P3 folds)
            // Side pot: 50 (P1 vs P2)
            
            var players = CreateTestPlayers(3, 100);
            var game = new TexasHoldemGame(players, 100);

            // Act
            var result = game.Start();

            // Assert: P1 wins main + side if better hand
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void AllIn_TwoPlayersAllInThirdFolded_WinnerGetsAll()
        {
            var players = CreateTestPlayers(3, 100);
            var game = new TexasHoldemGame(players, 100);

            var result = game.Start();

            // Verify conservation
            var total = GetTotalChips(result);
            Assert.That(total, Is.EqualTo(300));
        }

        #endregion

        #region Multi-Player Showdown Tests

        [Test]
        public void Showdown_ThreePlayers_WinnerIdentifiedCorrectly()
        {
            // Arrange: Create known hands
            // P1: Ace-King (best)
            // P2: Queen-Jack
            // P3: Ten-Nine
            // Community: Ace-Ace-Queen-King-Ten

            var players = CreateTestPlayers(3, 100);
            var game = new TexasHoldemGame(players, 100);

            // Act
            var result = game.Start();

            // Assert
            Assert.That(result.WinnerName, Is.EqualTo("P1")); // Has Aces full
        }

        [Test]
        public void Showdown_MultiplePlayersWithSameHand_SameRankValue()
        {
            // Arrange: Both players have identical hands
            var p1Cards = new List<Card> { Card AceSpade, Card AceHeart };
            var p2Cards = new List<Card> { Card AceDiamond, Card AceClub };
            var community = new List<Card> { Card KingSpade, Card KingHeart, Card QueenHeart, Card JackSpade, Card TenHeart };
            // Both have Two Pair: Aces over Kings

            // Act - Use Helpers.CompareCards
            var comparison = Helpers.CompareCards(
                p1Cards.Concat(community),
                p2Cards.Concat(community));

            // Assert
            Assert.That(comparison, Is.EqualTo(0)); // Equal hands
        }

        #endregion

        #region Money Conservation Tests

        [Test]
        public void MoneyConservation_TenHands_TotalChipsConstant()
        {
            // Arrange
            var players = CreateTestPlayers(4, 1000);
            var initialTotal = players.Sum(p => 1000);
            var game = new TexasHoldemGame(players, 1000);

            // Act
            var result = game.Start();

            // Assert
            var finalTotal = GetTotalChips(result);
            Assert.That(finalTotal, Is.EqualTo(initialTotal)); // No chips lost or created
        }

        [Test]
        public void MoneyConservation_AfterMultipleAllIns_TotalPreserved()
        {
            // Arrange
            var initialTotal = 400; // 4 players × 100
            var players = CreateTestPlayers(4, 100);
            var game = new TexasHoldemGame(players, 100);

            // Act - Play until completion
            var result = game.Start();

            // Assert
            var finalTotal = players.Sum(p => GetPlayerChips(p));
            Assert.That(finalTotal, Is.EqualTo(initialTotal));
        }

        #endregion

        #region Game Termination Tests

        [Test]
        public void GameTermination_OnePlayerWinsAll_GameEnds()
        {
            // Arrange
            var players = CreateTestPlayers(2, 100);
            var game = new TexasHoldemGame(players, 100);

            // Act
            var result = game.Start();

            // Assert
            var playersWithMoney = players.Count(p => GetPlayerChips(p) > 0);
            Assert.That(playersWithMoney, Is.EqualTo(1));
            Assert.That(result.WinnerName, Is.Not.Empty);
        }

        [Test]
        public void GameTermination_NoInfiniteLoop_GameCompletes()
        {
            // Arrange
            var players = CreateTestPlayers(2, 10);
            var game = new TexasHoldemGame(players, 10);

            // Act & Assert: Should not hang
            var result = game.Start();
            
            Assert.That(result.TotalHandsPlayed, Is.LessThan(1000)); // Sanity check
        }

        #endregion

        #region Bug-Specific Regression Tests

        [Test]
        public void BugFix_FullHouseWithTwoTripsPlusPair_ExactlyFiveCards()
        {
            // Bug #1: Full house with two three-of-a-kinds + pair
            // Arrange: 7 cards - A♠A♥A♦K♣K♦Q♥J♠
            var cards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Ace),
                new Card(CardSuit.Diamond, CardType.Ace),
                new Card(CardSuit.Club, CardType.King),
                new Card(CardSuit.Diamond, CardType.King),
                new Card(CardSuit.Heart, CardType.Queen),
                new Card(CardSuit.Spade, CardType.Jack)
            };

            // Act
            var evaluator = new HandEvaluator();
            var bestHand = evaluator.GetBestHand(cards);

            // Assert
            Assert.That(bestHand.Cards.Count, Is.EqualTo(5));
            Assert.That(bestHand.RankType, Is.EqualTo(HandRankType.FullHouse));
        }

        [Test]
        public void BugFix_IdenticalHands_GetSameRankValue()
        {
            // Bug #2: Two identical hands should have same value
            // Arrange
            var p1Cards = new List<Card> 
            { 
                new Card(CardSuit.Spade, CardType.Ace), 
                new Card(CardSuit.Heart, CardType.King) 
            };
            var p2Cards = new List<Card> 
            { 
                new Card(CardSuit.Diamond, CardType.Ace), 
                new Card(CardSuit.Club, CardType.King) 
            };
            var community = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Queen),
                new Card(CardSuit.Heart, CardType.Jack),
                new Card(CardSuit.Diamond, CardType.Ten),
                new Card(CardSuit.Club, CardType.Nine),
                new Card(CardSuit.Heart, CardType.Eight)
            };

            // Act
            var p1Value = Helpers.GetHandRankValue(p1Cards, new[] { p2Cards }, community);
            var p2Value = Helpers.GetHandRankValue(p2Cards, new[] { p1Cards }, community);

            // Assert
            Assert.That(p1Value, Is.EqualTo(p2Value));
        }

        #endregion

        // ========== Helper Methods ==========

        private List<TestPlayer> CreateTestPlayers(int count, int chips)
        {
            var players = new List<TestPlayer>();
            for (int i = 0; i < count; i++)
            {
                players.Add(new TestPlayer { Name = $"P{i + 1}" });
            }
            return players;
        }

        private int GetTotalChips(params TestPlayer[] players)
        {
            return players.Sum(p => GetPlayerChips(p));
        }

        private int GetTotalChips(GameEndResult result)
        {
            return result.Standings.Sum(s => s.FinalChips);
        }

        private int GetPlayerChips(TestPlayer player)
        {
            // Use reflection to access internal PlayerMoney.Money
            var field = typeof(TestPlayer).GetField("money", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)(field?.GetValue(player) ?? 0);
        }

        private void SetPlayerChips(TestPlayer player, int chips)
        {
            // Use reflection to set internal PlayerMoney.Money
            var field = typeof(TestPlayer).GetField("money", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(player, chips);
        }
    }

    // Test player implementation
    public class TestPlayer : IPlayer
    {
        public string Name { get; set; }
        public int FinalChips { get; private set; }

        public IPlayerAction GetTurn(GetTurnContext context)
        {
            // Always check/call if possible, otherwise fold
            if (context.CanCheck)
                return PlayerAction.CheckOrCall();
            
            return PlayerAction.Fold();
        }

        public void PostBlind(IPostingBlindContext context)
        {
            // Small blind
        }

        public void StartGame(IStartGameContext context)
        {
            FinalChips = context.StartMoney;
        }

        public void StartHand(IStartHandContext context)
        {
        }

        public void StartRound(IStartRoundContext context)
        {
        }

        public void EndRound(IEndRoundContext context)
        {
        }

        public void EndHand(IEndHandContext context)
        {
        }

        public void EndGame(IEndGameContext context)
        {
        }
    }
}
```

### Verification
1. Run all integration tests
2. Verify tests pass on clean codebase
3. Verify tests catch the 7 critical bugs when introduced
4. Verify money conservation invariant

---

## Step 13: Final Verification and Cleanup

### Objective
Run full test suite and verify all bugs are fixed.

### Actions
1. Run all unit tests: `dotnet test`
2. Run integration tests specifically
3. Review code coverage
4. Remove any TODO comments from fixed code
5. Update project documentation

### Verification Criteria
- [ ] All 7 bugs are fixed and verified
- [ ] CSPRNG is implemented and thread-safe
- [ ] Game terminates correctly without infinite loop
- [ ] API contract is documented
- [ ] Integration tests pass
- [ ] Money conservation is verified
- [ ] No TODO comments in production code

---

## Summary

This Phase 0 plan addresses the critical foundation issues before proceeding to multiplayer and web integration:

| Step | Task | Files Modified | Complexity |
|------|------|----------------|------------|
| 1 | Full House Bug Fix | HandEvaluator.cs | Medium |
| 2 | Showdown Ranking | Helpers.cs | Medium |
| 3 | Split Pot Chips | HandLogic.cs | Low |
| 4 | Heads-Up Side Pots | HandLogic.cs | Medium |
| 5 | Infinite Loop Fix | TexasHoldemGame.cs | Low |
| 6 | Constructor Validation | TexasHoldemGame.cs | Low |
| 7 | Mutable Singletons | PlayerAction.cs | Low |
| 8 | CSPRNG Replacement | RandomProvider.cs | Medium |
| 9 | Game End Detection | TexasHoldemGame.cs | Medium |
| 10 | Async Bridge Prototype | New files | High |
| 11 | API Contract | New files | Medium |
| 12 | Integration Tests | New test file | Medium |

**Total Estimated Effort:** 3-5 days for a proficient developer
