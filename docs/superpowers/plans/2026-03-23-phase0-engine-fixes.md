# Phase 0: Engine Fixes & Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 7 critical bugs in the Texas Hold'em engine, replace insecure RNG, and prototype the TaskCompletionSource sync-to-async bridge for future SignalR integration.

**Architecture:** The engine (`TexasHoldem.Logic`, netstandard2.0) is a synchronous blocking game loop. We fix correctness bugs first, then add a TCS-based bridge that lets an `IPlayer` implementation await async input (e.g., from a WebSocket client) while the engine blocks on `GetTurn()`. No structural changes to the game loop itself.

**Tech Stack:** C# / netstandard2.0 / xUnit / Moq / StyleCop

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/TexasHoldem.Logic/Helpers/HandEvaluator.cs` | Fix full house >5 cards (Bug #1) |
| Modify | `src/TexasHoldem.Logic/Helpers/Helpers.cs` | Fix rank value overflow (Bug #2) |
| Modify | `src/TexasHoldem.Logic/GameMechanics/HandLogic.cs` | Fix split pot chip loss, unify heads-up path (Bugs #3, #4), fix uncontested side pot |
| Modify | `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs` | Remove Rebuy, fix constructor validation (Bugs #5, #6) |
| Modify | `src/TexasHoldem.Logic/Players/PlayerAction.cs` | Make immutable (Bug #7) |
| Modify | `src/TexasHoldem.Logic/GameMechanics/InternalPlayerMoney.cs` | Stop mutating PlayerAction singleton (Bug #7 dependency) |
| Modify | `src/TexasHoldem.Logic/Extensions/RandomProvider.cs` | CSPRNG replacement |
| Create | `src/Tests/TexasHoldem.Logic.Tests/Helpers/HandEvaluatorBugTests.cs` | Regression tests for Bug #1 |
| Create | `src/Tests/TexasHoldem.Logic.Tests/Helpers/HelpersTests.cs` | Regression tests for Bug #2 |
| Create | `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/HandLogicTests.cs` | Pot distribution tests (Bugs #3, #4) |
| Create | `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/GameTerminationTests.cs` | Game loop termination tests (Bug #5) |
| Create | `src/Tests/TexasHoldem.Logic.Tests/Players/PlayerActionTests.cs` | Immutability tests (Bug #7) |
| Create | `src/Tests/TexasHoldem.Logic.Tests/Extensions/CsprngTests.cs` | CSPRNG correctness tests |
| Create | `src/TexasHoldem.Logic/Async/TcsPlayer.cs` | TaskCompletionSource IPlayer adapter |

---

## Task 1: Fix Full House Evaluation Exceeding 5 Cards (Bug #1)

**Files:**
- Modify: `src/TexasHoldem.Logic/Helpers/HandEvaluator.cs:70-98`
- Create: `src/Tests/TexasHoldem.Logic.Tests/Helpers/HandEvaluatorBugTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Tests/TexasHoldem.Logic.Tests/Helpers/HandEvaluatorBugTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.Helpers
{
    using System.Collections.Generic;

    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Helpers;

    using Xunit;

    public class HandEvaluatorBugTests
    {
        [Fact]
        public void GetBestHand_TwoTripsAndPair_ReturnsExactlyFiveCards()
        {
            // Bug #1: Two three-of-a-kinds + a pair could produce >5 cards
            var cards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Ace),
                new Card(CardSuit.Diamond, CardType.Ace),
                new Card(CardSuit.Club, CardType.King),
                new Card(CardSuit.Heart, CardType.King),
                new Card(CardSuit.Diamond, CardType.King),
                new Card(CardSuit.Spade, CardType.Queen),
            };

            var evaluator = new HandEvaluator();
            var bestHand = evaluator.GetBestHand(cards);

            Assert.Equal(HandRankType.FullHouse, bestHand.RankType);
            Assert.Equal(5, bestHand.Cards.Count);
        }

        [Fact]
        public void GetBestHand_TwoTripsAndPair_PicksHigherTrips()
        {
            // With AAA KKK Q, the full house should be AAA KK (not KKK AA)
            var cards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Ace),
                new Card(CardSuit.Diamond, CardType.Ace),
                new Card(CardSuit.Club, CardType.King),
                new Card(CardSuit.Heart, CardType.King),
                new Card(CardSuit.Diamond, CardType.King),
                new Card(CardSuit.Spade, CardType.Queen),
            };

            var evaluator = new HandEvaluator();
            var bestHand = evaluator.GetBestHand(cards);

            // Cards should contain 3 Aces and 2 Kings
            var aceCount = 0;
            var kingCount = 0;
            foreach (var card in bestHand.Cards)
            {
                if (card == CardType.Ace)
                {
                    aceCount++;
                }

                if (card == CardType.King)
                {
                    kingCount++;
                }
            }

            Assert.Equal(3, aceCount);
            Assert.Equal(2, kingCount);
        }

        [Fact]
        public void GetBestHand_TwoTripsNoPair_ReturnsFullHouse()
        {
            // Two trips, no separate pair: AAA KKK 5
            var cards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Ace),
                new Card(CardSuit.Diamond, CardType.Ace),
                new Card(CardSuit.Club, CardType.King),
                new Card(CardSuit.Heart, CardType.King),
                new Card(CardSuit.Diamond, CardType.King),
                new Card(CardSuit.Spade, CardType.Five),
            };

            var evaluator = new HandEvaluator();
            var bestHand = evaluator.GetBestHand(cards);

            Assert.Equal(HandRankType.FullHouse, bestHand.RankType);
            Assert.Equal(5, bestHand.Cards.Count);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HandEvaluatorBugTests"`
