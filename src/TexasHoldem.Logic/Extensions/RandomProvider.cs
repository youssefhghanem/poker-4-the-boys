namespace TexasHoldem.Logic.Extensions
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Thread-safe cryptographically secure random number generator.
    /// </summary>
    public static class RandomProvider
    {
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

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
            var maxAcceptable = (uint.MaxValue / (uint)range) * (uint)range;

            var bytes = new byte[4];
            uint candidate;
            do
            {
                Rng.GetBytes(bytes);
                candidate = BitConverter.ToUInt32(bytes, 0);
            }
            while (candidate >= maxAcceptable);

            return minValue + (int)(candidate % (uint)range);
        }
    }
}
