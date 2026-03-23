namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;

    public class GameEndDto
    {
        public string WinnerPlayerId { get; set; } = string.Empty;

        public string WinnerName { get; set; } = string.Empty;

        public int TotalHandsPlayed { get; set; }

        public List<PlayerStandingDto> Standings { get; set; } = new List<PlayerStandingDto>();
    }

    public class PlayerStandingDto
    {
        public string PlayerId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int FinalChips { get; set; }

        public int Position { get; set; }
    }

    public class TurnTimerDto
    {
        public string PlayerId { get; set; } = string.Empty;

        public int TimeRemaining { get; set; }

        public int TotalTime { get; set; } = 30;
    }
}
