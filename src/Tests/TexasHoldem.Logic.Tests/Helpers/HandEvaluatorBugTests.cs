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
