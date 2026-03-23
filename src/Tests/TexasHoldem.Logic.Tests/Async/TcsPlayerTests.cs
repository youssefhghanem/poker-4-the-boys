namespace TexasHoldem.Logic.Tests.Async
{
    using System.Threading;
    using System.Threading.Tasks;

    using TexasHoldem.Logic.Async;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    using Xunit;

    public class TcsPlayerTests
    {
        [Fact]
        public void SubmitAction_UnblocksGetTurn()
        {
            var player = new TcsPlayer("Test", turnTimeoutMs: 5000);
            PlayerAction receivedAction = null;

            player.TurnRequested += (sender, args) =>
            {
                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    player.SubmitAction(PlayerAction.CheckOrCall());
                });
            };

            var turnTask = Task.Run(() =>
            {
                receivedAction = player.GetTurn(null);
            });

            Assert.True(turnTask.Wait(3000), "GetTurn did not unblock");
            Assert.NotNull(receivedAction);
            Assert.Equal(PlayerActionType.CheckCall, receivedAction.Type);
        }

        [Fact]
        public void GetTurn_Timeout_AutoFolds()
        {
            var player = new TcsPlayer("Test", turnTimeoutMs: 500);

            var turnTask = Task.Run(() => player.GetTurn(null));

            Assert.True(turnTask.Wait(3000), "GetTurn did not return after timeout");
            Assert.Equal(PlayerActionType.Fold, turnTask.Result.Type);
        }

        [Fact]
        public void TcsPlayer_InFullGame_GameCompletes()
        {
            var tcsPlayer = new TcsPlayer("TcsBot", turnTimeoutMs: 5000);
            tcsPlayer.TurnRequested += (sender, args) =>
            {
                Task.Run(() => tcsPlayer.SubmitAction(PlayerAction.CheckOrCall()));
            };

            var aiPlayer = new AutoCallPlayer("AI");

            var game = new TexasHoldemGame(tcsPlayer, aiPlayer, 20);

            var task = Task.Run(() => game.Start());
            Assert.True(task.Wait(30000), "Game with TcsPlayer did not complete");
        }

        private class AutoCallPlayer : BasePlayer
        {
            public AutoCallPlayer(string name)
            {
                this.Name = name;
            }

            public override string Name { get; }

            public override int BuyIn => -1;

            public override PlayerAction PostingBlind(IPostingBlindContext context)
            {
                return context.BlindAction;
            }

            public override PlayerAction GetTurn(IGetTurnContext context)
            {
                return PlayerAction.CheckOrCall();
            }
        }
    }
}
