namespace TexasHoldem.Logic.Tests.Extensions
{
    using System.Collections.Generic;

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
            var counts = new int[10];
            for (int i = 0; i < 10000; i++)
            {
                counts[RandomProvider.Next(0, 10)]++;
            }

            foreach (var count in counts)
            {
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
