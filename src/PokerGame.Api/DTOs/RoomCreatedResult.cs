namespace PokerGame.Api.DTOs
{
    public class RoomCreatedResult
    {
        public string RoomCode { get; set; } = string.Empty;

        public string HostPlayerId { get; set; } = string.Empty;

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
