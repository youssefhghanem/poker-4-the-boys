namespace PokerGame.Api.Services
{
    using System;

    public class PlayerSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = string.Empty;

        public string Emoji { get; set; } = "😀";

        public int Chips { get; set; }

        public string? ConnectionId { get; set; }

        public bool IsHost { get; set; }

        public PlayerStatus Status { get; set; } = PlayerStatus.SittingOut;

        public int CurrentBet { get; set; }

        public bool IsDisconnected { get; set; }

        public DateTime? DisconnectedAt { get; set; }
    }

    public enum PlayerStatus
    {
        SittingOut,
        Active,
        Folded,
        AllIn,
        Eliminated,
    }
}
