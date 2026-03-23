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
