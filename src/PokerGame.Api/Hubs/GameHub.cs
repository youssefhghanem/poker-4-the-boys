namespace PokerGame.Api.Hubs
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using Microsoft.AspNetCore.SignalR;

    using PokerGame.Api.DTOs;
    using PokerGame.Api.Services;

    public class GameHub : Hub
    {
        private readonly IRoomManager roomManager;
        private readonly IGameEngineWrapper gameEngine;

        public GameHub(IRoomManager roomManager, IGameEngineWrapper gameEngine)
        {
            this.roomManager = roomManager;
            this.gameEngine = gameEngine;
        }

        public async Task<RoomCreatedResult> CreateRoom(string hostName, string emoji, int startingChips = 1000)
        {
            try
            {
                var (roomCode, hostPlayerId) = this.roomManager.CreateRoom(hostName, emoji, startingChips);

                await this.Groups.AddToGroupAsync(this.Context.ConnectionId, roomCode);
                this.roomManager.UpdateConnection(hostPlayerId, this.Context.ConnectionId);

                return new RoomCreatedResult
                {
                    RoomCode = roomCode,
                    HostPlayerId = hostPlayerId,
                    Success = true,
                };
            }
            catch (Exception ex)
            {
                return new RoomCreatedResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }
        }

        public async Task<JoinResult> JoinRoom(string roomCode, string playerName, string emoji)
        {
            try
            {
                var (playerId, error) = this.roomManager.JoinRoom(roomCode, playerName, emoji);

                if (playerId == null)
                {
                    return new JoinResult
                    {
                        Success = false,
                        ErrorMessage = error ?? "Failed to join room",
                    };
                }

                await this.Groups.AddToGroupAsync(this.Context.ConnectionId, roomCode.Trim().ToUpperInvariant());
                this.roomManager.UpdateConnection(playerId, this.Context.ConnectionId);

                await this.Clients.OthersInGroup(roomCode.Trim().ToUpperInvariant()).SendAsync(
                    "OnPlayerJoined",
                    new { PlayerId = playerId, Name = playerName, Emoji = emoji });

                return new JoinResult
                {
                    PlayerId = playerId,
                    Success = true,
                };
            }
            catch (Exception ex)
            {
                return new JoinResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }
        }

        public async Task LeaveRoom()
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return;
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return;
            }

            var wasHost = room.Players.Any(p => p.Id == playerId && p.IsHost);

            this.roomManager.LeaveRoom(room.RoomCode, playerId);
            await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, room.RoomCode);

            if (wasHost)
            {
                await this.Clients.Group(room.RoomCode).SendAsync("OnRoomClosed");
            }
            else
            {
                await this.Clients.Group(room.RoomCode).SendAsync(
                    "OnPlayerLeft",
                    new { PlayerId = playerId });
            }
        }

        public async Task<bool> KickPlayer(string targetPlayerId)
        {
            var callerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (callerId == null)
            {
                return false;
            }

            var room = this.roomManager.GetRoomByPlayerId(callerId);
            if (room == null)
            {
                return false;
            }

            var caller = room.Players.FirstOrDefault(p => p.Id == callerId);
            if (caller == null || !caller.IsHost)
            {
                return false;
            }

            var target = room.Players.FirstOrDefault(p => p.Id == targetPlayerId);
            if (target == null || target.IsHost)
            {
                return false;
            }

            if (target.ConnectionId != null)
            {
                await this.Clients.Client(target.ConnectionId).SendAsync("OnKicked");
            }

            this.roomManager.LeaveRoom(room.RoomCode, targetPlayerId);

            await this.Clients.Group(room.RoomCode).SendAsync(
                "OnPlayerLeft",
                new { PlayerId = targetPlayerId });

            return true;
        }

        public async Task<ActionResultDto> StartGame()
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Not in a room" };
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Room not found" };
            }

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Only the host can start the game" };
            }

            if (room.Players.Count < 2)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Need at least 2 players" };
            }

            if (room.State != RoomState.Lobby)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Game already started" };
            }

            await this.gameEngine.StartGameAsync(room);
            return new ActionResultDto { Success = true };
        }

        public ActionResultDto SubmitAction(string actionType, int? amount = null)
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Not in a room" };
            }

            // Validate action before submitting to engine
            var (isValid, error) = this.gameEngine.ValidateAction(playerId, actionType, amount);
            if (!isValid)
            {
                return new ActionResultDto { Success = false, ErrorMessage = error };
            }

            var success = this.gameEngine.SubmitAction(playerId, actionType, amount);

            return new ActionResultDto
            {
                Success = success,
                ErrorMessage = success ? null : "Failed to submit action",
            };
        }

        public GameStateDto? GetGameState()
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return null;
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return null;
            }

            // Get state from engine (includes players from BuildDto, hole cards filtered per-player).
            // Note: broadcast via OnGameStateChanged does NOT include hole cards (hidden info).
            // Clients should call GetGameState after receiving OnGameStateChanged to get their cards.
            var state = this.gameEngine.GetGameState(room.RoomCode, playerId);

            // If no active game, return lobby/game-complete state
            if (state == null)
            {
                return new GameStateDto
                {
                    Phase = room.State.ToString(),
                    Players = room.Players.Select((p, i) => new PlayerInfoDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Emoji = p.Emoji,
                        Chips = p.Chips,
                        Status = p.Status.ToString(),
                        IsHost = p.IsHost,
                        Position = i,
                    }).ToList(),
                    SmallBlind = room.SmallBlind,
                };
            }

            return state;
        }

        public LobbyStateDto? GetLobbyState()
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return null;
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return null;
            }

            return new LobbyStateDto
            {
                RoomCode = room.RoomCode,
                State = room.State.ToString(),
                StartingChips = room.StartingChips,
                SmallBlind = room.SmallBlind,
                Players = room.Players.Select(p => new LobbyPlayerDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Emoji = p.Emoji,
                    IsHost = p.IsHost,
                }).ToList(),
            };
        }

        public async Task<ActionResultDto> UpdateSettings(int? smallBlind = null, int? startingChips = null)
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Not in a room" };
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Room not found" };
            }

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Only the host can update settings" };
            }

            if (room.State != RoomState.Lobby)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Cannot change settings during game" };
            }

            if (smallBlind.HasValue)
            {
                if (smallBlind.Value <= 0)
                {
                    return new ActionResultDto { Success = false, ErrorMessage = "Small blind must be positive" };
                }

                room.SmallBlind = smallBlind.Value;
            }

            if (startingChips.HasValue)
            {
                if (startingChips.Value <= 0)
                {
                    return new ActionResultDto { Success = false, ErrorMessage = "Starting chips must be positive" };
                }

                room.StartingChips = startingChips.Value;

                foreach (var p in room.Players)
                {
                    p.Chips = startingChips.Value;
                }
            }

            await this.Clients.Group(room.RoomCode).SendAsync(
                "OnSettingsChanged",
                new { SmallBlind = room.SmallBlind, StartingChips = room.StartingChips });

            return new ActionResultDto { Success = true };
        }

        public async Task<ActionResultDto> PlayAgain()
        {
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);
            if (playerId == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Not in a room" };
            }

            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Room not found" };
            }

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Only the host can restart" };
            }

            if (room.State != RoomState.GameComplete)
            {
                return new ActionResultDto { Success = false, ErrorMessage = "Game not complete" };
            }

            this.roomManager.ResetRoom(room.RoomCode);
            this.gameEngine.ClearGameState(room.RoomCode);

            await this.Clients.Group(room.RoomCode).SendAsync("OnGameRestarted");

            return new ActionResultDto { Success = true };
        }

        public async Task<GameStateDto?> Reconnect(string playerId)
        {
            var room = this.roomManager.GetRoomByPlayerId(playerId);
            if (room == null)
            {
                return null;
            }

            var session = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (session == null)
            {
                return null;
            }

            // Re-add to SignalR group with new connection
            await this.Groups.AddToGroupAsync(this.Context.ConnectionId, room.RoomCode);
            this.roomManager.UpdateConnection(playerId, this.Context.ConnectionId);

            // Clear disconnected state
            session.IsDisconnected = false;
            session.DisconnectedAt = null;

            // Notify others
            await this.Clients.Group(room.RoomCode).SendAsync(
                "OnPlayerReconnected",
                new { PlayerId = playerId });

            // Return current game state
            return this.gameEngine.GetGameState(room.RoomCode, playerId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Look up player BEFORE removing the connection mapping
            var playerId = this.roomManager.GetPlayerIdByConnectionId(this.Context.ConnectionId);

            if (playerId != null)
            {
                var room = this.roomManager.GetRoomByPlayerId(playerId);

                if (room != null && room.State == RoomState.HandInProgress)
                {
                    var session = room.Players.FirstOrDefault(p => p.Id == playerId);
                    if (session != null)
                    {
                        // Mark as disconnected, start grace period
                        session.IsDisconnected = true;
                        session.DisconnectedAt = DateTime.UtcNow;

                        await this.Clients.Group(room.RoomCode).SendAsync(
                            "OnPlayerDisconnected",
                            new { PlayerId = playerId, GracePeriodSeconds = 60 });

                        // Capture for closure
                        var capturedPlayerId = playerId;
                        var capturedRoom = room;
                        var capturedEngine = this.gameEngine;

                        // Start grace period timer
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(60000);

                            var currentSession = capturedRoom.Players.FirstOrDefault(p => p.Id == capturedPlayerId);
                            if (currentSession?.IsDisconnected == true)
                            {
                                // Grace period expired, auto-fold
                                capturedEngine.SubmitAction(capturedPlayerId, "Fold", null);
                            }
                        });
                    }
                }
                else if (room != null)
                {
                    // In lobby, remove player from room
                    var wasHost = room.Players.Any(p => p.Id == playerId && p.IsHost);
                    this.roomManager.LeaveRoom(room.RoomCode, playerId);

                    if (wasHost)
                    {
                        await this.Clients.Group(room.RoomCode).SendAsync("OnRoomClosed");
                    }
                    else
                    {
                        await this.Clients.Group(room.RoomCode).SendAsync(
                            "OnPlayerLeft",
                            new { PlayerId = playerId });
                    }
                }
            }

            // Clean up the connection mapping (but keep player in room for reconnection)
            this.roomManager.DisconnectPlayer(this.Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }
    }
}
