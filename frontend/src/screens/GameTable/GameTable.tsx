import { useState, useEffect } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { Button } from '../../components/common/Button'
import { PlayingCard } from '../../components/cards/PlayingCard'
import { Avatar } from '../../components/players/Avatar'
import { ChipDisplay } from '../../components/players/ChipDisplay'
import { useGameStore } from '../../store/gameStore'
import { signalRService } from '../../services/signalRService'
import './GameTable.css'

export function GameTableScreen() {
  const {
    gameState, isMyTurn, myTurnInfo, turnTimer, playerId,
    disconnectedPlayerIds, prevHandChips, handResultVisible,
  } = useGameStore();
  const [raiseAmount, setRaiseAmount] = useState(0);
  const [submitting, setSubmitting] = useState(false);

  // Init raise slider to minRaise when turn starts
  useEffect(() => {
    if (myTurnInfo) setRaiseAmount(myTurnInfo.minRaise);
  }, [myTurnInfo]);

  if (!gameState) {
    return <div className="table-loading">Loading game...</div>;
  }

  const me = gameState.players.find((p) => p.id === playerId);
  const others = gameState.players.filter((p) => p.id !== playerId);

  // Per-player chip deltas during the HandComplete animation window
  const deltas: Record<string, number> = {};
  if (handResultVisible && prevHandChips) {
    for (const p of gameState.players) {
      deltas[p.id] = p.chips - (prevHandChips[p.id] ?? p.chips);
    }
  }

  // Winner banner text (only used when handResultVisible && gameState.isShowdown)
  let winnerBannerText = '';
  if (handResultVisible && gameState.isShowdown) {
    const winners = gameState.players.filter((p) => (deltas[p.id] ?? 0) > 0);
    if (winners.length > 0) {
      const totalWon = winners.reduce((sum, p) => sum + deltas[p.id], 0);
      const names = winners.map((p) => p.name).join(' + ');
      winnerBannerText = winners.length === 1
        ? `* ${names} wins ${totalWon.toLocaleString()} *`
        : `* ${names} split ${totalWon.toLocaleString()} *`;
    } else {
      // Fallback: all deltas zero (perfectly equal split) — derive names from showdownHands keys
      const showdownIds = Object.keys(gameState.showdownHands ?? {});
      const showdownNames = gameState.players
        .filter((p) => showdownIds.includes(p.id))
        .map((p) => p.name)
        .join(' + ');
      winnerBannerText = showdownNames ? `* ${showdownNames} split pot *` : '* Split pot *';
    }
  }

  const handleAction = async (action: string, amount?: number) => {
    setSubmitting(true);
    try {
      const result = await signalRService.submitAction(action, amount);
      if (result.success) useGameStore.getState().setMyTurn(false, null);
    } finally {
      setSubmitting(false);
    }
  };

  const actions = myTurnInfo?.availableActions || [];

  return (
    <div className="game-table">
      {/* Opponents */}
      <div className="opponents-row">
        {others.map((p) => (
          <div key={p.id} className="opponent-seat">
            <Avatar emoji={p.emoji} name={p.name} size="sm"
              isActive={gameState.currentPlayerToActId === p.id}
              isFolded={p.status === 'Folded'}
              isDisconnected={disconnectedPlayerIds.has(p.id)} />
            <ChipDisplay
              amount={handResultVisible && prevHandChips ? (prevHandChips[p.id] ?? p.chips) : p.chips}
              animateTo={handResultVisible ? p.chips : undefined}
              delta={handResultVisible && (deltas[p.id] ?? 0) !== 0 ? deltas[p.id] : undefined}
              size="sm"
            />
            {p.currentBet > 0 && <span className="bet-badge">{p.currentBet}</span>}
            {p.status === 'AllIn' && <span className="allin-badge">ALL IN</span>}
            {/* Showdown: show opponent hole cards */}
            {gameState.isShowdown && gameState.showdownHands?.[p.id] && (
              <div className="showdown-cards">
                {gameState.showdownHands[p.id].map((c, i) => (
                  <PlayingCard key={i} card={c} size="sm" delay={i * 0.1} />
                ))}
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Table center */}
      <div className="table-center">
        <div className="pot-display">
          <span className="pot-label">Pot</span>
          <span className="pot-amount">{gameState.mainPot.toLocaleString()}</span>
        </div>

        <AnimatePresence>
          {handResultVisible && gameState.isShowdown && (
            <motion.div
              className="winner-banner"
              initial={{ scale: 0.8, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.8, opacity: 0 }}
              transition={{ type: 'spring', stiffness: 300, damping: 20 }}
            >
              {winnerBannerText}
            </motion.div>
          )}
        </AnimatePresence>

        <div className="community-cards">
          {(gameState.communityCards || []).map((card, i) => (
            <PlayingCard key={i} card={card} size="md" delay={i * 0.12} />
          ))}
          {/* Empty slots for undealt cards */}
          {Array.from({ length: Math.max(0, 5 - (gameState.communityCards?.length || 0)) }).map((_, i) => (
            <div key={`empty-${i}`} className="card-slot" />
          ))}
        </div>

        <div className="table-info">
          <span>Hand #{gameState.handNumber}</span>
          <span>Blinds {gameState.smallBlind}/{gameState.smallBlind * 2}</span>
        </div>
      </div>

      {/* Your seat */}
      <div className="my-seat">
        <div className="my-hand">
          <div className="my-cards">
            {me?.holeCards?.map((card, i) => (
              <PlayingCard key={i} card={card} size="lg" delay={i * 0.15} />
            )) || [<PlayingCard key={0} size="lg" />, <PlayingCard key={1} size="lg" />]}
          </div>
          <ChipDisplay
            amount={handResultVisible && prevHandChips && me
              ? (prevHandChips[me.id] ?? me.chips ?? 0)
              : (me?.chips ?? 0)}
            animateTo={handResultVisible && me ? me.chips : undefined}
            delta={handResultVisible && me && (deltas[me.id] ?? 0) !== 0 ? deltas[me.id] : undefined}
            label="You"
            size="md"
          />
        </div>

        {/* Betting controls */}
        <AnimatePresence>
          {isMyTurn && myTurnInfo && (
            <motion.div className="bet-controls"
              initial={{ y: 80, opacity: 0 }} animate={{ y: 0, opacity: 1 }}
              exit={{ y: 80, opacity: 0 }} transition={{ type: 'spring', damping: 20 }}>

              {turnTimer != null && (
                <div className="timer-bar">
                  <span className="timer-text">{turnTimer}s</span>
                </div>
              )}

              <div className="action-row">
                {actions.includes('Fold') && (
                  <Button variant="fold" size="md" onClick={() => handleAction('Fold')} disabled={submitting}>Fold</Button>
                )}
                {actions.includes('Check') && (
                  <Button variant="check" size="md" onClick={() => handleAction('Check')} disabled={submitting}>Check</Button>
                )}
                {actions.includes('Call') && (
                  <Button variant="call" size="md" onClick={() => handleAction('Call')} disabled={submitting}>
                    Call {myTurnInfo.amountToCall}
                  </Button>
                )}
                {actions.includes('AllIn') && (
                  <Button variant="allIn" size="md" onClick={() => handleAction('AllIn')} disabled={submitting}>All In</Button>
                )}
              </div>

              {actions.includes('Raise') && (
                <div className="raise-row">
                  <input type="range" className="raise-slider"
                    min={myTurnInfo.minRaise} max={myTurnInfo.maxRaise}
                    value={raiseAmount} onChange={(e) => setRaiseAmount(Number(e.target.value))} />
                  <Button variant="raise" size="md"
                    onClick={() => handleAction('Raise', raiseAmount)} disabled={submitting}>
                    Raise {raiseAmount}
                  </Button>
                </div>
              )}
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
