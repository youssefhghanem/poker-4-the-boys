namespace PokerGame.Api.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using PokerGame.Api.DTOs;
    using TexasHoldem.Logic.Async;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    public class GameEngineWrapper : IGameEngineWrapper
    {
        // Per-room game state keyed by roomCode
        private readonly ConcurrentDictionary<string, ActiveGame> activeGames = new ConcurrentDictionary<string, ActiveGame>();

        public event Action<string, GameStateDto>? GameStateChanged;

        public event Action<string, string, YourTurnDto>? PlayerTurnRequested;

        public event Action<string, GameEndDto>? GameEnded;

        public event Action<string, TurnTimerDto>? TurnTimerTick;

        public Task StartGameAsync(Room room)
        {
            if (room.Players.Count < 2)
            {
                return Task.CompletedTask;
            }

            var gameState = new ActiveGame(room.RoomCode, room);

            // Create TcsPlayer for each player, keyed by session ID
            foreach (var session in room.Players)
            {
                var tcsPlayer = new TcsPlayer(session.Name, turnTimeoutMs: 30000);
                gameState.PlayerMap[session.Id] = tcsPlayer;
                gameState.NameToSessionId[session.Name] = session.Id;
            }

            // Wire up events
            foreach (var kvp in gameState.PlayerMap)
            {
                var sessionId = kvp.Key;
                var tcsPlayer = kvp.Value;

                tcsPlayer.TurnRequested += (sender, args) =>
                {
                    this.HandleTurnRequested(room, gameState, sessionId, args.Context);
                };

                tcsPlayer.HandStarted += (sender, args) =>
                {
                    this.HandleHandStarted(room, gameState, sessionId, args.Context);
                };

                tcsPlayer.RoundStarted += (sender, args) =>
                {
                    this.HandleRoundStarted(room, gameState, args.Context);
                };

                tcsPlayer.HandEnded += (sender, args) =>
                {
                    this.HandleHandEnded(room, gameState, args.Context);
                };
            }

            this.activeGames[room.RoomCode] = gameState;

            // Create the engine game
            var players = room.Players
                .Select(s => (IPlayer)gameState.PlayerMap[s.Id])
                .ToList();
            var game = new TexasHoldemGame(players, room.StartingChips);
            gameState.Game = game;

            room.State = RoomState.HandInProgress;

            // Run on a background thread (engine is synchronous/blocking)
            _ = Task.Run(() =>
            {
                try
                {
                    var winner = game.Start();
                    room.State = RoomState.GameComplete;

                    var winnerSessionId = gameState.NameToSessionId.GetValueOrDefault(winner.Name, string.Empty);

                    var gameEndDto = new GameEndDto
                    {
                        WinnerPlayerId = winnerSessionId,
                        WinnerName = winner.Name,
                        TotalHandsPlayed = game.HandsPlayed,
                        Standings = room.Players
                            .OrderByDescending(p => p.Chips)
                            .Select((p, i) => new PlayerStandingDto
                            {
                                PlayerId = p.Id,
                                Name = p.Name,
                                FinalChips = p.Chips,
                                Position = i + 1,
                            }).ToList(),
                    };

                    this.GameEnded?.Invoke(room.RoomCode, gameEndDto);

                    // Keep activeGame alive so clients can call GetGameState after game ends.
                    // It will be cleaned up by ClearGameState (called from PlayAgain).
                }
                catch (Exception)
                {
                    room.State = RoomState.Lobby;
                    this.activeGames.TryRemove(room.RoomCode, out _);
                }
            });

            return Task.CompletedTask;
        }

        public bool SubmitAction(string playerId, string actionType, int? amount)
        {
            // Cancel any active turn timer for this player
            this.CancelTimer(playerId);

            var tcsPlayer = this.FindTcsPlayer(playerId);
            if (tcsPlayer == null)
            {
                return false;
            }

            PlayerAction action;
            switch (actionType.ToUpperInvariant())
            {
                case "FOLD":
                    action = PlayerAction.Fold();
                    break;
                case "CHECK":
                case "CALL":
                case "CHECKCALL":
                    action = PlayerAction.CheckOrCall();
                    break;
                case "RAISE":
                    action = amount.HasValue
                        ? PlayerAction.Raise(amount.Value)
                        : PlayerAction.CheckOrCall();
                    break;
                case "ALLIN":
                    // Raise with max amount; the engine will clamp to MoneyLeft
                    action = PlayerAction.Raise(int.MaxValue);
                    break;
                default:
                    return false;
            }

            return tcsPlayer.SubmitAction(action);
        }

        public GameStateDto? GetGameState(string roomCode, string? requestingPlayerId = null)
        {
            if (!this.activeGames.TryGetValue(roomCode, out var gameState))
            {
                return null;
            }

            var dto = gameState.BuildDto();

            // Filter hole cards: only the requesting player sees their own
            if (requestingPlayerId != null && dto.Players != null)
            {
                var holeCards = gameState.GetHoleCardsFor(requestingPlayerId);
                foreach (var player in dto.Players)
                {
                    if (player.Id == requestingPlayerId)
                    {
                        player.HoleCards = holeCards;
                    }
                    else if (!dto.IsShowdown)
                    {
                        player.HoleCards = null;
                    }
                }
            }

            return dto;
        }

        public void ClearGameState(string roomCode)
        {
            if (this.activeGames.TryRemove(roomCode, out var game))
            {
                // Cancel all timers
                foreach (var cts in game.TimerCts.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                game.TimerCts.Clear();

                foreach (var player in game.PlayerMap.Values)
                {
                    player.Dispose();
                }
            }
        }

        public (bool Success, string? Error) ValidateAction(string playerId, string actionType, int? amount)
        {
            foreach (var game in this.activeGames.Values)
            {
                if (!game.PlayerMap.ContainsKey(playerId))
                {
                    continue;
                }

                if (game.CurrentPlayerToActId != playerId)
                {
                    return (false, "Not your turn");
                }

                if (actionType.Equals("RAISE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!amount.HasValue || amount.Value <= 0)
                    {
                        return (false, "Invalid raise amount");
                    }
                }

                return (true, null);
            }

            return (false, "Player not in active game");
        }

        public void Dispose()
        {
            foreach (var game in this.activeGames.Values)
            {
                foreach (var cts in game.TimerCts.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                game.TimerCts.Clear();

                foreach (var player in game.PlayerMap.Values)
                {
                    player.Dispose();
                }
            }

            this.activeGames.Clear();
        }

        private TcsPlayer? FindTcsPlayer(string playerId)
        {
            foreach (var game in this.activeGames.Values)
            {
                if (game.PlayerMap.TryGetValue(playerId, out var player))
                {
                    return player;
                }
            }

            return null;
        }

        private void CancelTimer(string playerId)
        {
            foreach (var game in this.activeGames.Values)
            {
                if (game.TimerCts.TryRemove(playerId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
        }

        private void CancelAllTimers(ActiveGame gameState)
        {
            foreach (var kvp in gameState.TimerCts)
            {
                if (gameState.TimerCts.TryRemove(kvp.Key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
        }

        private void HandleTurnRequested(Room room, ActiveGame gameState, string sessionId, IGetTurnContext context)
        {
            // Cancel any leftover timer from previous turn (handles TcsPlayer timeout case)
            this.CancelAllTimers(gameState);

            // Back-fill the previous player's bet from the pot delta.
            // HandleTurnRequested fires BEFORE the current player acts, so the player who
            // just acted (gameState.CurrentPlayerToActId) was never updated after their raise.
            // The pot delta reveals exactly how much they added since the last broadcast.
            var prevPlayerId = gameState.CurrentPlayerToActId;
            if (prevPlayerId != null && prevPlayerId != sessionId)
            {
                var potDelta = context.CurrentPot - gameState.MainPot;
                if (potDelta > 0)
                {
                    var prevSession = room.Players.FirstOrDefault(p => p.Id == prevPlayerId);
                    if (prevSession != null)
                    {
                        prevSession.CurrentBet += potDelta;
                        prevSession.TotalBetThisHand += potDelta;
                        prevSession.Chips = Math.Max(0, prevSession.Chips - potDelta);
                    }
                }
            }

            gameState.CurrentPlayerToActId = sessionId;

            // Build turn info from engine context
            var turnInfo = new YourTurnDto
            {
                MinRaise = context.MinRaise,
                MaxRaise = context.MoneyLeft,
                AmountToCall = context.MoneyToCall,
                TimeRemaining = 30,
                AvailableActions = new List<string>(),
            };

            turnInfo.AvailableActions.Add("Fold");
            if (context.CanCheck)
            {
                turnInfo.AvailableActions.Add("Check");
            }

            if (context.MoneyToCall > 0)
            {
                turnInfo.AvailableActions.Add("Call");
            }

            if (context.CanRaise)
            {
                turnInfo.AvailableActions.Add("Raise");
            }

            if (context.MoneyLeft > 0)
            {
                turnInfo.AvailableActions.Add("AllIn");
            }

            // Update DTO state
            gameState.CurrentRound = context.RoundType.ToString();
            gameState.MainPot = context.CurrentPot;
            gameState.MinRaise = context.MinRaise;

            // Update the acting player's chip info
            var session = room.Players.FirstOrDefault(p => p.Id == sessionId);
            if (session != null)
            {
                // Calculate the incremental bet amount and add to cumulative total
                var betDelta = context.MyMoneyInTheRound - session.CurrentBet;
                session.TotalBetThisHand += betDelta;
                session.Chips = context.MoneyLeft;
                session.CurrentBet = context.MyMoneyInTheRound;
                session.Status = PlayerStatus.Active;
            }

            this.PlayerTurnRequested?.Invoke(room.RoomCode, sessionId, turnInfo);

            // Start countdown timer that pushes every second
            var timerCts = new CancellationTokenSource();
            gameState.TimerCts[sessionId] = timerCts;

            _ = Task.Run(async () =>
            {
                var remaining = 30;
                try
                {
                    while (remaining > 0 && !timerCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(1000, timerCts.Token);
                        remaining--;

                        var timerUpdate = new TurnTimerDto
                        {
                            PlayerId = sessionId,
                            TimeRemaining = remaining,
                            TotalTime = 30,
                        };

                        this.TurnTimerTick?.Invoke(room.RoomCode, timerUpdate);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Timer was cancelled (player acted or timeout), this is expected
                }
            });
        }

        private void HandleHandStarted(Room room, ActiveGame gameState, string sessionId, IStartHandContext context)
        {
            gameState.IsHandComplete = false;       // fires N times per hand; idempotent
            gameState.HandNumber = context.HandNumber;
            gameState.SmallBlind = context.SmallBlind;
            gameState.CommunityCards.Clear();
            gameState.CurrentPlayerToActId = null;
            gameState.IsShowdown = false;
            gameState.ShowdownCards = null;

            // Store this player's hole cards
            gameState.HoleCards[sessionId] = new List<CardDto>
            {
                ToCardDto(context.FirstCard),
                ToCardDto(context.SecondCard),
            };

            // Snapshot chips at hand start for winner detection
            var session = room.Players.FirstOrDefault(p => p.Id == sessionId);
            if (session != null)
            {
                session.Chips = context.MoneyLeft;
                session.CurrentBet = 0;
                session.TotalBetThisHand = 0;
                session.Status = PlayerStatus.Active;
                gameState.ChipsAtHandStart[sessionId] = context.MoneyLeft;
            }

            // Broadcast state after all players have received their hand start
            this.BroadcastState(room.RoomCode, gameState);
        }

        private void HandleRoundStarted(Room room, ActiveGame gameState, IStartRoundContext context)
        {
            // Capture the last player's bet before resetting. The player who called/checked
            // to end the previous round is never covered by HandleTurnRequested (no next
            // player fires for them); the pot delta here is exactly what they added.
            var lastActorId = gameState.CurrentPlayerToActId;
            if (lastActorId != null)
            {
                var potDelta = context.CurrentPot - gameState.MainPot;
                if (potDelta > 0)
                {
                    var lastSession = room.Players.FirstOrDefault(p => p.Id == lastActorId);
                    if (lastSession != null)
                    {
                        lastSession.TotalBetThisHand += potDelta;
                        lastSession.Chips = Math.Max(0, lastSession.Chips - potDelta);
                    }
                }
            }

            gameState.CurrentPlayerToActId = null;
            gameState.CurrentRound = context.RoundType.ToString();
            gameState.MainPot = context.CurrentPot;
            gameState.CommunityCards = context.CommunityCards
                .Select(ToCardDto)
                .ToList();

            // Reset current bets for the new round
            foreach (var session in room.Players)
            {
                session.CurrentBet = 0;
            }

            this.BroadcastState(room.RoomCode, gameState);
        }

        private void HandleHandEnded(Room room, ActiveGame gameState, IEndHandContext context)
        {
            // Cancel any leftover timers
            this.CancelAllTimers(gameState);

            // Store showdown cards (keyed by session ID)
            if (context.ShowdownCards != null)
            {
                var dict = context.ShowdownCards
                    .ToDictionary(
                        kvp => gameState.NameToSessionId.GetValueOrDefault(kvp.Key, kvp.Key),
                        kvp => kvp.Value.Select(ToCardDto).ToList());
                gameState.ShowdownCards = new ConcurrentDictionary<string, List<CardDto>>(dict);
            }
            else
            {
                gameState.ShowdownCards = null;
            }

            gameState.CurrentPlayerToActId = null;
            gameState.IsShowdown = gameState.ShowdownCards != null && gameState.ShowdownCards.Count > 0;

            // Guard: HandleHandEnded fires once per player — only broadcast and sleep on first invocation.
            // Engine events fire sequentially on a single background thread, so IsHandComplete is set
            // before any subsequent invocation reads it.
            if (!gameState.IsHandComplete)
            {
                gameState.IsHandComplete = true;
                this.BroadcastState(room.RoomCode, gameState);      // HandComplete broadcast
                System.Threading.Thread.Sleep(2500);                // pause engine thread; clients animate

                // Clear hand state after animation window (inside guard so state stays intact during sleep).
                gameState.HoleCards.Clear();
                gameState.ShowdownCards = null;
                gameState.IsShowdown = false;
                gameState.ChipsAtHandStart.Clear();
            }
        }

        private void BroadcastState(string roomCode, ActiveGame gameState)
        {
            var dto = gameState.BuildDto();
            this.GameStateChanged?.Invoke(roomCode, dto);
        }

        private static CardDto ToCardDto(Card card)
        {
            return new CardDto
            {
                Suit = card.Suit.ToFriendlyString(),
                Rank = card.Type.ToFriendlyString(),
                Display = card.ToString(),
            };
        }

        /// <summary>
        /// Holds all mutable state for one active game.
        /// </summary>
        private class ActiveGame
        {
            public ActiveGame(string roomCode, Room room)
            {
                this.RoomCode = roomCode;
                this.Room = room;
            }

            public string RoomCode { get; }

            public Room Room { get; }

            public TexasHoldemGame? Game { get; set; }

            public ConcurrentDictionary<string, TcsPlayer> PlayerMap { get; }
                = new ConcurrentDictionary<string, TcsPlayer>();

            public ConcurrentDictionary<string, string> NameToSessionId { get; }
                = new ConcurrentDictionary<string, string>();

            public ConcurrentDictionary<string, List<CardDto>> HoleCards { get; }
                = new ConcurrentDictionary<string, List<CardDto>>();

            public ConcurrentDictionary<string, List<CardDto>>? ShowdownCards { get; set; }

            public ConcurrentDictionary<string, int> ChipsAtHandStart { get; }
                = new ConcurrentDictionary<string, int>();

            public List<CardDto> CommunityCards { get; set; } = new List<CardDto>();

            public string? CurrentPlayerToActId { get; set; }

            public string? CurrentRound { get; set; }

            public int HandNumber { get; set; }

            public int SmallBlind { get; set; } = 10;

            public int MainPot { get; set; }

            public int MinRaise { get; set; }

            public bool IsShowdown { get; set; }

            public bool IsHandComplete { get; set; }

            public ConcurrentDictionary<string, CancellationTokenSource> TimerCts { get; }
                = new ConcurrentDictionary<string, CancellationTokenSource>();

            public GameStateDto BuildDto()
            {
                var dto = new GameStateDto
                {
                    StateVersion = this.HandNumber,
                    Phase = this.IsHandComplete ? "HandComplete" : "HandInProgress",
                    CurrentRound = this.CurrentRound,
                    CommunityCards = this.CommunityCards.Count > 0
                        ? new List<CardDto>(this.CommunityCards)
                        : null,
                    MainPot = this.MainPot,
                    CurrentPlayerToActId = this.CurrentPlayerToActId,
                    HandNumber = this.HandNumber,
                    SmallBlind = this.SmallBlind,
                    MinRaise = this.MinRaise,
                    IsShowdown = this.IsShowdown,
                };

                // Include showdown hands when in showdown state
                if (this.IsShowdown && this.ShowdownCards != null)
                {
                    dto.ShowdownHands = new Dictionary<string, List<CardDto>>(this.ShowdownCards);
                }

                // Populate player info from room
                dto.Players = this.Room.Players.Select((p, i) => new PlayerInfoDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Emoji = p.Emoji,
                    Chips = p.Chips,
                    Status = p.Status.ToString(),
                    CurrentBet = p.CurrentBet,
                    TotalBetThisHand = p.TotalBetThisHand,
                    IsHost = p.IsHost,
                    Position = i,
                }).ToList();

                return dto;
            }

            public List<CardDto>? GetHoleCardsFor(string sessionId)
            {
                return this.HoleCards.TryGetValue(sessionId, out var cards)
                    ? cards
                    : null;
            }
        }
    }
}
