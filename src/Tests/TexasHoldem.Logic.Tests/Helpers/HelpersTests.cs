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
            // Bug #2: With many opponents, wins could overflow into next rank range
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

            // Create 8 weak opponents that all lose to highCardPlayer
            var weakOpponents = Enumerable.Range(0, 8).Select(i => (IEnumerable<Card>)new List<Card>
            {
                new Card(CardSuit.Diamond, CardType.Six),
                new Card(CardSuit.Club, CardType.Eight),
            }).ToList();

            var highCardAllOpponents = weakOpponents.Concat(new[] { pairPlayer }).ToArray();
            var pairAllOpponents = weakOpponents.Concat(new[] { highCardPlayer }).ToArray();

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
