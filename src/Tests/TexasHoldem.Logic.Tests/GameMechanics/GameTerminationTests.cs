namespace TexasHoldem.Logic.Tests.GameMechanics
{
    using System.Threading.Tasks;

    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;

    using Xunit;

    public class GameTerminationTests
    {
        [Fact]
        public void Start_TwoPlayers_GameTerminates()
        {
            var player1 = new AlwaysCallTestPlayer("P1");
            var player2 = new AlwaysFoldTestPlayer("P2");

            var game = new TexasHoldemGame(player1, player2, 10);

            var task = Task.Run(() => game.Start());
            Assert.True(task.Wait(10000), "Game did not terminate within 10 seconds");
        }

        [Fact]
        public void Start_ThreePlayers_GameTerminates()
        {
            var players = new IPlayer[]
            {
                new AlwaysCallTestPlayer("P1"),
                new AlwaysFoldTestPlayer("P2"),
                new AlwaysFoldTestPlayer("P3"),
            };

            var game = new TexasHoldemGame(players, 10);

            var task = Task.Run(() => game.Start());
            Assert.True(task.Wait(10000), "Game did not terminate within 10 seconds");
        }

        private class AlwaysCallTestPlayer : BasePlayer
        {
            public AlwaysCallTestPlayer(string name)
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

        private class AlwaysFoldTestPlayer : BasePlayer
        {
            public AlwaysFoldTestPlayer(string name)
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
                if (context.CanCheck)
                {
                    return PlayerAction.CheckOrCall();
                }

                return PlayerAction.Fold();
            }
        }
    }
}