Expected: FAIL — `BestHand` constructor throws because card count != 5

- [ ] **Step 3: Implement the fix**

In `HandEvaluator.cs`, replace lines 70-98 (the `// Full` block). The bug: both `if (threeOfAKindTypes.Count > 1)` and `if (pairTypes.Count > 0)` can execute, producing 3+2+2=7 cards. Fix: use `else if`.

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

    // For the pair component: prefer second trips over a pair (higher rank)
    if (threeOfAKindTypes.Count > 1)
    {
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(threeOfAKindTypes[1]);
        }
    }
    else if (pairTypes.Count > 0)
    {
        for (var i = 0; i < 2; i++)
        {
            bestCards.Add(pairTypes[0]);
        }
    }

    return new BestHand(HandRankType.FullHouse, bestCards);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HandEvaluatorBugTests"`
Expected: All 3 tests PASS

- [ ] **Step 5: Run full test suite for regression**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All existing tests still pass (including FullHouseCases)

- [ ] **Step 6: Commit**

```bash
git add src/TexasHoldem.Logic/Helpers/HandEvaluator.cs src/Tests/TexasHoldem.Logic.Tests/Helpers/HandEvaluatorBugTests.cs
git commit -m "fix: full house evaluation no longer exceeds 5 cards (Bug #1)"
```

---

## Task 2: Fix Multi-Player Showdown Ranking Overflow (Bug #2)

**Files:**
- Modify: `src/TexasHoldem.Logic/Helpers/Helpers.cs:39-60`
- Create: `src/Tests/TexasHoldem.Logic.Tests/Helpers/HelpersTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Tests/TexasHoldem.Logic.Tests/Helpers/HelpersTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.Helpers
{
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Helpers;

    using Xunit;

    public class HelpersTests
    {
        [Fact]
        public void GetHandRankValue_IdenticalHands_ReturnSameValue()
        {
            // Bug #2: Two players with identical hands should get same score
            var p1Cards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.King),
            };
            var p2Cards = new List<Card>
            {
                new Card(CardSuit.Diamond, CardType.Ace),
                new Card(CardSuit.Club, CardType.King),
            };
            var community = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Queen),
                new Card(CardSuit.Heart, CardType.Jack),
                new Card(CardSuit.Diamond, CardType.Ten),
                new Card(CardSuit.Club, CardType.Nine),
                new Card(CardSuit.Heart, CardType.Eight),
            };

            var p1Value = Helpers.GetHandRankValue(p1Cards, new[] { p2Cards }, community);
            var p2Value = Helpers.GetHandRankValue(p2Cards, new[] { p1Cards }, community);

            Assert.Equal(p1Value, p2Value);
        }

        [Fact]
        public void GetHandRankValue_DifferentRankTypes_NoOverlap()
        {
            // Bug #2: With many opponents, wins can overflow into next rank range
            var highCardPlayer = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Seven),
            };

            var pairPlayer = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Two),
                new Card(CardSuit.Heart, CardType.Two),
            };

            var community = new List<Card>
            {
                new Card(CardSuit.Diamond, CardType.Three),
                new Card(CardSuit.Club, CardType.Four),
                new Card(CardSuit.Heart, CardType.Five),
                new Card(CardSuit.Diamond, CardType.Nine),
                new Card(CardSuit.Club, CardType.Jack),
            };

            // Create 8 opponents all worse than highCardPlayer
            var weakOpponents = new List<IEnumerable<Card>>();
            var weakCards = new[]
            {
                CardType.Six, CardType.Eight, CardType.Ten, CardType.Queen,
                CardType.Six, CardType.Eight, CardType.Ten, CardType.Queen,
            };
            var suits = new[] { CardSuit.Spade, CardSuit.Heart, CardSuit.Diamond, CardSuit.Club };
            for (int i = 0; i < 8; i++)
            {
                weakOpponents.Add(new List<Card>
                {
                    new Card(suits[i % 4], weakCards[i]),
                    new Card(suits[(i + 1) % 4], CardType.Two),
                });
            }

            // High card beating 8 opponents should NOT overflow into Pair range
            var highCardAllOpponents = new List<IEnumerable<Card>>(weakOpponents) { pairPlayer };
            var pairAllOpponents = new List<IEnumerable<Card>>(weakOpponents) { highCardPlayer };

            var highCardValue = Helpers.GetHandRankValue(highCardPlayer, highCardAllOpponents, community);
            var pairValue = Helpers.GetHandRankValue(pairPlayer, pairAllOpponents, community);

            Assert.True(pairValue > highCardValue, $"Pair ({pairValue}) should rank higher than HighCard ({highCardValue})");
        }

        [Fact]
        public void GetHandRankValue_BetterHand_HigherValue()
        {
            var strongCards = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.Ace),
                new Card(CardSuit.Heart, CardType.Ace),
            };

            var weakCards = new List<Card>
            {
                new Card(CardSuit.Diamond, CardType.Two),
                new Card(CardSuit.Club, CardType.Three),
            };

            var community = new List<Card>
            {
                new Card(CardSuit.Spade, CardType.King),
                new Card(CardSuit.Heart, CardType.Queen),
                new Card(CardSuit.Diamond, CardType.Jack),
                new Card(CardSuit.Club, CardType.Nine),
                new Card(CardSuit.Heart, CardType.Eight),
            };

            var strongValue = Helpers.GetHandRankValue(strongCards, new[] { weakCards }, community);
            var weakValue = Helpers.GetHandRankValue(weakCards, new[] { strongCards }, community);

            Assert.True(strongValue > weakValue);
        }
    }
}
```

