namespace PokerGame.Api.Services
{
    using System;
    using System.Threading.Tasks;

    using PokerGame.Api.DTOs;

    public interface IGameEngineWrapper : IDisposable
    {
        Task StartGameAsync(Room room);

        bool SubmitAction(string playerId, string actionType, int? amount);

        GameStateDto? GetGameState(string roomCode);

        event Action<string, GameStateDto>? GameStateChanged;

        event Action<string, string, YourTurnDto>? PlayerTurnRequested;

        event Action<string, string, int>? GameEnded;
    }
}
