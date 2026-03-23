# Phase 1: Foundation & Backend Core

> **Status:** IMPLEMENTED (2026-03-23)
> **Prerequisite:** Phase 0 must be complete (7 engine bugs fixed, TcsPlayer created, CSPRNG implemented)
> **Estimated Duration:** 3-5 weeks

---

## Review Amendments (2026-03-23)

The original plan was reviewed against the actual codebase before implementation. **13 issues** were found and corrected during implementation:

### Showstoppers Fixed
1. **`GameEndResult` type doesn't exist** — `TexasHoldemGame.Start()` returns `IPlayer` (the winner). Replaced with `IPlayer` + `game.HandsPlayed`.
2. **Room code generation: 3 bytes for 6 chars** — Would crash with `IndexOutOfRangeException`. Fixed to generate 6 bytes.
3. **`PlayerActionType.AllIn` doesn't exist** — Enum only has `Fold, CheckCall, Raise, Post`. AllIn is handled as `Raise(int.MaxValue)` with string parsing.
4. **Wrong `using` directive** — `GameRoundType` is an enum, not a namespace. DTOs use string serialization instead.
5. **Event invoke missing sender** — `EventHandler<T>` needs `(this, args)`. Replaced with `Action<>` delegates for simplicity.

### Logic Bugs Fixed
6. **Double timeout race condition** — TcsPlayer already has a 30s timeout. Removed redundant wrapper timer.
7. **`LeaveRoom` passed ConnectionId to `GetRoomByPlayer`** — Added `GetRoomByConnectionId` and `GetPlayerIdByConnectionId` methods.
8. **`OnDisconnectedAsync` removed connection before lookup** — Reordered: lookup first, then remove.
9. **`_players.Clear()` wiped all rooms** — Game state is now per-room via `ActiveGame` class.
10. **CORS: `AllowAnyOrigin` + SignalR** — Replaced with `SetIsOriginAllowed(_ => true)` + `AllowCredentials()`.

### Architectural Improvements
11. **Room model drifts from engine state** — TcsPlayer events now sync chip counts back to `PlayerSession`.
12. **No hole cards in GameStateDto** — Added `HoleCards` field to `PlayerInfoDto`, populated from `HandStarted` event.
13. **Events fire on engine thread** — Used `Action<>` delegates; SignalR hub context is thread-safe by design.

### Solution Structure Change
Instead of creating a separate `PokerGame.sln`, projects were added to the existing `TexasHoldem.sln` to keep one solution.

---

## Overview

Phase 1 establishes the backend foundation for the multiplayer poker game. This phase sets up the ASP.NET Core infrastructure with SignalR for real-time communication, integrates the fixed TexasHoldem game engine, and implements room management with no-auth player sessions.

The primary architectural challenge from Phase 0 (sync-to-async bridge) has already been solved via `TcsPlayer` - this phase leverages that work to create a full multiplayer backend.

---

## Prerequisites

Before starting Phase 1, verify Phase 0 is complete:

```bash
# Verify all Phase 0 tests pass
dotnet test src/Tests/TexasHoldem.Logic.Tests/

# Verify TcsPlayer exists
ls src/TexasHoldem.Logic/Async/TcsPlayer.cs

# Verify CSPRNG is in place
ls src/TexasHoldem.Logic/Extensions/RandomProvider.cs
```

Required tools:
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- Postman or similar for API testing (optional)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        BACKEND                                   │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │              PokerGame.Api (ASP.NET Core 8.0)              ││
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐     ││
│  │  │ Game Engine │  │   SignalR   │  │  Room Manager  │     ││
│  │  │ Integration │  │     Hub      │  │  (In-Memory)   │     ││
│  │  └──────────────┘  └──────────────┘  └────────────────┘     ││
│  └─────────────────────────────────────────────────────────────┘│
│                         ▲                                         │
│                         │                                         │
│              TexasHoldem.Logic (netstandard2.0)                  │
│              (Fixed engine + TcsPlayer async bridge)            │
└─────────────────────────────────────────────────────────────────┘
```

---

## File Structure to Create

```
src/
├── PokerGame.sln                          # NEW: Solution file (renamed from TexasHoldem.sln)
├── PokerGame.Api/                         # NEW: ASP.NET Core Web API project
│   ├── PokerGame.Api.csproj
│   ├── Program.cs
│   ├── Hubs/
│   │   └── GameHub.cs                     # NEW: SignalR hub
│   ├── Services/
│   │   ├── RoomManager.cs                 # NEW: Room lifecycle management
│   │   ├── GameEngineWrapper.cs           # NEW: Wraps TexasHoldemGame for SignalR
│   │   └── PlayerSession.cs               # NEW: Player session data
│   └── DTOs/
│       ├── RoomCreatedResult.cs           # NEW
│       ├── JoinResult.cs                  # NEW
│       └── GameStateDto.cs                # NEW
└── (existing TexasHoldem.Logic, Tests, AI unchanged)
```

---

## Task 1: Create ASP.NET Core Solution & Projects

**Goal:** Set up the solution structure with the new API project and existing logic project references.

### Step 1.1: Create the Solution Structure

```bash
# Navigate to src directory
cd src

# Create new solution (keep existing TexasHoldem.sln, create new one)
dotnet new sln -n PokerGame

# Add existing projects to solution
dotnet sln add TexasHoldem.Logic/TexasHoldem.Logic.csproj
dotnet sln add Tests/TexasHoldem.Logic.Tests/TexasHoldem.Logic.Tests.csproj

# Create new API project
dotnet new webapi -n PokerGame.Api -o PokerGame.Api

# Add API project to solution
dotnet sln add PokerGame.Api/PokerGame.Api.csproj
```

### Step 1.2: Add Project References

```bash
# From src directory
dotnet add PokerGame.Api/PokerGame.Api.csproj reference TexasHoldem.Logic/TexasHoldem.Logic.csproj
```

### Step 1.3: Configure Program.cs

Modify `PokerGame.Api/Program.cs`:

```csharp
using PokerGame.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add SignalR
builder.Services.AddSignalR();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

