namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;

    public class YourTurnDto
    {
        public List<string> AvailableActions { get; set; } = new List<string>();

        public int MinRaise { get; set; }

        public int MaxRaise { get; set; }

        public int AmountToCall { get; set; }

        public int TimeRemaining { get; set; } = 30;
    }
}
