namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;

    public class GameStateDto
    {
        public long StateVersion { get; set; }

        public string Phase { get; set; } = "Lobby";

        public string? CurrentRound { get; set; }

        public List<CardDto>? CommunityCards { get; set; }

        public int MainPot { get; set; }

        public List<SidePotDto>? SidePots { get; set; }

        public int DealerPosition { get; set; }

        public string? CurrentPlayerToActId { get; set; }

        public List<PlayerInfoDto> Players { get; set; } = new List<PlayerInfoDto>();

        public int HandNumber { get; set; }

        public int SmallBlind { get; set; }

        public int MinRaise { get; set; }

        public bool IsShowdown { get; set; }

        public Dictionary<string, List<CardDto>>? ShowdownHands { get; set; }
    }

    public class CardDto
    {
        public string Suit { get; set; } = string.Empty;

        public string Rank { get; set; } = string.Empty;

        public string Display { get; set; } = string.Empty;
    }

    public class SidePotDto
    {
        public int Amount { get; set; }

        public List<string> EligiblePlayerIds { get; set; } = new List<string>();
    }

    public class PlayerInfoDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;

        public int Chips { get; set; }

        public string Status { get; set; } = "SittingOut";

        public int CurrentBet { get; set; }

        public int TotalBetThisHand { get; set; }

        public bool IsHost { get; set; }

        public int Position { get; set; }

        public List<CardDto>? HoleCards { get; set; }
    }
}
