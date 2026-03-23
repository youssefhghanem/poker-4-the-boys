namespace TexasHoldem.Logic.Async
{
    using System;
    using System.Threading;

    using TexasHoldem.Logic.Players;

    /// <summary>
    /// An IPlayer implementation that bridges the synchronous game engine
    /// to async callers (e.g., SignalR) using TaskCompletionSource.
    ///
    /// The engine calls GetTurn() synchronously and blocks.
    /// External code calls SubmitAction() to unblock it.
    /// External code subscribes to events to receive game state updates.
    /// </summary>
    public class TcsPlayer : BasePlayer, IDisposable
    {
        private readonly ManualResetEventSlim turnGate = new ManualResetEventSlim(false);

        private readonly int turnTimeoutMs;

        private PlayerAction submittedAction;

        public TcsPlayer(string name, int turnTimeoutMs = 30000)
        {
            this.Name = name;
            this.turnTimeoutMs = turnTimeoutMs;
        }

        /// <summary>Fired when the engine requests this player's action.</summary>
        public event EventHandler<TurnRequestedEventArgs> TurnRequested;

        /// <summary>Fired when a new hand starts (cards dealt).</summary>
        public event EventHandler<HandStartedEventArgs> HandStarted;

        /// <summary>Fired when community cards are revealed.</summary>
        public event EventHandler<RoundStartedEventArgs> RoundStarted;

        /// <summary>Fired when the hand ends.</summary>
        public event EventHandler<HandEndedEventArgs> HandEnded;

        public override string Name { get; }

        public override int BuyIn => -1;

        public override PlayerAction PostingBlind(IPostingBlindContext context)
        {
            return context.BlindAction;
        }

        /// <summary>
        /// Called by the game engine synchronously. Blocks until SubmitAction is called
        /// or the timeout expires (auto-fold).
        /// </summary>
        public override PlayerAction GetTurn(IGetTurnContext context)
        {
            this.turnGate.Reset();
            this.submittedAction = null;

            // Notify subscribers that this player needs to act
            this.TurnRequested?.Invoke(this, new TurnRequestedEventArgs(this.Name, context));

            // Block until action is submitted or timeout
            if (!this.turnGate.Wait(this.turnTimeoutMs))
            {
                return PlayerAction.Fold();
            }

            return this.submittedAction ?? PlayerAction.Fold();
        }

        /// <summary>
        /// Called by external code (e.g., SignalR hub) to submit the player's action.
        /// Unblocks GetTurn().
        /// </summary>
        public bool SubmitAction(PlayerAction action)
        {
            if (action == null)
            {
                return false;
            }

            this.submittedAction = action;
            this.turnGate.Set();
            return true;
        }

        public override void StartHand(IStartHandContext context)
        {
            base.StartHand(context);
            this.HandStarted?.Invoke(this, new HandStartedEventArgs(this.Name, context));
        }

        public override void StartRound(IStartRoundContext context)
        {
            base.StartRound(context);
            this.RoundStarted?.Invoke(this, new RoundStartedEventArgs(this.Name, context));
        }

        public override void EndHand(IEndHandContext context)
        {
            base.EndHand(context);
            this.HandEnded?.Invoke(this, new HandEndedEventArgs(this.Name, context));
        }

        public void Dispose()
        {
            this.turnGate.Dispose();
        }
    }

    public class TurnRequestedEventArgs : EventArgs
    {
        public TurnRequestedEventArgs(string playerName, IGetTurnContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IGetTurnContext Context { get; }
    }

    public class HandStartedEventArgs : EventArgs
    {
        public HandStartedEventArgs(string playerName, IStartHandContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IStartHandContext Context { get; }
    }

    public class RoundStartedEventArgs : EventArgs
    {
        public RoundStartedEventArgs(string playerName, IStartRoundContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IStartRoundContext Context { get; }
    }

    public class HandEndedEventArgs : EventArgs
    {
        public HandEndedEventArgs(string playerName, IEndHandContext context)
        {
            this.PlayerName = playerName;
            this.Context = context;
        }

        public string PlayerName { get; }

        public IEndHandContext Context { get; }
    }
}