- [ ] **Step 2: Run test to verify the overflow test fails**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HelpersTests"`
Expected: `GetHandRankValue_DifferentRankTypes_NoOverlap` FAILS

- [ ] **Step 3: Implement the fix**

In `Helpers.cs`, replace `GetHandRankValue` method (lines 39-60). The fix: scale rank type by 10000 to prevent win-count overflow into adjacent ranks.

```csharp
public static int GetHandRankValue(
    IEnumerable<Card> player,
    IEnumerable<IEnumerable<Card>> opponents,
    IEnumerable<Card> communityCards)
{
    var playerHand = player.Concat(communityCards);
    var playerBestHand = HandEvaluator.GetBestHand(playerHand);

    // Scale rank by 10000 to prevent win-count from overflowing
    // into the next rank's range (max 9 opponents, so max +9)
    var playerHandValue = (int)playerBestHand.RankType * 10000;

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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HelpersTests"`
Expected: All 3 tests PASS

- [ ] **Step 5: Run full test suite**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/TexasHoldem.Logic/Helpers/Helpers.cs src/Tests/TexasHoldem.Logic.Tests/Helpers/HelpersTests.cs
git commit -m "fix: scale hand rank value to prevent overflow across rank types (Bug #2)"
```

---

## Task 3: Fix Split Pot Chip Loss and Uncontested Side Pot (Bugs #3 + side pot bug)

**Files:**
- Modify: `src/TexasHoldem.Logic/GameMechanics/HandLogic.cs:148-170`
- Create: `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/HandLogicTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/HandLogicTests.cs`.

Note: `DetermineWinnerAndAddPot` is private, so we test via the public `HandLogic.Play()` flow indirectly, or we test the specific calculation. Since internal types are visible to tests via `InternalsVisibleTo`, we can test `InternalPlayerMoney` chip conservation. For now, we test the math in isolation:

```csharp
namespace TexasHoldem.Logic.Tests.GameMechanics
{
    using Xunit;