app.MapHub<GameHub>("/gamehub");

app.MapGet("/", () => "Poker Game API is running");

app.Run();
```

### Step 1.4: Build and Verify

```bash
dotnet build src/PokerGame.sln
```

Expected: Build succeeds with 0 errors

---

## Task 2: Implement Room Manager

**Goal:** Create in-memory room management with 6-character codes, no-auth sessions, and thread-safe operations.

### Step 2.1: Create Player Session Model

Create `PokerGame.Api/Services/PlayerSession.cs`:

```csharp
namespace PokerGame.Api.Services
{
    /// <summary>
    /// Represents a player in a room session (no auth)
    /// </summary>
    public class PlayerSession
    {
        /// <summary>
        /// Unique player ID (GUID)
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name chosen by player
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Emoji avatar selected by player
        /// </summary>
        public string Emoji { get; set; } = "😀";

        /// <summary>
        /// Current chip count
        /// </summary>
        public int Chips { get; set; }

        /// <summary>
        /// SignalR connection ID for sending messages
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// Whether this player is the room host
        /// </summary>
        public bool IsHost { get; set; }

        /// <summary>
        /// Player status in current hand
        /// </summary>
        public PlayerStatus Status { get; set; } = PlayerStatus.SittingOut;

        /// <summary>
        /// Current bet amount in this round
        /// </summary>
        public int CurrentBet { get; set; }

        /// <summary>
        /// Hole cards (only sent to this player)
        /// </summary>
        public IReadOnlyList<Logic.Cards.Card>? HoleCards { get; set; }
    }

    public enum PlayerStatus
    {
        SittingOut,
        Active,
        Folded,
        AllIn,
        Won,
        Lost
    }
}
```

### Step 2.2: Create Room Model

Create `PokerGame.Api/Services/Room.cs`:

```csharp
namespace PokerGame.Api.Services
{
    /// <summary>
    /// Represents a game room
    /// </summary>
    public class Room
    {
        /// <summary>
        /// 6-character room code for sharing
        /// </summary>
        public string RoomCode { get; set; } = string.Empty;

        /// <summary>
        /// Host player ID
        /// </summary>
        public string HostPlayerId { get; set; } = string.Empty;

        /// <summary>
        /// Players in the room
        /// </summary>
        public List<PlayerSession> Players { get; set; } = new();

        /// <summary>
        /// Starting chip amount for new players
        /// </summary>
        public int StartingChips { get; set; } = 1000;

        /// <summary>
        /// Current game state
        /// </summary>
        public RoomState State { get; set; } = RoomState.Lobby;

        /// <summary>
        /// Small blind amount
        /// </summary>
        public int SmallBlind { get; set; } = 10;
    }

    public enum RoomState
    {
        Lobby,           // Waiting for players, host can start
        HandInProgress,  // Active hand being played
        HandComplete,    // Showdown done, preparing next hand
        GameComplete     // Game ended (one player has all chips)
    }
}
```

### Step 2.3: Create Room Manager Interface

Create `PokerGame.Api/Services/IRoomManager.cs`:

```csharp
namespace PokerGame.Api.Services
{
    public interface IRoomManager
    {
        /// <summary>
        /// Creates a new room with the host
        /// </summary>
        /// <param name="hostName">Name of the host player</param>
        /// <param name="emoji">Host's emoji avatar</param>
        /// <param name="startingChips">Initial chip amount (default 1000)</param>
        /// <returns>Room code and host player ID</returns>
        (string roomCode, string hostPlayerId) CreateRoom(string hostName, string emoji, int startingChips = 1000);

        /// <summary>
        /// Joins an existing room
        /// </summary>
        /// <param name="roomCode">Room code to join</param>
        /// <param name="playerName">Player's display name</param>
        /// <param name="emoji">Player's emoji avatar</param>
        /// <returns>Player ID if successful, error message otherwise</returns>
        (string? playerId, string? error) JoinRoom(string roomCode, string playerName, string emoji);

        /// <summary>
        /// Leaves a room
        /// </summary>
        /// <param name="roomCode">Room code</param>
        /// <param name="playerId">Player ID</param>
        /// <returns>True if successful</returns>
        bool LeaveRoom(string roomCode, string playerId);

        /// <summary>
        /// Gets room state
        /// </summary>
        Room? GetRoom(string roomCode);

        /// <summary>
        /// Gets room by player ID
        /// </summary>
        Room? GetRoomByPlayer(string playerId);

        /// <summary>
        /// Updates player's connection ID (for SignalR)
        /// </summary>
        void UpdateConnection(string playerId, string connectionId);

        /// <summary>
        /// Removes a player by connection ID
        /// </summary>
        void RemoveByConnection(string connectionId);
    }
}
```

### Step 2.4: Create Room Manager Implementation

Create `PokerGame.Api/Services/RoomManager.cs`:

```csharp
namespace PokerGame.Api.Services
{
    /// <summary>
    /// Thread-safe in-memory room management
    /// </summary>
    public class RoomManager : IRoomManager
    {
        private readonly Dictionary<string, Room> _rooms = new();
        private readonly Dictionary<string, string> _playerIdToRoomCode = new(); // playerId -> roomCode
        private readonly Dictionary<string, string> _connectionToPlayerId = new(); // connectionId -> playerId
        private readonly object _lock = new();

