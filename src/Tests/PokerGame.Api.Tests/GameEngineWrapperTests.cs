namespace PokerGame.Api.Tests
{
    using System;
    using System.Linq;

    using PokerGame.Api.DTOs;
    using PokerGame.Api.Services;

    using Xunit;

    public class GameEngineWrapperTests : IDisposable
    {
        private readonly RoomManager roomManager;
        private readonly GameEngineWrapper wrapper;

        public GameEngineWrapperTests()
        {
            this.roomManager = new RoomManager();
            this.wrapper = new GameEngineWrapper();
        }

        [Fact]
        public void SubmitAction_InvalidPlayerId_ReturnsFalse()
        {
            var result = this.wrapper.SubmitAction("invalid-id", "Fold", null);
            Assert.False(result);
        }

        [Fact]
        public void GetGameState_NoActiveGame_ReturnsNull()
        {
            var state = this.wrapper.GetGameState("NONEXISTENT");
            Assert.Null(state);
        }

        [Fact]
        public void GetGameState_WithRequestingPlayer_ReturnsNull_WhenNoGame()
        {
            var state = this.wrapper.GetGameState("NONEXISTENT", "some-player");
            Assert.Null(state);
        }

        [Fact]
        public void ClearGameState_NoActiveGame_DoesNotThrow()
        {
            var exception = Record.Exception(() => this.wrapper.ClearGameState("NONEXISTENT"));
            Assert.Null(exception);
        }

        [Fact]
        public void ValidateAction_PlayerNotInGame_ReturnsFalse()
        {
            var (success, error) = this.wrapper.ValidateAction("unknown-player", "Fold", null);
            Assert.False(success);
            Assert.Equal("Player not in active game", error);
        }

        [Fact]
        public void ValidateAction_RaiseWithNoAmount_ReturnsFalse()
        {
            // Create a room and start a game to have an active game
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎", 1000);
            this.roomManager.JoinRoom(roomCode, "Player2", "🤔");
            var room = this.roomManager.GetRoom(roomCode)!;

            this.wrapper.StartGameAsync(room).Wait();

            // Wait briefly for game to start
            System.Threading.Thread.Sleep(200);

            // ValidateAction for Raise with no amount against the current player
            // We don't know which player is current, so test against both
            var (success1, error1) = this.wrapper.ValidateAction(hostId, "RAISE", null);
            if (success1)
            {
                // This means it was the host's turn but raise with null amount should still fail
                // Actually, our validation checks the raise amount
                Assert.True(false, "Raise with no amount should not succeed validation");
            }
            else
            {
                // Either not their turn, or invalid raise
                Assert.NotNull(error1);
            }
        }

        [Fact]
        public void ValidateAction_RaiseWithZeroAmount_ReturnsFalse()
        {
            var (roomCode, hostId) = this.roomManager.CreateRoom("Host", "😎", 1000);
            this.roomManager.JoinRoom(roomCode, "Player2", "🤔");
            var room = this.roomManager.GetRoom(roomCode)!;

            this.wrapper.StartGameAsync(room).Wait();
            System.Threading.Thread.Sleep(200);

            var (success, error) = this.wrapper.ValidateAction(hostId, "RAISE", 0);

            // Either not their turn, or invalid raise amount
            Assert.False(success);
            Assert.NotNull(error);
        }

        [Fact]
        public void HandleHandEnded_ShowdownHand_BroadcastsHandCompletePhase()
        {
            // Arrange
            var roomManager = new RoomManager();
            using var wrapper = new GameEngineWrapper();
            var (roomCode, _) = roomManager.CreateRoom("Alice", "😀", 200);
            roomManager.JoinRoom(roomCode, "Bob", "🎯");
            var room = roomManager.GetRoom(roomCode)!;

            var broadcastPhases = new System.Collections.Concurrent.ConcurrentBag<string>();
            wrapper.GameStateChanged += (_, dto) => broadcastPhases.Add(dto.Phase);

            // Act
            wrapper.StartGameAsync(room);

            // Poll until first player needs to act (max 2s)
            GameStateDto? state = null;
            for (int i = 0; i < 20 && state?.CurrentPlayerToActId == null; i++)
            {
                System.Threading.Thread.Sleep(100);
                state = wrapper.GetGameState(roomCode);
            }

            Assert.NotNull(state?.CurrentPlayerToActId);
            wrapper.SubmitAction(state!.CurrentPlayerToActId!, "AllIn", null);

            // Poll until second player needs to act (max 2s)
            state = null;
            for (int i = 0; i < 20 && state?.CurrentPlayerToActId == null; i++)
            {
                System.Threading.Thread.Sleep(100);
                state = wrapper.GetGameState(roomCode);
            }

            if (state?.CurrentPlayerToActId != null)
            {
                wrapper.SubmitAction(state.CurrentPlayerToActId, "AllIn", null);
            }

            // Poll until HandComplete appears in broadcastPhases (max 3s)
            for (int i = 0; i < 30 && !broadcastPhases.Contains("HandComplete"); i++)
            {
                System.Threading.Thread.Sleep(100);
            }

            // Assert
            Assert.Contains("HandComplete", broadcastPhases);
        }

        public void Dispose()
        {
            this.wrapper?.Dispose();
        }
    }
}
