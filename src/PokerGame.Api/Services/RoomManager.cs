namespace PokerGame.Api.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;

    public class RoomManager : IRoomManager
    {
        // Exclude confusing chars: 0/O, 1/I/L
        private const string CodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        private readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        private readonly Dictionary<string, string> playerToRoom = new Dictionary<string, string>();
        private readonly Dictionary<string, string> connectionToPlayer = new Dictionary<string, string>();
        private readonly object syncLock = new object();

        public (string RoomCode, string HostPlayerId) CreateRoom(string hostName, string emoji, int startingChips = 1000)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new ArgumentException("Host name is required.", nameof(hostName));
            }

            var host = new PlayerSession
            {
                Name = hostName.Trim(),
                Emoji = emoji,
                Chips = startingChips,
                IsHost = true,
            };

            lock (this.syncLock)
            {
                var roomCode = this.GenerateUniqueRoomCode();
                var room = new Room
                {
                    RoomCode = roomCode,
                    HostPlayerId = host.Id,
                    Players = new List<PlayerSession> { host },
                    StartingChips = startingChips,
                };

                this.rooms[roomCode] = room;
                this.playerToRoom[host.Id] = roomCode;

                return (roomCode, host.Id);
            }
        }

        public (string? PlayerId, string? Error) JoinRoom(string roomCode, string playerName, string emoji)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                return (null, "Room code is required");
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                return (null, "Player name is required");
            }

            roomCode = roomCode.Trim().ToUpperInvariant();
            playerName = playerName.Trim();

            lock (this.syncLock)
            {
                if (!this.rooms.TryGetValue(roomCode, out var room))
                {
                    return (null, "Room not found");
                }

                if (room.State != RoomState.Lobby)
                {
                    return (null, "Game already in progress");
                }

                if (room.Players.Count >= 6)
                {
                    return (null, "Room is full (max 6 players)");
                }

                if (room.Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (null, "Name already taken in this room");
                }

                var session = new PlayerSession
                {
                    Name = playerName,
                    Emoji = emoji,
                    Chips = room.StartingChips,
                    IsHost = false,
                };

                room.Players.Add(session);
                this.playerToRoom[session.Id] = roomCode;

                return (session.Id, null);
            }
        }

        public bool LeaveRoom(string roomCode, string playerId)
        {
            lock (this.syncLock)
            {
                if (!this.rooms.TryGetValue(roomCode, out var room))
                {
                    return false;
                }

                var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                if (player == null)
                {
                    return false;
                }

                if (player.IsHost)
                {
                    // Host leaving closes the room
                    foreach (var p in room.Players)
                    {
                        this.playerToRoom.Remove(p.Id);
                        if (p.ConnectionId != null)
                        {
                            this.connectionToPlayer.Remove(p.ConnectionId);
                        }
                    }

                    this.rooms.Remove(roomCode);
                    return true;
                }

                room.Players.Remove(player);
                this.playerToRoom.Remove(playerId);
                if (player.ConnectionId != null)
                {
                    this.connectionToPlayer.Remove(player.ConnectionId);
                }

                return true;
            }
        }

        public Room? GetRoom(string roomCode)
        {
            lock (this.syncLock)
            {
                return this.rooms.TryGetValue(roomCode, out var room) ? room : null;
            }
        }

        public Room? GetRoomByPlayerId(string playerId)
        {
            lock (this.syncLock)
            {
                if (!this.playerToRoom.TryGetValue(playerId, out var roomCode))
                {
                    return null;
                }

                return this.rooms.TryGetValue(roomCode, out var room) ? room : null;
            }
        }

        public Room? GetRoomByConnectionId(string connectionId)
        {
            lock (this.syncLock)
            {
                if (!this.connectionToPlayer.TryGetValue(connectionId, out var playerId))
                {
                    return null;
                }

                return this.GetRoomByPlayerId(playerId);
            }
        }

        public string? GetPlayerIdByConnectionId(string connectionId)
        {
            lock (this.syncLock)
            {
                return this.connectionToPlayer.TryGetValue(connectionId, out var playerId)
                    ? playerId
                    : null;
            }
        }

        public void UpdateConnection(string playerId, string connectionId)
        {
            lock (this.syncLock)
            {
                // Remove old connection mapping if any
                var oldConnection = this.connectionToPlayer
                    .FirstOrDefault(x => x.Value == playerId).Key;
                if (oldConnection != null)
                {
                    this.connectionToPlayer.Remove(oldConnection);
                }

                this.connectionToPlayer[connectionId] = playerId;

                // Update player's stored connection ID
                if (this.playerToRoom.TryGetValue(playerId, out var roomCode)
                    && this.rooms.TryGetValue(roomCode, out var room))
                {
                    var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                    if (player != null)
                    {
                        player.ConnectionId = connectionId;
                    }
                }
            }
        }

        public void DisconnectPlayer(string connectionId)
        {
            lock (this.syncLock)
            {
                if (!this.connectionToPlayer.TryGetValue(connectionId, out var playerId))
                {
                    return;
                }

                this.connectionToPlayer.Remove(connectionId);

                if (this.playerToRoom.TryGetValue(playerId, out var roomCode)
                    && this.rooms.TryGetValue(roomCode, out var room))
                {
                    var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                    if (player != null)
                    {
                        player.ConnectionId = null;
                    }
                }
            }
        }

        public bool ResetRoom(string roomCode)
        {
            lock (this.syncLock)
            {
                if (!this.rooms.TryGetValue(roomCode, out var room))
                {
                    return false;
                }

                room.State = RoomState.Lobby;

                foreach (var player in room.Players)
                {
                    player.Chips = room.StartingChips;
                    player.CurrentBet = 0;
                    player.Status = PlayerStatus.SittingOut;
                }

                return true;
            }
        }

        private string GenerateUniqueRoomCode()
        {
            string code;
            do
            {
                code = GenerateRoomCode();
            }
            while (this.rooms.ContainsKey(code));

            return code;
        }

        private static string GenerateRoomCode()
        {
            var bytes = new byte[6];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);

            var code = new char[6];
            for (int i = 0; i < 6; i++)
            {
                code[i] = CodeChars[bytes[i] % CodeChars.Length];
            }

            return new string(code);
        }
    }
}
