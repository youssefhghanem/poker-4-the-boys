namespace TexasHoldem.Logic.Tests.GameMechanics
{
    using Xunit;

    public class HandLogicTests
    {
        [Theory]
        [InlineData(101, 2, 50, 1)]
        [InlineData(103, 3, 34, 1)]
        [InlineData(100, 4, 25, 0)]
        [InlineData(7, 3, 2, 1)]
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
