namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;

    public class LobbyStateDto
    {
        public string RoomCode { get; set; } = string.Empty;

        public string State { get; set; } = "Lobby";

        public int StartingChips { get; set; }

        public int SmallBlind { get; set; }

        public List<LobbyPlayerDto> Players { get; set; } = new List<LobbyPlayerDto>();
    }

    public class LobbyPlayerDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;

        public bool IsHost { get; set; }
    }
}
