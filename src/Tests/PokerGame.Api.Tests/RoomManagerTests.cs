namespace PokerGame.Api.Tests
{
    using System;
    using System.Linq;

    using PokerGame.Api.Services;

    using Xunit;

    public class RoomManagerTests
    {
        private readonly RoomManager roomManager;

        public RoomManagerTests()
        {
            this.roomManager = new RoomManager();
        }

        [Fact]
        public void CreateRoom_ReturnsValidRoomCode()
        {
            var (roomCode, hostPlayerId) = this.roomManager.CreateRoom("Host", "😎", 1000);

            Assert.Equal(6, roomCode.Length);
            Assert.NotEmpty(hostPlayerId);
        }

        [Fact]
        public void CreateRoom_HostIsInRoom()
        {
            var (roomCode, hostPlayerId) = this.roomManager.CreateRoom("Host", "😎");

            var room = this.roomManager.GetRoom(roomCode);
            Assert.NotNull(room);
            Assert.Single(room.Players);
            var host = room.Players.First();
            Assert.True(host.IsHost);
            Assert.Equal(hostPlayerId, host.Id);
            Assert.Equal("Host", host.Name);
        }

        [Fact]
        public void CreateRoom_DefaultChipsIs1000()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");
            var room = this.roomManager.GetRoom(roomCode);

            Assert.Equal(1000, room!.StartingChips);
            Assert.Equal(1000, room.Players.First().Chips);
        }

        [Fact]
        public void CreateRoom_CustomChips()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎", 5000);
            var room = this.roomManager.GetRoom(roomCode);

            Assert.Equal(5000, room!.StartingChips);
            Assert.Equal(5000, room.Players.First().Chips);
        }

        [Fact]
        public void CreateRoom_EmptyName_Throws()
        {
            Assert.Throws<ArgumentException>(() => this.roomManager.CreateRoom("", "😎"));
        }

        [Fact]
        public void JoinRoom_ValidCode_AddsPlayer()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");
            var (playerId, error) = this.roomManager.JoinRoom(roomCode, "Player2", "🤔");

            Assert.NotNull(playerId);
            Assert.Null(error);

            var room = this.roomManager.GetRoom(roomCode);
            Assert.Equal(2, room!.Players.Count);
        }

        [Fact]
        public void JoinRoom_InvalidCode_ReturnsError()
        {
            var (playerId, error) = this.roomManager.JoinRoom("XXXXXX", "Player", "😀");

            Assert.Null(playerId);
            Assert.Equal("Room not found", error);
        }

        [Fact]
        public void JoinRoom_DuplicateName_ReturnsError()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");

            var (playerId, error) = this.roomManager.JoinRoom(roomCode, "Host", "😀");

            Assert.Null(playerId);
            Assert.Equal("Name already taken in this room", error);
        }

        [Fact]
        public void JoinRoom_DuplicateName_CaseInsensitive()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");

            var (playerId, error) = this.roomManager.JoinRoom(roomCode, "HOST", "😀");

            Assert.Null(playerId);
            Assert.Equal("Name already taken in this room", error);
        }

        [Fact]
        public void JoinRoom_MaxPlayers_ReturnsError()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");
            for (int i = 0; i < 5; i++)
            {
                this.roomManager.JoinRoom(roomCode, $"Player{i}", "😀");
            }

            var (playerId, error) = this.roomManager.JoinRoom(roomCode, "OneMore", "😀");

            Assert.Null(playerId);
            Assert.Equal("Room is full (max 6 players)", error);
        }

        [Fact]
        public void JoinRoom_GetsStartingChips()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎", 2000);
            var (playerId, _) = this.roomManager.JoinRoom(roomCode, "Player2", "😀");

            var room = this.roomManager.GetRoom(roomCode);
            var player = room!.Players.First(p => p.Id == playerId);
            Assert.Equal(2000, player.Chips);
        }

        [Fact]
        public void LeaveRoom_Host_ClosesRoom()
        {
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎");
            this.roomManager.JoinRoom(roomCode, "Player2", "😀");

            var result = this.roomManager.LeaveRoom(roomCode, hostId);

            Assert.True(result);
            Assert.Null(this.roomManager.GetRoom(roomCode));
        }

        [Fact]
        public void LeaveRoom_NonHost_RemovesPlayer()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");
            var (playerId, _) = this.roomManager.JoinRoom(roomCode, "Player2", "😀");

            var result = this.roomManager.LeaveRoom(roomCode, playerId!);

            Assert.True(result);
            var room = this.roomManager.GetRoom(roomCode);
            Assert.Single(room!.Players);
        }

        [Fact]
        public void LeaveRoom_InvalidRoom_ReturnsFalse()
        {
            var result = this.roomManager.LeaveRoom("XXXXXX", "invalid");
            Assert.False(result);
        }

        [Fact]
        public void UpdateConnection_MapsCorrectly()
        {
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎");
            this.roomManager.UpdateConnection(hostId, "conn-123");

            var room = this.roomManager.GetRoom(roomCode);
            var host = room!.Players.First(p => p.Id == hostId);
            Assert.Equal("conn-123", host.ConnectionId);
        }

        [Fact]
        public void GetPlayerIdByConnectionId_ReturnsCorrectId()
        {
            var (_, hostId) = this.roomManager.CreateRoom("Host", "😎");
            this.roomManager.UpdateConnection(hostId, "conn-123");

            var result = this.roomManager.GetPlayerIdByConnectionId("conn-123");
            Assert.Equal(hostId, result);
        }

        [Fact]
        public void GetPlayerIdByConnectionId_UnknownConnection_ReturnsNull()
        {
            var result = this.roomManager.GetPlayerIdByConnectionId("unknown");
            Assert.Null(result);
        }

        [Fact]
        public void GetRoomByConnectionId_ReturnsCorrectRoom()
        {
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎");
            this.roomManager.UpdateConnection(hostId, "conn-123");

            var room = this.roomManager.GetRoomByConnectionId("conn-123");
            Assert.NotNull(room);
            Assert.Equal(roomCode, room.RoomCode);
        }

        [Fact]
        public void DisconnectPlayer_ClearsConnection()
        {
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎");
            this.roomManager.UpdateConnection(hostId, "conn-123");

            this.roomManager.DisconnectPlayer("conn-123");

            Assert.Null(this.roomManager.GetPlayerIdByConnectionId("conn-123"));
            var room = this.roomManager.GetRoom(roomCode);
            Assert.Null(room!.Players.First().ConnectionId);
        }

        [Fact]
        public void JoinRoom_GameInProgress_ReturnsError()
        {
            var (roomCode, _) = this.roomManager.CreateRoom("Host", "😎");
            var room = this.roomManager.GetRoom(roomCode);
            room!.State = RoomState.HandInProgress;

            var (playerId, error) = this.roomManager.JoinRoom(roomCode, "Late", "😀");

            Assert.Null(playerId);
            Assert.Equal("Game already in progress", error);
        }

        [Fact]
        public void CreateRoom_RoomCodeIsUpperAlphanumeric()
        {
            for (int i = 0; i < 20; i++)
            {
                var (roomCode, _) = this.roomManager.CreateRoom($"Host{i}", "😎");
                Assert.Matches("^[A-Z0-9]{6}$", roomCode);
            }
        }

        [Fact]
        public void MultipleRooms_Independent()
        {
            var (room1, _) = this.roomManager.CreateRoom("Host1", "😎");
            var (room2, _) = this.roomManager.CreateRoom("Host2", "🤖");

            Assert.NotEqual(room1, room2);
            Assert.NotNull(this.roomManager.GetRoom(room1));
            Assert.NotNull(this.roomManager.GetRoom(room2));
        }
    }
}
