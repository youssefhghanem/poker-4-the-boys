namespace PokerGame.Api.Services
{
    public interface IRoomManager
    {
        (string RoomCode, string HostPlayerId) CreateRoom(string hostName, string emoji, int startingChips = 1000);

        (string? PlayerId, string? Error) JoinRoom(string roomCode, string playerName, string emoji);

        bool LeaveRoom(string roomCode, string playerId);

        Room? GetRoom(string roomCode);

        Room? GetRoomByPlayerId(string playerId);

        Room? GetRoomByConnectionId(string connectionId);

        string? GetPlayerIdByConnectionId(string connectionId);

        void UpdateConnection(string playerId, string connectionId);

        void DisconnectPlayer(string connectionId);
    }
}