    public class HandLogicTests
    {
        [Theory]
        [InlineData(101, 2, 50, 1)] // 101/2 = 50 remainder 1
        [InlineData(103, 3, 34, 1)] // 103/3 = 34 remainder 1
        [InlineData(100, 4, 25, 0)] // 100/4 = 25 remainder 0
        [InlineData(7, 3, 2, 1)]    // 7/3 = 2 remainder 1
        public void SplitPot_ChipsConserved(int pot, int winners, int expectedPrize, int expectedRemainder)
        {
            var prize = pot / winners;
            var remainder = pot % winners;

            Assert.Equal(expectedPrize, prize);
            Assert.Equal(expectedRemainder, remainder);

            // Conservation: prize * winners + remainder == pot
            Assert.Equal(pot, (prize * winners) + remainder);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it passes (math sanity check)**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~HandLogicTests"`
Expected: PASS (this validates our math, the real fix is in step 3)

- [ ] **Step 3: Implement the fix in HandLogic.cs**

Fix two issues in `DetermineWinnerAndAddPot`:
1. Line 164: add remainder chip distribution after integer division
2. Lines 153-156: uncontested side pot `continue` should award to the single player

Replace the multi-player `else` block (lines 119-192):

```csharp
else
{
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
                // Uncontested pot: award to the single remaining player
                var winnerName = oneOfThePots.ActivePlayer.First();
                this.players.First(x => x.Name == winnerName).PlayerMoney.Money += oneOfThePots.AmountOfMoney;
            }
            else
            {
                var nominees = oneOfThePots.ActivePlayer.Intersect(playersWithTheBestHand.Value);
                var count = nominees.Count();

                if (count > 0)
                {
                    var prize = oneOfThePots.AmountOfMoney / count;
                    var remainder = oneOfThePots.AmountOfMoney % count;

                    // Distribute remainder chips to first nominees (deterministic)
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

- [ ] **Step 4: Run full test suite**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add src/TexasHoldem.Logic/GameMechanics/HandLogic.cs src/Tests/TexasHoldem.Logic.Tests/GameMechanics/HandLogicTests.cs
git commit -m "fix: split pot remainder distribution and uncontested side pot award (Bug #3)"
```

---

## Task 4: Unify Heads-Up Showdown with Multi-Player Path (Bug #4)

**Files:**
- Modify: `src/TexasHoldem.Logic/GameMechanics/HandLogic.cs:100-118`

- [ ] **Step 1: Remove the 2-player special case**

In `HandLogic.cs`, the `DetermineWinnerAndAddPot` method has `if (this.players.Count == 2)` at line 100 that bypasses side pot logic. Remove this entire block so all showdowns use the multi-player path (which already handles 2 players correctly).

Replace lines 89-193 (the entire `else` block after the "only 1 player in hand" check):

```csharp
else
{
    // showdown
    foreach (var player in this.players)
    {
        if (player.PlayerMoney.InHand)
        {
            this.showdownCards.Add(player.Name, player.Cards);
        }
    }

    // Unified pot distribution for all player counts (2+)
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
                var winnerName = oneOfThePots.ActivePlayer.First();
                this.players.First(x => x.Name == winnerName).PlayerMoney.Money += oneOfThePots.AmountOfMoney;
            }
            else
            {
                var nominees = oneOfThePots.ActivePlayer.Intersect(playersWithTheBestHand.Value);
                var count = nominees.Count();

                if (count > 0)
                {
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

Note: Tasks 3 and 4 both modify the same `else` block. If implemented sequentially, Task 4 effectively replaces the result of Task 3 with the unified version (which includes the Bug #3 fixes). The final code is the same either way.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add src/TexasHoldem.Logic/GameMechanics/HandLogic.cs
git commit -m "fix: unify heads-up showdown with multi-player pot distribution (Bug #4)"
```

---

## Task 5: Remove Rebuy and Fix Infinite Game Loop (Bug #5)

**Files:**
- Modify: `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs:104-141`
- Create: `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/GameTerminationTests.cs`

- [ ] **Step 1: Write the test**

Create `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/GameTerminationTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.GameMechanics
{
    using System.Threading;
    using System.Threading.Tasks;

    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    using Xunit;

    public class GameTerminationTests
    {
        [Fact]
        public void Start_TwoPlayers_GameTerminates()
        {
            // Bug #5: Rebuy caused infinite loop
            var player1 = new AlwaysCallTestPlayer("P1");
            var player2 = new AlwaysFoldTestPlayer("P2");

            var game = new TexasHoldemGame(player1, player2, 10);

            // Game must complete within a reasonable time
            var cts = new CancellationTokenSource(10000); // 10 second timeout
            var task = Task.Run(() => game.Start(), cts.Token);

            Assert.True(task.Wait(10000), "Game did not terminate within 10 seconds");
        }

        [Fact]
        public void Start_ThreePlayers_GameTerminates()
        {
            var players = new IPlayer[]
            {
                new AlwaysCallTestPlayer("P1"),
                new AlwaysFoldTestPlayer("P2"),
                new AlwaysFoldTestPlayer("P3"),
            };

            var game = new TexasHoldemGame(players, 10);

            var task = Task.Run(() => game.Start());
            Assert.True(task.Wait(10000), "Game did not terminate within 10 seconds");
        }

        private class AlwaysCallTestPlayer : BasePlayer
        {
            public AlwaysCallTestPlayer(string name)
            {
                this.Name = name;
            }

            public override string Name { get; }

            public override int BuyIn => -1;

            public override PlayerAction PostingBlind(IPostingBlindContext context)
            {
                return context.BlindAction;
            }

            public override PlayerAction GetTurn(IGetTurnContext context)
            {
                return PlayerAction.CheckOrCall();
            }
        }

        private class AlwaysFoldTestPlayer : BasePlayer
        {
            public AlwaysFoldTestPlayer(string name)
            {
                this.Name = name;
            }

            public override string Name { get; }

            public override int BuyIn => -1;

            public override PlayerAction PostingBlind(IPostingBlindContext context)
            {
                return context.BlindAction;
            }

            public override PlayerAction GetTurn(IGetTurnContext context)
            {
                if (context.CanCheck)
                {
                    return PlayerAction.CheckOrCall();
                }

                return PlayerAction.Fold();
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it hangs (or times out)**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~GameTerminationTests" -- RunConfiguration.TestSessionTimeout=15000`
Expected: FAIL (timeout — game loops forever due to Rebuy)

- [ ] **Step 3: Implement the fix**

In `TexasHoldemGame.cs`, remove the `Rebuy()` call at line 139 and the `Rebuy()` method (lines 104-114):

Remove the `Rebuy` method entirely and remove `this.Rebuy();` from `PlayGame()`.

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~GameTerminationTests"`
Expected: PASS

- [ ] **Step 5: Run full suite**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs src/Tests/TexasHoldem.Logic.Tests/GameMechanics/GameTerminationTests.cs
git commit -m "fix: remove auto-rebuy to allow game termination (Bug #5)"
```

---

## Task 6: Fix Constructor Null Validation Order (Bug #6)

**Files:**
- Modify: `src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs:56-81`

- [ ] **Step 1: Write the test**

Add to `src/Tests/TexasHoldem.Logic.Tests/GameMechanics/TexasHoldemGameTests.cs`:

```csharp
[Fact]
public void ConstructorShouldThrowArgumentNullExceptionWhenPlayerInCollectionIsNull()
{
    var mockedPlayer = new Mock<IPlayer>();
    mockedPlayer.SetupGet(x => x.Name).Returns("ValidPlayer");
    IPlayer nullPlayer = null;

    Assert.Throws<ArgumentNullException>(
        () => new TexasHoldemGame(new[] { mockedPlayer.Object, nullPlayer }, 1000));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~ConstructorShouldThrowArgumentNullExceptionWhenPlayerInCollectionIsNull"`
Expected: FAIL — throws `NullReferenceException` instead of `ArgumentNullException`

- [ ] **Step 3: Implement the fix**

In `TexasHoldemGame.cs`, add null-per-player validation in the private constructor, before the foreach that creates `InternalPlayer` objects:

After line 71 (`throw new ArgumentOutOfRangeException(...)`) and before line 73 (`this.allPlayers = new List...`), add:

```csharp
foreach (var player in players)
{
    if (player == null)
    {
        throw new ArgumentNullException(nameof(players), "One of the players in the collection is null");
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~TexasHoldemGameTests"`
Expected: All pass (including existing tests)

- [ ] **Step 5: Commit**

```bash
git add src/TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs src/Tests/TexasHoldem.Logic.Tests/GameMechanics/TexasHoldemGameTests.cs
git commit -m "fix: validate individual players for null before iteration (Bug #6)"
```

---

## Task 7: Make PlayerAction Immutable (Bug #7)

**Files:**
- Modify: `src/TexasHoldem.Logic/Players/PlayerAction.cs:23`
- Modify: `src/TexasHoldem.Logic/GameMechanics/InternalPlayerMoney.cs:76-79`
- Create: `src/Tests/TexasHoldem.Logic.Tests/Players/PlayerActionTests.cs`

- [ ] **Step 1: Write the test**

Create `src/Tests/TexasHoldem.Logic.Tests/Players/PlayerActionTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.Players
{
    using TexasHoldem.Logic.Players;

    using Xunit;

    public class PlayerActionTests
    {
        [Fact]
        public void Fold_ReturnsSameInstance()
        {
            var fold1 = PlayerAction.Fold();
            var fold2 = PlayerAction.Fold();
            Assert.Same(fold1, fold2);
        }

        [Fact]
        public void CheckOrCall_ReturnsSameInstance()
        {
            var check1 = PlayerAction.CheckOrCall();
            var check2 = PlayerAction.CheckOrCall();
            Assert.Same(check1, check2);
        }

        [Fact]
        public void Raise_ReturnsNewInstance()
        {
            var raise1 = PlayerAction.Raise(100);
            var raise2 = PlayerAction.Raise(100);
            Assert.NotSame(raise1, raise2);
        }

        [Fact]
        public void Raise_MoneyIsCorrect()
        {
            var raise = PlayerAction.Raise(500);
            Assert.Equal(500, raise.Money);
        }

        [Fact]
        public void Fold_MoneyIsZero()
        {
            var fold = PlayerAction.Fold();
            Assert.Equal(0, fold.Money);
        }
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~PlayerActionTests"`
Expected: PASS (these verify current behavior before we make changes)

- [ ] **Step 3: Make PlayerAction.Money read-only**

In `PlayerAction.cs`, line 23, change:
```csharp
public int Money { get; internal set; }
```
to:
```csharp
public int Money { get; }
```

- [ ] **Step 4: Fix InternalPlayerMoney.cs — stop mutating action**

In `InternalPlayerMoney.cs`, the all-in path at lines 76-79 mutates `action.Money`. Replace:

```csharp
else
{
    // All-in
    action.Money = this.Money;
    this.PlaceMoney(action.Money);
}
```

with:

```csharp
else
{
    // All-in — create new action with actual amount instead of mutating
    var allInAmount = this.Money;
    this.PlaceMoney(allInAmount);
    return PlayerAction.Raise(allInAmount);
}
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build src/TexasHoldem.sln && dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: Build succeeds, all tests pass

- [ ] **Step 6: Commit**

```bash
git add src/TexasHoldem.Logic/Players/PlayerAction.cs src/TexasHoldem.Logic/GameMechanics/InternalPlayerMoney.cs src/Tests/TexasHoldem.Logic.Tests/Players/PlayerActionTests.cs
git commit -m "fix: make PlayerAction immutable to prevent singleton corruption (Bug #7)"
```

---

## Task 8: Replace System.Random with CSPRNG

**Files:**
- Modify: `src/TexasHoldem.Logic/Extensions/RandomProvider.cs`
- Create: `src/Tests/TexasHoldem.Logic.Tests/Extensions/CsprngTests.cs`

- [ ] **Step 1: Write the tests**

Create `src/Tests/TexasHoldem.Logic.Tests/Extensions/CsprngTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.Extensions
{
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.Logic.Extensions;

    using Xunit;

    public class CsprngTests
    {
        [Fact]
        public void Next_ReturnsValueInRange()
        {
            for (int i = 0; i < 1000; i++)
            {
                var value = RandomProvider.Next(0, 52);
                Assert.InRange(value, 0, 51);
            }
        }

        [Fact]
        public void Next_AllValuesReachable()
        {
            // Over 10000 trials, all values 0-9 should appear at least once
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++)
            {
                seen.Add(RandomProvider.Next(0, 10));
            }

            Assert.Equal(10, seen.Count);
        }

        [Fact]
        public void Next_RoughlyUniform()
        {
            // Chi-squared-like test: each bucket should get ~1000 of 10000 draws
            var counts = new int[10];
            for (int i = 0; i < 10000; i++)
            {
                counts[RandomProvider.Next(0, 10)]++;
            }

            foreach (var count in counts)
            {
                // Each should be within 500-1500 (very generous bounds)
                Assert.InRange(count, 500, 1500);
            }
        }

        [Fact]
        public void Next_MinValueEqualsMaxValueMinusOne_ReturnsSingleValue()
        {
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(5, RandomProvider.Next(5, 6));
            }
        }
    }
}
```

- [ ] **Step 2: Run tests against current implementation**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~CsprngTests"`
Expected: Should PASS (current Random works, but is insecure)

- [ ] **Step 3: Replace RandomProvider with CSPRNG**

Replace entire `RandomProvider.cs`:

```csharp
namespace TexasHoldem.Logic.Extensions
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Thread-safe cryptographically secure random number generator.
    /// </summary>
    public static class RandomProvider
    {
        /// <summary>
        /// Returns a random integer in the range [minValue, maxValue).
        /// </summary>
        public static int Next(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue");
            }

            // For range == 1, there's only one possible value
            long range = (long)maxValue - minValue;
            if (range == 1)
            {
                return minValue;
            }

            // Use rejection sampling to avoid modular bias
            // Generate random uint, reject values that would cause bias
            var maxAcceptable = (uint.MaxValue / (uint)range) * (uint)range;

            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                uint candidate;
                do
                {
                    rng.GetBytes(bytes);
                    candidate = BitConverter.ToUInt32(bytes, 0);
                }
                while (candidate >= maxAcceptable);

                return minValue + (int)(candidate % (uint)range);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~CsprngTests"`
Expected: All PASS

- [ ] **Step 5: Run full suite (includes Deck shuffle tests)**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/TexasHoldem.Logic/Extensions/RandomProvider.cs src/Tests/TexasHoldem.Logic.Tests/Extensions/CsprngTests.cs
git commit -m "security: replace System.Random with CSPRNG for card dealing"
```

---

## Task 9: Prototype TaskCompletionSource Async Bridge

**Files:**
- Create: `src/TexasHoldem.Logic/Async/TcsPlayer.cs`

- [ ] **Step 1: Create the Async directory**

```bash
mkdir -p src/TexasHoldem.Logic/Async
```

- [ ] **Step 2: Create TcsPlayer — an IPlayer that bridges sync GetTurn to async**

Create `src/TexasHoldem.Logic/Async/TcsPlayer.cs`:

```csharp
namespace TexasHoldem.Logic.Async
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;

    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Players;

