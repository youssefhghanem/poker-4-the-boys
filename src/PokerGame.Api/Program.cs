namespace PokerGame.Api
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    using PokerGame.Api.Hubs;
    using PokerGame.Api.Services;

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // SignalR
            builder.Services.AddSignalR();

            // CORS: SignalR requires AllowCredentials, which is incompatible with AllowAnyOrigin.
            // For dev we allow localhost origins; production should be restricted to the actual domain.
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy
                        .SetIsOriginAllowed(_ => true) // Dev: allow all origins
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });

            // Services
            builder.Services.AddSingleton<IRoomManager, RoomManager>();
            builder.Services.AddSingleton<IGameEngineWrapper, GameEngineWrapper>();

            var app = builder.Build();

            app.UseCors();

            app.MapHub<GameHub>("/gamehub");

            app.MapGet("/", () => "Poker Game API is running");

            // Wire GameEngineWrapper events to SignalR
            var hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>();
            var gameEngine = app.Services.GetRequiredService<IGameEngineWrapper>();

            gameEngine.GameStateChanged += (roomCode, state) =>
            {
                hubContext.Clients.Group(roomCode).SendAsync("OnGameStateChanged", state);
            };

            gameEngine.PlayerTurnRequested += (roomCode, playerId, turnInfo) =>
            {
                var roomManager = app.Services.GetRequiredService<IRoomManager>();
                var room = roomManager.GetRoomByPlayerId(playerId);
                if (room == null)
                {
                    return;
                }

                var player = room.Players.Find(p => p.Id == playerId);
                if (player?.ConnectionId == null)
                {
                    return;
                }

                hubContext.Clients.Client(player.ConnectionId).SendAsync("OnYourTurn", turnInfo);

                // Notify all others who is acting
                hubContext.Clients.Group(roomCode).SendAsync("OnPlayerActing", new { PlayerId = playerId });
            };

            gameEngine.GameEnded += (roomCode, winnerSessionId, handsPlayed) =>
            {
                hubContext.Clients.Group(roomCode).SendAsync("OnGameEnded", new
                {
                    WinnerPlayerId = winnerSessionId,
                    HandsPlayed = handsPlayed,
                });
            };

            app.Run();
        }
    }
}