        // Room code characters (uppercase letters + digits, excluding confusing chars)
        private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public (string roomCode, string hostPlayerId) CreateRoom(string hostName, string emoji, int startingChips = 1000)
        {
            var roomCode = GenerateRoomCode();

            var hostSession = new PlayerSession
            {
                Name = hostName,
                Emoji = emoji,
                Chips = startingChips,
                IsHost = true,
                Status = PlayerStatus.SittingOut
            };

            var room = new Room
            {
                RoomCode = roomCode,
                HostPlayerId = hostSession.Id,
                Players = new List<PlayerSession> { hostSession },
                StartingChips = startingChips,
                State = RoomState.Lobby
            };

            lock (_lock)
            {
                // Ensure unique room code
                while (_rooms.ContainsKey(roomCode))
                {
                    roomCode = GenerateRoomCode();
                    room.RoomCode = roomCode;
                }

                _rooms[roomCode] = room;
                _playerIdToRoomCode[hostSession.Id] = roomCode;
            }

            return (roomCode, hostSession.Id);
        }

        public (string? playerId, string? error) JoinRoom(string roomCode, string playerName, string emoji)
        {
            Room? room;

            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomCode, out room))
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

                // Check for duplicate names
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
                    Status = PlayerStatus.SittingOut
                };

                room.Players.Add(session);
                _playerIdToRoomCode[session.Id] = roomCode;

                return (session.Id, null);
            }
        }

        public bool LeaveRoom(string roomCode, string playerId)
        {
            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomCode, out var room))
                {
                    return false;
                }

                var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                if (player == null)
                {
                    return false;
                }

                // If host leaves, close the room
                if (player.IsHost)
                {
                    _rooms.Remove(roomCode);
                    foreach (var p in room.Players)
                    {
                        _playerIdToRoomCode.Remove(p.Id);
                    }
                    return true;
                }

                room.Players.Remove(player);
                _playerIdToRoomCode.Remove(playerId);

                // Clean up connection mapping
                if (player.ConnectionId != null)
                {
                    _connectionToPlayerId.Remove(player.ConnectionId);
                }

                return true;
            }
        }

        public Room? GetRoom(string roomCode)
        {
            lock (_lock)
            {
                return _rooms.TryGetValue(roomCode, out var room) ? room : null;
            }
        }

        public Room? GetRoomByPlayer(string playerId)
        {
            lock (_lock)
            {
                if (!_playerIdToRoomCode.TryGetValue(playerId, out var roomCode))
                {
                    return null;
                }

                return _rooms.TryGetValue(roomCode, out var room) ? room : null;
            }
        }

        public void UpdateConnection(string playerId, string connectionId)
        {
            lock (_lock)
            {
                // Remove old connection mapping
                var oldConnection = _connectionToPlayerId.FirstOrDefault(x => x.Value == playerId).Key;
                if (oldConnection != null)
                {
                    _connectionToPlayerId.Remove(oldConnection);
                }

                // Add new connection mapping
                _connectionToPlayerId[connectionId] = playerId;

                // Update player's connection ID
                foreach (var room in _rooms.Values)
                {
                    var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                    if (player != null)
                    {
                        player.ConnectionId = connectionId;
                        break;
                    }
                }
            }
        }

        public void RemoveByConnection(string connectionId)
        {
            lock (_lock)
            {
                if (_connectionToPlayerId.TryGetValue(connectionId, out var playerId))
                {
                    _connectionToPlayerId.Remove(connectionId);

                    foreach (var room in _rooms.Values)
                    {
                        var player = room.Players.FirstOrDefault(p => p.Id == playerId);
                        if (player != null)
                        {
                            player.ConnectionId = null;
                            break;
                        }
                    }
                }
            }
        }

        private string GenerateRoomCode()
        {
            // Use crypto-secure random for room codes
            var bytes = new byte[3];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
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
```

### Step 2.5: Register in Program.cs

Add to `PokerGame.Api/Program.cs`:

```csharp
builder.Services.AddSingleton<IRoomManager, RoomManager>();
```

### Step 2.6: Build and Test

```bash
dotnet build src/PokerGame.sln
```

---

## Task 3: Define API Contract & DTOs

**Goal:** Create the data transfer objects that define the API contract between backend and future frontend.

### Step 3.1: Create Room Created Result DTO

Create `PokerGame.Api/DTOs/RoomCreatedResult.cs`:

```csharp
namespace PokerGame.Api.DTOs
{
    public class RoomCreatedResult
    {
        /// <summary>
        /// 6-character room code for sharing
        /// </summary>
        public string RoomCode { get; set; } = string.Empty;

        /// <summary>
        /// Host's player ID
        /// </summary>
        public string HostPlayerId { get; set; } = string.Empty;

        /// <summary>
        /// Whether room creation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
```

### Step 3.2: Create Join Result DTO

Create `PokerGame.Api/DTOs/JoinResult.cs`:

```csharp
namespace PokerGame.Api.DTOs
{
    public class JoinResult
    {
        /// <summary>
        /// Player's ID if successful
        /// </summary>
        public string? PlayerId { get; set; }

        /// <summary>
        /// Whether join was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
```

### Step 3.3: Create Game State DTO

Create `PokerGame.Api/DTOs/GameStateDto.cs`:

```csharp
namespace PokerGame.Api.DTOs
{
    using System.Collections.Generic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.GameRoundType;

    /// <summary>
    /// Game state sent to all clients
    /// </summary>
    public class GameStateDto
    {
        /// <summary>
        /// Unique identifier for this game state
        /// </summary>
        public long StateId { get; set; }

        /// <summary>
        /// Current room state
        /// </summary>
        public string Phase { get; set; } = "Lobby";

        /// <summary>
        /// Current betting round
        /// </summary>
        public GameRoundType? CurrentRound { get; set; }

        /// <summary>
        /// Community cards (flop/turn/river)
        /// </summary>
        public List<CardDto>? CommunityCards { get; set; }

        /// <summary>
        /// Main pot amount
        /// </summary>
        public int MainPot { get; set; }

        /// <summary>
        /// Side pots for all-in scenarios
        /// </summary>
        public List<SidePotDto>? SidePots { get; set; }

        /// <summary>
        /// Current dealer position (index into Players)
        /// </summary>
        public int DealerPosition { get; set; }

        /// <summary>
        /// Current player to act (null if not in betting round)
        /// </summary>
        public string? CurrentPlayerToActId { get; set; }

        /// <summary>
        /// Players in the game
        /// </summary>
        public List<PlayerInfoDto>? Players { get; set; }

        /// <summary>
        /// Hand number
        /// </summary>
        public int HandNumber { get; set; }

        /// <summary>
        /// Small blind amount
        /// </summary>
        public int SmallBlind { get; set; }

        /// <summary>
        /// Minimum raise amount
        /// </summary>
        public int MinRaise { get; set; }
    }

    public class CardDto
    {
        public CardSuit Suit { get; set; }
        public CardType Type { get; set; }

        public string ToDisplayString()
        {
            var suitChar = Suit switch
            {
                CardSuit.Spade => "♠",
                CardSuit.Heart => "♥",
                CardSuit.Diamond => "♦",
                CardSuit.Club => "♣",
                _ => "?"
            };

            var rankChar = Type switch
            {
                CardType.Two => "2",
                CardType.Three => "3",
                CardType.Four => "4",
                CardType.Five => "5",
                CardType.Six => "6",
                CardType.Seven => "7",
                CardType.Eight => "8",
                CardType.Nine => "9",
                CardType.Ten => "10",
                CardType.Jack => "J",
                CardType.Queen => "Q",
                CardType.King => "K",
                CardType.Ace => "A",
                _ => "?"
            };

            return rankChar + suitChar;
        }
    }

    public class SidePotDto
    {
        public int Amount { get; set; }
        public List<string> EligiblePlayerIds { get; set; } = new();
    }

    public class PlayerInfoDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
        public int Chips { get; set; }
        public string Status { get; set; } = "SittingOut";
        public int CurrentBet { get; set; }
        public bool IsHost { get; set; }
        public int Position { get; set; }
    }
}
```

### Step 3.4: Create Action Result DTO

Create `PokerGame.Api/DTOs/ActionResult.cs`:

```csharp
namespace PokerGame.Api.DTOs
{
    public class ActionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
```

### Step 3.5: Create Turn Notification DTO

Create `PokerGame.Api/DTOs/YourTurnDto.cs`:

```csharp
namespace PokerGame.Api.DTOs
{
    /// <summary>
    /// Notification sent to player when it's their turn
    /// </summary>
    public class YourTurnDto
    {
        /// <summary>
        /// Available actions for this turn
        /// </summary>
        public List<string> AvailableActions { get; set; } = new();

        /// <summary>
        /// Minimum raise amount
        /// </summary>
        public int MinRaise { get; set; }

        /// <summary>
        /// Maximum raise amount (player's chips)
        /// </summary>
        public int MaxRaise { get; set; }

        /// <summary>
        /// Amount needed to call
        /// </summary>
        public int AmountToCall { get; set; }

        /// <summary>
        /// Time remaining in seconds
        /// </summary>
        public int TimeRemaining { get; set; } = 30;
    }
}
```

### Step 3.6: Build and Verify

```bash
dotnet build src/PokerGame.sln
```

---

## Task 4: Implement Game Engine Wrapper

**Goal:** Create a wrapper that integrates the TexasHoldem game engine with SignalR, using the TcsPlayer from Phase 0.

### Step 4.1: Create Game Engine Wrapper Interface

Create `PokerGame.Api/Services/IGameEngineWrapper.cs`:

```csharp
namespace PokerGame.Api.Services
{
    using System;
    using System.Threading.Tasks;
    using PokerGame.Api.DTOs;
    using TexasHoldem.Logic.Players;

    public interface IGameEngineWrapper : IDisposable
    {
        /// <summary>
        /// Starts a new game in the room
        /// </summary>
        Task StartGameAsync(string roomCode);

        /// <summary>
        /// Submits a player's action
        /// </summary>
        /// <param name="roomCode">Room code</param>
        /// <param name="playerId">Player ID</param>
        /// <param name="actionType">Action type (Fold, Check, Call, Raise, AllIn)</param>
        /// <param name="amount">Amount for raise (optional)</param>
        Task<bool> SubmitActionAsync(string roomCode, string playerId, PlayerActionType actionType, int? amount = null);

        /// <summary>
        /// Gets the current game state
        /// </summary>
        GameStateDto? GetGameState(string roomCode);

        /// <summary>
        /// Event fired when game state changes
        /// </summary>
        event EventHandler<GameStateDto>? GameStateChanged;

        /// <summary>
        /// Event fired when a player needs to act
        /// </summary>
        event EventHandler<(string playerId, YourTurnDto turnInfo)>? PlayerTurnRequested;
    }
}
```

### Step 4.2: Create Game Engine Wrapper Implementation

Create `PokerGame.Api/Services/GameEngineWrapper.cs`:

```csharp
namespace PokerGame.Api.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.AspNetCore.SignalR;

    using PokerGame.Api.DTOs;
    using PokerGame.Api.Hubs;
    using TexasHoldem.Logic.Async;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    /// <summary>
    /// Wraps TexasHoldemGame for SignalR integration using TcsPlayer async bridge
    /// </summary>
    public class GameEngineWrapper : IGameEngineWrapper
    {
        private readonly IRoomManager _roomManager;
        private readonly IHubContext<GameHub> _hubContext;

        // One TcsPlayer per player in the game
        private readonly ConcurrentDictionary<string, TcsPlayer> _players = new();

        // Active game instances
        private readonly ConcurrentDictionary<string, TexasHoldemGame> _games = new();

        // Game state tracking
        private readonly ConcurrentDictionary<string, int> _handNumbers = new();
        private readonly ConcurrentDictionary<string, int> _smallBlinds = new();

        // Turn timer cancellation
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _turnTimers = new();

        private const int TurnTimeoutSeconds = 30;

        public event EventHandler<GameStateDto>? GameStateChanged;
        public event EventHandler<(string playerId, YourTurnDto turnInfo)>? PlayerTurnRequested;

        public GameEngineWrapper(IRoomManager roomManager, IHubContext<GameHub> hubContext)
        {
            _roomManager = roomManager;
            _hubContext = hubContext;
        }

        public async Task StartGameAsync(string roomCode)
        {
            var room = _roomManager.GetRoom(roomCode);
            if (room == null)
            {
                return;
            }

            if (room.Players.Count < 2)
            {
                await _hubContext.Clients.Group(roomCode).SendAsync("OnError", "Need at least 2 players to start");
                return;
            }

            // Create TcsPlayer for each player
            _players.Clear();
            foreach (var session in room.Players)
            {
                var tcsPlayer = new TcsPlayer(session.Name, turnTimeoutMs: TurnTimeoutSeconds * 1000);

                // Subscribe to turn requests
                tcsPlayer.TurnRequested += async (sender, args) =>
                {
                    await OnPlayerTurnRequested(roomCode, session.Id, args.Context);
                };

                // Subscribe to hand events for state updates
                tcsPlayer.HandStarted += (sender, args) => OnHandStarted(roomCode);
                tcsPlayer.RoundStarted += (sender, args) => OnRoundStarted(roomCode, args.Context);
                tcsPlayer.HandEnded += (sender, args) => OnHandEnded(roomCode, args.Context);

                _players[session.Id] = tcsPlayer;
            }

            // Create the game
            var gamePlayers = _players.Values.Cast<IPlayer>().ToList();
            var game = new TexasHoldemGame(gamePlayers, room.StartingChips);

            // Update small blind
            _smallBlinds[roomCode] = room.SmallBlind;
            _handNumbers[roomCode] = 0;

            // Store game reference
            _games[roomCode] = game;

            // Update room state
            room.State = RoomState.HandInProgress;

            // Start the game on a background thread
            _ = Task.Run(() =>
            {
                try
                {
                    var result = game.Start();

                    // Game ended
                    room.State = RoomState.GameComplete;

                    // Broadcast final standings
                    BroadcastGameEnd(roomCode, result);
                }
                catch (Exception ex)
                {
                    room.State = RoomState.Lobby;
                    _hubContext.Clients.Group(roomCode).SendAsync("OnError", $"Game error: {ex.Message}");
                }
            });
        }

        private async Task OnPlayerTurnRequested(string roomCode, string playerId, IGetTurnContext context)
        {
            var room = _roomManager.GetRoom(roomCode);
            if (room == null) return;

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return;

            // Create turn notification
            var turnInfo = new YourTurnDto
            {
                MinRaise = context.MinRaise,
                MaxRaise = context.MoneyLeft,
                AmountToCall = context.MoneyToCall,
                TimeRemaining = TurnTimeoutSeconds,
                AvailableActions = new List<string>()
            };

            if (context.CanCheck) turnInfo.AvailableActions.Add("Check");
            turnInfo.AvailableActions.Add("Fold");
            if (context.CanRaise) turnInfo.AvailableActions.Add("Raise");
            if (context.MoneyToCall > 0)
            {
                turnInfo.AvailableActions.Add("Call");
                turnInfo.AvailableActions.Add("AllIn");
            }

            // Send to specific player
            if (player.ConnectionId != null)
            {
                await _hubContext.Clients.Client(player.ConnectionId).SendAsync("OnYourTurn", turnInfo);
            }

            // Fire event for external handlers
            PlayerTurnRequested?.Invoke((playerId, turnInfo));

            // Start turn timer
            var cts = new CancellationTokenSource();
            _turnTimers[playerId] = cts;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(TurnTimeoutSeconds), cts.Token);

                // Timeout - auto fold
                await SubmitActionAsync(roomCode, playerId, PlayerActionType.Fold, null);
            }
            catch (TaskCanceledException)
            {
                // Action submitted before timeout
            }
        }

        private void OnHandStarted(string roomCode)
        {
            _handNumbers.AddOrUpdate(roomCode, 1, (k, v) => v + 1);
            BroadcastGameState(roomCode);
        }

        private void OnRoundStarted(string roomCode, IStartRoundContext context)
        {
            BroadcastGameState(roomCode);
        }

        private void OnHandEnded(string roomCode, IEndHandContext context)
        {
            BroadcastGameState(roomCode);
        }

        public async Task<bool> SubmitActionAsync(string roomCode, string playerId, PlayerActionType actionType, int? amount)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                return false;
            }

            // Cancel turn timer
            if (_turnTimers.TryRemove(playerId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            // Convert action type to PlayerAction
            PlayerAction action = actionType switch
            {
                PlayerActionType.Fold => PlayerAction.Fold(),
                PlayerActionType.CheckCall => PlayerAction.CheckOrCall(),
                PlayerActionType.Raise => amount.HasValue ? PlayerAction.Raise(amount.Value) : PlayerAction.CheckOrCall(),
                PlayerActionType.AllIn => PlayerAction.Raise(int.MaxValue),
                _ => PlayerAction.Fold()
            };

            return player.SubmitAction(action);
        }

        public GameStateDto? GetGameState(string roomCode)
        {
            var room = _roomManager.GetRoom(roomCode);
            if (room == null) return null;

            var state = new GameStateDto
            {
                StateId = _handNumbers.GetValueOrDefault(roomCode, 0),
                Phase = room.State.ToString(),
                SmallBlind = _smallBlinds.GetValueOrDefault(roomCode, 10),
                HandNumber = _handNumbers.GetValueOrDefault(roomCode, 0),
                Players = room.Players.Select(p => new PlayerInfoDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Emoji = p.Emoji,
                    Chips = p.Chips,
                    Status = p.Status.ToString(),
                    CurrentBet = p.CurrentBet,
                    IsHost = p.IsHost
                }).ToList()
            };

            return state;
        }

        private void BroadcastGameState(string roomCode)
        {
            var state = GetGameState(roomCode);
            if (state != null)
            {
                GameStateChanged?.Invoke(this, state);
                _hubContext.Clients.Group(roomCode).SendAsync("OnGameStateChanged", state);
            }
        }

        private void BroadcastGameEnd(string roomCode, GameEndResult result)
        {
            _hubContext.Clients.Group(roomCode).SendAsync("OnGameEnd", new
            {
                WinnerName = result.WinnerName,
                Standings = result.Standings.Select(s => new
                {
                    Name = s.Name,
                    FinalChips = s.FinalChips,
                    Position = s.Position
                }),
                TotalHandsPlayed = result.TotalHandsPlayed
            });
        }

        public void Dispose()
        {
            foreach (var cts in _turnTimers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            foreach (var game in _games.Values)
            {
                // Game doesn't implement IDisposable
            }
        }
    }
}
```

### Step 4.3: Register in Program.cs

Add to `PokerGame.Api/Program.cs`:

```csharp
builder.Services.AddSingleton<IGameEngineWrapper, GameEngineWrapper>();
```

### Step 4.4: Build and Verify

```bash
dotnet build src/PokerGame.sln
```

---

## Task 5: Create SignalR Hub

**Goal:** Implement the SignalR hub that handles real-time communication between clients and server.

### Step 5.1: Create GameHub

Create `PokerGame.Api/Hubs/GameHub.cs`:

```csharp
namespace PokerGame.Api.Hubs
{
    using System;
    using System.Threading.Tasks;

    using Microsoft.AspNetCore.SignalR;

    using PokerGame.Api.DTOs;
    using PokerGame.Api.Services;

    /// <summary>
    /// SignalR Hub for real-time poker game communication
    /// </summary>
    public class GameHub : Hub
    {
        private readonly IRoomManager _roomManager;
        private readonly IGameEngineWrapper _gameEngine;

        public GameHub(IRoomManager roomManager, IGameEngineWrapper gameEngine)
        {
            _roomManager = roomManager;
            _gameEngine = gameEngine;
        }

        #region Room Management

        /// <summary>
        /// Creates a new game room
        /// </summary>
        /// <param name="hostName">Host's display name</param>
        /// <param name="emoji">Host's emoji avatar</param>
        /// <param name="startingChips">Starting chips (default 1000)</param>
        public async Task<RoomCreatedResult> CreateRoom(string hostName, string emoji, int startingChips = 1000)
        {
            try
            {
                var (roomCode, hostPlayerId) = _roomManager.CreateRoom(hostName, emoji, startingChips);

                // Join the SignalR group
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                // Update connection mapping
                _roomManager.UpdateConnection(hostPlayerId, Context.ConnectionId);

                return new RoomCreatedResult
                {
                    RoomCode = roomCode,
                    HostPlayerId = hostPlayerId,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new RoomCreatedResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Joins an existing room
        /// </summary>
        /// <param name="roomCode">Room code to join</param>
        /// <param name="playerName">Player's display name</param>
        /// <param name="emoji">Player's emoji avatar</param>
        public async Task<JoinResult> JoinRoom(string roomCode, string playerName, string emoji)
        {
            try
            {
                var (playerId, error) = _roomManager.JoinRoom(roomCode, playerName, emoji);

                if (playerId == null)
                {
                    return new JoinResult
                    {
                        Success = false,
                        ErrorMessage = error ?? "Failed to join room"
                    };
                }

                // Join the SignalR group
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                // Update connection mapping
                _roomManager.UpdateConnection(playerId, Context.ConnectionId);

                // Notify other players
                await Clients.OthersInGroup(roomCode).SendAsync("OnPlayerJoined", new
                {
                    PlayerId = playerId,
                    Name = playerName,
                    Emoji = emoji
                });

                return new JoinResult
                {
                    PlayerId = playerId,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new JoinResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Leaves the current room
        /// </summary>
        public async Task LeaveRoom()
        {
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room == null) return;

            var playerId = GetPlayerIdByConnection(Context.ConnectionId);
            if (playerId == null) return;

            var wasHost = room.Players.Any(p => p.Id == playerId && p.IsHost);

            _roomManager.LeaveRoom(room.RoomCode, playerId);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.RoomCode);

            // Notify others
            if (!wasHost)
            {
                await Clients.OthersInGroup(room.RoomCode).SendAsync("OnPlayerLeft", new
                {
                    PlayerId = playerId
                });
            }
        }

        /// <summary>
        /// Kicks a player (host only)
        /// </summary>
        public async Task<bool> KickPlayer(string playerId)
        {
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room == null) return false;

            var currentPlayer = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (currentPlayer == null || !currentPlayer.IsHost)
            {
                return false;
            }

            var playerToKick = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (playerToKick == null || playerToKick.IsHost)
            {
                return false;
            }

            _roomManager.LeaveRoom(room.RoomCode, playerId);

            // Notify kicked player and others
            if (playerToKick.ConnectionId != null)
            {
                await Clients.Client(playerToKick.ConnectionId).SendAsync("OnKicked");
            }

            await Clients.OthersInGroup(room.RoomCode).SendAsync("OnPlayerLeft", new
            {
                PlayerId = playerId
            });

            return true;
        }

        #endregion

        #region Game Control

        /// <summary>
        /// Starts the game (host only)
        /// </summary>
        public async Task<bool> StartGame()
        {
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room == null) return false;

            var currentPlayer = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (currentPlayer == null || !currentPlayer.IsHost)
            {
                return false;
            }

            if (room.Players.Count < 2)
            {
                await Clients.Caller.SendAsync("OnError", "Need at least 2 players to start");
                return false;
            }

            await _gameEngine.StartGameAsync(room.RoomCode);
            return true;
        }

        /// <summary>
        /// Submits player action during their turn
        /// </summary>
        public async Task<ActionResult> SubmitAction(string actionType, int? amount = null)
        {
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room == null)
            {
                return new ActionResult { Success = false, ErrorMessage = "Not in a room" };
            }

            var playerId = GetPlayerIdByConnection(Context.ConnectionId);
            if (playerId == null)
            {
                return new ActionResult { Success = false, ErrorMessage = "Player not found" };
            }

            // Parse action type
            if (!Enum.TryParse<PlayerActionType>(actionType, true, out var parsedAction))
            {
                return new ActionResult { Success = false, ErrorMessage = "Invalid action type" };
            }

            var success = await _gameEngine.SubmitActionAsync(room.RoomCode, playerId, parsedAction, amount);

            return new ActionResult
            {
                Success = success,
                ErrorMessage = success ? null : "Failed to submit action"
            };
        }

        /// <summary>
        /// Gets current game state
        /// </summary>
        public GameStateDto? GetGameState()
        {
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room == null) return null;

            var state = _gameEngine.GetGameState(room.RoomCode);

            // Hide other players' hole cards
            var playerId = GetPlayerIdByConnection(Context.ConnectionId);
            if (state?.Players != null && playerId != null)
            {
                foreach (var player in state.Players)
                {
                    if (player.Id != playerId)
                    {
                        // Don't send hole cards to other players
                    }
                }
            }

            return state;
        }

        #endregion

        #region Connection Handling

        public override async Task OnConnectedAsync()
        {
            // Connection established
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Remove from room
            _roomManager.RemoveByConnection(Context.ConnectionId);

            // Find the room and notify others
            var room = _roomManager.GetRoomByPlayer(Context.ConnectionId);
            if (room != null)
            {
                var playerId = GetPlayerIdByConnection(Context.ConnectionId);
                if (playerId != null)
                {
                    // Auto-fold if disconnected during game
                    if (room.State == RoomState.HandInProgress)
                    {
                        await _gameEngine.SubmitActionAsync(room.RoomCode, playerId, PlayerActionType.Fold, null);
                    }

                    await Clients.OthersInGroup(room.RoomCode).SendAsync("OnPlayerDisconnected", new
                    {
                        PlayerId = playerId
                    });
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        #endregion

        #region Helper Methods

        private string? GetPlayerIdByConnection(string connectionId)
        {
            var room = _roomManager.GetRoomByPlayer(connectionId);
            if (room == null) return null;

            var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
            return player?.Id;
        }

        #endregion
    }
}
```

### Step 5.2: Build and Verify

```bash
dotnet build src/PokerGame.sln
```

---

## Task 6: Write Unit Tests

**Goal:** Create comprehensive unit tests for the backend components.

### Step 6.1: Create Test Project (if needed)

If not already set up, create a test project for the API:

```bash
dotnet new xunit -n PokerGame.Api.Tests -o ../Tests/PokerGame.Api.Tests
dotnet add PokerGame.Api.Tests/PokerGame.Api.Tests.csproj reference PokerGame.Api/PokerGame.Api.csproj
dotnet add PokerGame.Api.Tests/PokerGame.Api.Tests.csproj reference TexasHoldem.Logic/TexasHoldem.Logic.csproj
```

### Step 6.2: Test Room Manager

Create `Tests/PokerGame.Api.Tests/RoomManagerTests.cs`:

```csharp
namespace PokerGame.Api.Tests
{
    using PokerGame.Api.Services;

    using Xunit;

    public class RoomManagerTests
    {
        private readonly RoomManager _roomManager;

        public RoomManagerTests()
        {
            _roomManager = new RoomManager();
        }

        [Fact]
        public void CreateRoom_ReturnsValidRoomCode()
        {
            // Act
            var (roomCode, hostPlayerId) = _roomManager.CreateRoom("Host", "😎", 1000);

            // Assert
            Assert.Equal(6, roomCode.Length);
            Assert.NotEmpty(hostPlayerId);
        }

        [Fact]
        public void CreateRoom_HostIsSet()
        {
            // Act
            var (roomCode, hostPlayerId) = _roomManager.CreateRoom("Host", "😎");

            // Assert
            var room = _roomManager.GetRoom(roomCode);
            Assert.NotNull(room);
            var host = room.Players.First(p => p.Id == hostPlayerId);
            Assert.True(host.IsHost);
        }

        [Fact]
        public void JoinRoom_ValidCode_AddsPlayer()
        {
            // Arrange
            var (roomCode, _) = _roomManager.CreateRoom("Host", "😎");

            // Act
            var (playerId, error) = _roomManager.JoinRoom(roomCode, "Player2", "🤔");

            // Assert
            Assert.NotNull(playerId);
            Assert.Null(error);

            var room = _roomManager.GetRoom(roomCode);
            Assert.Equal(2, room.Players.Count);
        }

        [Fact]
        public void JoinRoom_InvalidCode_ReturnsError()
        {
            // Act
            var (playerId, error) = _roomManager.JoinRoom("INVALID", "Player", "😀");

            // Assert
            Assert.Null(playerId);
            Assert.Equal("Room not found", error);
        }

        [Fact]
        public void JoinRoom_DuplicateName_ReturnsError()
        {
            // Arrange
            var (roomCode, _) = _roomManager.CreateRoom("Host", "😎");
            _roomManager.JoinRoom(roomCode, "Duplicate", "🤔");

            // Act
            var (playerId, error) = _roomManager.JoinRoom(roomCode, "Duplicate", "😀");

            // Assert
            Assert.Null(playerId);
            Assert.Equal("Name already taken in this room", error);
        }

        [Fact]
        public void JoinRoom_MaxPlayers_ReturnsError()
        {
            // Arrange
            var (roomCode, _) = _roomManager.CreateRoom("Host", "😎");
            for (int i = 0; i < 5; i++)
            {
                _roomManager.JoinRoom(roomCode, $"Player{i}", "😀");
            }

            // Act
            var (playerId, error) = _roomManager.JoinRoom(roomCode, "Player6", "😀");

            // Assert
            Assert.Null(playerId);
            Assert.Equal("Room is full (max 6 players)", error);
        }

        [Fact]
        public void LeaveRoom_Host_ClosesRoom()
        {
            // Arrange
            var (roomCode, hostId) = _roomManager.CreateRoom("Host", "😎");
            _roomManager.JoinRoom(roomCode, "Player2", "😀");

            // Act
            var result = _roomManager.LeaveRoom(roomCode, hostId);

            // Assert
            Assert.True(result);
            Assert.Null(_roomManager.GetRoom(roomCode));
        }

        [Fact]
        public void LeaveRoom_NonHost_RemovesPlayer()
        {
            // Arrange
            var (roomCode, _) = _roomManager.CreateRoom("Host", "😎");
            var (playerId, _) = _roomManager.JoinRoom(roomCode, "Player2", "😀");

            // Act
            var result = _roomManager.LeaveRoom(roomCode, playerId);

            // Assert
            Assert.True(result);
            var room = _roomManager.GetRoom(roomCode);
            Assert.Single(room.Players);
        }

        [Fact]
        public void UpdateConnection_MapsCorrectly()
        {
            // Arrange
            var (roomCode, hostId) = _roomManager.CreateRoom("Host", "😎");

            // Act
            _roomManager.UpdateConnection(hostId, "connection-123");

            // Assert
            var room = _roomManager.GetRoom(roomCode);
            var host = room.Players.First(p => p.Id == hostId);
            Assert.Equal("connection-123", host.ConnectionId);
        }
    }
}
```

### Step 6.3: Run Tests

```bash
dotnet test src/PokerGame.sln
```

---

## Task 7: Add Turn Timer System

**Goal:** Implement turn timeout handling with auto-fold.

### Step 7.1: Enhance GameEngineWrapper with Timer Logic

The timer logic is already implemented in the GameEngineWrapper from Task 4. Ensure it's properly wired up:

1. Turn requested → start 30-second timer
2. Action submitted → cancel timer
3. Timer expires → auto-fold

### Step 7.2: Send Timer Updates to Client

Update the `OnPlayerTurnRequested` method in `GameEngineWrapper.cs` to send periodic updates:

```csharp
// Add after the initial turn notification
_ = Task.Run(async () =>
{
    var remaining = TurnTimeoutSeconds;
    while (remaining > 0 && !cts.Token.IsCancellationRequested)
    {
        await Task.Delay(1);
        remaining--;

        // Send update every 5 seconds
        if (remaining % 5 == 0 && player.ConnectionId != null)
        {
            await _hubContext.Clients.Client(player.ConnectionId).SendAsync("OnTurnTimer", new
            {
                TimeRemaining = remaining
            });
        }
    }
});
```

### Step 7.3: Handle Disconnect During Turn

In `GameHub.OnDisconnectedAsync`, auto-fold is already implemented for players disconnected during active hands.

---

## Task 8: Final Integration Testing

**Goal:** Verify the complete backend works end-to-end.

### Step 8.1: Run All Tests

```bash
dotnet test src/PokerGame.sln
```

### Step 8.2: Manual API Testing (Optional)

Start the API and test with Postman or similar:

```bash
dotnet run --project src/PokerGame.Api/
```

Test flows:
1. `POST /gamehub/CreateRoom` → Get room code
2. `POST /gamehub/JoinRoom` → Join with code
3. `GET /gamehub/GetGameState` → Verify lobby state

### Step 8.3: Test Game Flow

Integration test for full game flow:

```csharp
[Fact]
public async Task FullGameFlow_CompletesWithoutError()
{
    // Arrange - create room with 2 players
    var roomManager = new RoomManager();
    var (roomCode, hostId) = roomManager.CreateRoom("Host", "😎", 100);
    var (player2Id, _) = roomManager.JoinRoom(roomCode, "Player2", "😀");

    // Create mock game engine wrapper (for testing)
    // ... setup with test doubles

    // Act - start game
    // ... verify game completes

    // Assert
    var room = roomManager.GetRoom(roomCode);
    Assert.NotNull(room);
    // Verify game ended properly
}
```

---

## Summary

| Task | Description | Files Created/Modified | Complexity |
|------|-------------|----------------------|------------|
| 1 | ASP.NET Core Solution Setup | PokerGame.sln, PokerGame.Api project | Low |
| 2 | Room Manager | PlayerSession.cs, Room.cs, IRoomManager.cs, RoomManager.cs | Medium |
| 3 | API Contract & DTOs | RoomCreatedResult.cs, JoinResult.cs, GameStateDto.cs, ActionResult.cs, YourTurnDto.cs | Low |
| 4 | Game Engine Wrapper | IGameEngineWrapper.cs, GameEngineWrapper.cs | High |
| 5 | SignalR Hub | GameHub.cs | High |
| 6 | Unit Tests | RoomManagerTests.cs | Medium |
| 7 | Turn Timer System | (integrated in GameEngineWrapper) | Medium |
| 8 | Final Integration | Verification | Low |

---

## Next Steps (Phase 2)

After Phase 1 completes, the backend will support:
- Room creation and joining
- Real-time game state synchronization
- All player actions (fold, check, call, raise, all-in)
- Turn timer with auto-fold
- Host controls (kick, start game)

Phase 2 will focus on:
- Enhanced game state broadcasting
- Reconnection handling
- Showdown sequence
- Game summary screen
- Play again functionality

---

## Notes for Future Development

1. **Thread Safety**: The RoomManager uses locking for thread safety. For higher scale (1000+ concurrent rooms), consider ConcurrentDictionary improvements or Redis.

2. **State Persistence**: Current implementation is in-memory only. Server restart = all games lost. For production, add Redis state storage.

3. **Scalability**: The TcsPlayer approach blocks one thread per active player turn. For 100+ concurrent games, consider async rewrite of game engine.

4. **Security**: Room codes use crypto-secure random. Add rate limiting for join attempts to prevent brute-force.

5. **Validation**: Player actions should be validated server-side before passing to engine. Currently trusting engine completely.