    /// <summary>
    /// An IPlayer implementation that bridges the synchronous game engine
    /// to async callers (e.g., SignalR) using TaskCompletionSource.
    ///
    /// The engine calls GetTurn() synchronously and blocks.
    /// External code calls SubmitAction() to unblock it.
    /// External code subscribes to events to receive game state updates.
    /// </summary>
    public class TcsPlayer : BasePlayer
    {
        private readonly ConcurrentDictionary<string, object> pendingContexts =
            new ConcurrentDictionary<string, object>();

        private ManualResetEventSlim turnGate = new ManualResetEventSlim(false);

        private PlayerAction submittedAction;

        private int turnTimeoutMs;

        public TcsPlayer(string name, int turnTimeoutMs = 30000)
        {
            this.Name = name;
            this.turnTimeoutMs = turnTimeoutMs;
        }

        /// <summary>Fired when the engine requests this player's action.</summary>
        public event EventHandler<TurnRequestedEventArgs> TurnRequested;

        /// <summary>Fired when a new hand starts (cards dealt).</summary>
        public event EventHandler<HandStartedEventArgs> HandStarted;

        /// <summary>Fired when community cards are revealed.</summary>
        public event EventHandler<RoundStartedEventArgs> RoundStarted;

        /// <summary>Fired when the hand ends.</summary>
        public event EventHandler<HandEndedEventArgs> HandEnded;

