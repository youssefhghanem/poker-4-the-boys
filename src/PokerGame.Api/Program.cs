namespace PokerGame.Api
{
    using System.Linq;

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
            var roomManager = app.Services.GetRequiredService<IRoomManager>();

            gameEngine.GameStateChanged += (roomCode, state) =>
            {
                hubContext.Clients.Group(roomCode).SendAsync("OnGameStateChanged", state);
            };

            gameEngine.PlayerTurnRequested += (roomCode, playerId, turnInfo) =>
            {
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

            gameEngine.GameEnded += (roomCode, gameEndDto) =>
            {
                hubContext.Clients.Group(roomCode).SendAsync("OnGameEnd", gameEndDto);
            };

            gameEngine.TurnTimerTick += (roomCode, timerDto) =>
            {
                // Broadcast timer tick to all players so everyone sees the countdown
                hubContext.Clients.Group(roomCode).SendAsync("OnTurnTimer", timerDto);
            };

            app.Run();
        }
    }
}
