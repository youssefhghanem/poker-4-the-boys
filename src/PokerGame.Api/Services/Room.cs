namespace PokerGame.Api.Services
{
    using System.Collections.Generic;

    public class Room
    {
        public string RoomCode { get; set; } = string.Empty;

        public string HostPlayerId { get; set; } = string.Empty;

        public List<PlayerSession> Players { get; set; } = new List<PlayerSession>();

        public int StartingChips { get; set; } = 1000;

        public RoomState State { get; set; } = RoomState.Lobby;

        public int SmallBlind { get; set; } = 10;
    }

    public enum RoomState
    {
        Lobby,
        HandInProgress,
        HandComplete,
        GameComplete,
    }
}