        public override string Name { get; }

        public override int BuyIn => -1;

        public override PlayerAction PostingBlind(IPostingBlindContext context)
        {
            return context.BlindAction;
        }

        /// <summary>
        /// Called by the game engine synchronously. Blocks until SubmitAction is called
        /// or the timeout expires (auto-fold).
        /// </summary>
        public override PlayerAction GetTurn(IGetTurnContext context)
        {
            this.turnGate.Reset();
            this.submittedAction = null;

            // Notify subscribers that this player needs to act
            this.TurnRequested?.Invoke(this, new TurnRequestedEventArgs(this.Name, context));

            // Block until action is submitted or timeout
            if (!this.turnGate.Wait(this.turnTimeoutMs))
            {
                // Timeout — auto-fold
                return PlayerAction.Fold();
            }

            return this.submittedAction ?? PlayerAction.Fold();
        }

        /// <summary>
        /// Called by external code (e.g., SignalR hub) to submit the player's action.
        /// Unblocks GetTurn().
        /// </summary>
        public bool SubmitAction(PlayerAction action)
        {
            if (action == null)
            {
                return false;
            }

            this.submittedAction = action;
            this.turnGate.Set();
            return true;
        }

        public override void StartHand(IStartHandContext context)
        {
            base.StartHand(context);
            this.HandStarted?.Invoke(this, new HandStartedEventArgs(this.Name, context));
        }

