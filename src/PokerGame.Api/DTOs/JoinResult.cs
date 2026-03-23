namespace PokerGame.Api.DTOs
{
    public class JoinResult
    {
        public string? PlayerId { get; set; }

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