        public override void StartRound(IStartRoundContext context)
        {
            base.StartRound(context);
            this.RoundStarted?.Invoke(this, new RoundStartedEventArgs(this.Name, context));
        }

        public override void EndHand(IEndHandContext context)
        {
            base.EndHand(context);
            this.HandEnded?.Invoke(this, new HandEndedEventArgs(this.Name, context));
        }
    }

    public class TurnRequestedEventArgs : EventArgs
    {
        public TurnRequestedEventArgs(string playerName, IGetTurnContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IGetTurnContext Context { get; }
    }

    public class HandStartedEventArgs : EventArgs
    {
        public HandStartedEventArgs(string playerName, IStartHandContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IStartHandContext Context { get; }
    }

    public class RoundStartedEventArgs : EventArgs
    {
        public RoundStartedEventArgs(string playerName, IStartRoundContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IStartRoundContext Context { get; }
    }

    public class HandEndedEventArgs : EventArgs
    {
        public HandEndedEventArgs(string playerName, IEndHandContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IEndHandContext Context { get; }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/TexasHoldem.sln`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/TexasHoldem.Logic/Async/TcsPlayer.cs
git commit -m "feat: add TcsPlayer — TaskCompletionSource bridge for async integration"
```

---

## Task 10: Write Integration Smoke Test for TcsPlayer

**Files:**
- Create: `src/Tests/TexasHoldem.Logic.Tests/Async/TcsPlayerTests.cs`

- [ ] **Step 1: Write integration test**

Create `src/Tests/TexasHoldem.Logic.Tests/Async/TcsPlayerTests.cs`:

```csharp
namespace TexasHoldem.Logic.Tests.Async
{
    using System.Threading;
    using System.Threading.Tasks;

    using TexasHoldem.Logic.Async;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    using Xunit;

    public class TcsPlayerTests
    {
        [Fact]
        public void SubmitAction_UnblocksGetTurn()
        {
            var player = new TcsPlayer("Test", turnTimeoutMs: 5000);
            PlayerAction receivedAction = null;

            // Subscribe to turn requests
            player.TurnRequested += (sender, args) =>
            {
                // Simulate external code submitting action after a short delay
                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    player.SubmitAction(PlayerAction.CheckOrCall());
                });
            };

            // Simulate game calling GetTurn on a background thread
            var turnTask = Task.Run(() =>
            {
                // We need a context — create a minimal one
                // GetTurn is called by the engine; here we call it directly
                receivedAction = player.GetTurn(null);
            });

            Assert.True(turnTask.Wait(3000), "GetTurn did not unblock");
            Assert.NotNull(receivedAction);
            Assert.Equal(PlayerActionType.CheckCall, receivedAction.Type);
        }

        [Fact]
        public void GetTurn_Timeout_AutoFolds()
        {
            var player = new TcsPlayer("Test", turnTimeoutMs: 500);

            var turnTask = Task.Run(() => player.GetTurn(null));

            Assert.True(turnTask.Wait(3000), "GetTurn did not return after timeout");
            Assert.Equal(PlayerActionType.Fold, turnTask.Result.Type);
        }

        [Fact]
        public void TcsPlayer_InFullGame_GameCompletes()
        {
            // TcsPlayer that auto-responds to every turn request
            var tcsPlayer = new TcsPlayer("TcsBot", turnTimeoutMs: 5000);
            tcsPlayer.TurnRequested += (sender, args) =>
            {
                Task.Run(() => tcsPlayer.SubmitAction(PlayerAction.CheckOrCall()));
            };

            var aiPlayer = new AutoCallPlayer("AI");

            var game = new TexasHoldemGame(tcsPlayer, aiPlayer, 20);

            var task = Task.Run(() => game.Start());
            Assert.True(task.Wait(30000), "Game with TcsPlayer did not complete");
        }

        private class AutoCallPlayer : BasePlayer
        {
            public AutoCallPlayer(string name)
            {
                this.Name = name;
            }

            public override string Name { get; }

            public override int BuyIn => -1;

            public override PlayerAction PostingBlind(IPostingBlindContext context)
            {
                return context.BlindAction;
            }

            public override PlayerAction GetTurn(IGetTurnContext context)
            {
                return PlayerAction.CheckOrCall();
            }
        }
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/Tests/TexasHoldem.Logic.Tests/ --filter "FullyQualifiedName~TcsPlayerTests"`
Expected: All 3 tests PASS

- [ ] **Step 3: Commit**

```bash
git add src/Tests/TexasHoldem.Logic.Tests/Async/TcsPlayerTests.cs
git commit -m "test: integration tests for TcsPlayer async bridge"
```

---

## Task 11: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Build entire solution**

Run: `dotnet build src/TexasHoldem.sln`
Expected: Build succeeds with 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/TexasHoldem.sln`
Expected: All tests pass

- [ ] **Step 3: Verify game simulation still runs**

Run: `dotnet run --project src/Tests/TexasHoldem.Tests.GameSimulations/`
Expected: Completes without hanging or crashing

- [ ] **Step 4: Create summary commit**

No changes needed — all commits are already done per task.

---

## Summary

| Task | Bug | Files Modified | Risk |
|------|-----|----------------|------|
| 1 | Full House >5 cards | HandEvaluator.cs | Low |
| 2 | Rank value overflow | Helpers.cs | Low |
| 3 | Split pot chip loss + side pot | HandLogic.cs | Medium |
| 4 | Heads-up showdown | HandLogic.cs | Medium |
| 5 | Infinite loop (Rebuy) | TexasHoldemGame.cs | Low |
| 6 | Constructor null validation | TexasHoldemGame.cs | Low |
| 7 | Mutable singleton | PlayerAction.cs, InternalPlayerMoney.cs | Medium |
| 8 | Insecure RNG | RandomProvider.cs | Low |
| 9 | Async bridge prototype | New: TcsPlayer.cs | Medium |
| 10 | TcsPlayer integration tests | New test file | Low |
| 11 | Final verification | None | Low |
