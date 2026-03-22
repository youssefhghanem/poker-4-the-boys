# Poker 4 The Boys -- Online Texas Hold'em

A web-based multiplayer Texas Hold'em poker game built on a forked C# game engine. No account required -- create a room, share the code, play with friends.

> **Status:** Pre-development. See [`plan.md`](plan.md) for the full design brief. This README captures the plan review, engine audit findings, and the revised approach.

---

## Plan Review Summary

The full plan ([`plan.md`](plan.md)) was reviewed by a Senior Software Engineer against seven criteria: completeness, edge cases, performance at scale, security, maintainability, top risks, and minimum viable scope. A parallel code audit was performed on the existing game engine.

**Verdict: The plan needs significant revision before implementation can begin.** The engine has critical correctness bugs that must be fixed first, the plan's hardest architectural challenge is given one line of attention, and testing is deferred 4 months to Phase 5.

---

## Critical Engine Bugs (Must Fix Before Building On It)

The existing C# engine (`src/TexasHoldem.Logic/`) has **7 critical bugs** discovered during audit:

| # | Bug | File | Impact |
|---|-----|------|--------|
| 1 | **Full house evaluation can exceed 5 cards** | `HandEvaluator.cs:70-98` | Runtime crash (`BestHand` constructor throws) for certain 7-card combinations with two three-of-a-kind groups plus a pair |
| 2 | **Multi-player showdown ranking is broken** | `Helpers.cs:39-60` | `GetHandRankValue` uses relative opponent-count scoring, not absolute hand comparison. Two players with identical hands get different scores and don't split the pot correctly |
| 3 | **Integer division loses chips on split pots** | `HandLogic.cs:164` | `pot / count` truncates. A 101-chip pot split 2 ways gives 50 each; 1 chip vanishes permanently. Money leaks every split hand |
| 4 | **Heads-up showdown ignores side pots** | `HandLogic.cs:100-117` | 2-player path gives entire `pot` to winner, ignoring main/side pot structure. All-in for less than opponent's bet is not handled |
| 5 | **Infinite game loop** | `TexasHoldemGame.cs:116-141` | `Rebuy()` restores busted players after every hand, so `WithMoney().Count() > 1` is always true. The game never terminates |
| 6 | **Constructor validates after delegation** | `TexasHoldemGame.cs:22-40` | Null checks run after the private constructor has already iterated the collection. Null player causes `NullReferenceException` instead of the intended `ArgumentNullException` |
| 7 | **Mutable shared singleton actions** | `PlayerAction.cs` | `Fold()` and `CheckOrCall()` return shared static instances, but `Money` has an `internal set`. Any mutation corrupts the singleton for all future uses |

**Additional important issues:** blind posting can index out-of-bounds with 2 active players from a larger game, no raise amount validation (TODO in code), blind escalation is commented out, `Pot` struct default has null `ActivePlayer` list, inconsistent default buy-in between constructors (1000 vs 200).

> **Action required:** Fix bugs #1-#5 and add money-conservation invariant tests before building the online platform on this engine. These are not "nice to haves" -- they produce wrong game results.

---

## Missing Steps in the Plan

### The #1 Gap: Synchronous Engine vs. Async Web Architecture

The plan's task 1.6 says "Bridge IPlayer interface to SignalR" in one line. This is actually the **hardest engineering challenge in the entire project**.

The engine is synchronous and blocking:
- `TexasHoldemGame.Start()` calls `PlayGame()` which runs a `while` loop
- Inside each hand, `BettingLogic.Bet()` calls `player.GetTurn()` **synchronously** and blocks until it returns
- For a web game, you need to send "your turn" to the client, **wait asynchronously** for a WebSocket response, then resume

This requires either:
- **(A)** Rewriting the engine to be async/event-driven (cleaner but more work)
- **(B)** Running each game on a dedicated thread with `TaskCompletionSource<PlayerAction>` to bridge sync-to-async (faster but ties up threads)

Neither approach is mentioned in the plan. This should be the **first architectural decision** and deserves its own design document.

### Other Missing Steps

| Gap | Why It Matters |
|-----|---------------|
| **Engine bug fixes** | Can't build on a broken foundation. Needs a Phase 0 |
| **Rebuy behavior** | Engine auto-rebuys at $0. Online games need explicit rebuy-or-bust choice |
| **Blind escalation** | Commented out in engine. Plan doesn't mention enabling it for tournament-style play |
| **Player name uniqueness** | Engine throws on duplicate names. Plan's join flow doesn't address collision |
| **API contract definition** | Deferred to "Next Steps" but frontend and backend can't start in parallel without it |
| **Host disconnection / migration** | No host = orphaned room. Need either host migration or graceful shutdown |
| **Spectator / mid-game join** | What happens when someone joins a game in progress? |
| **Seat/position management** | Engine uses list order for dealer rotation. How do players pick or get assigned seats? |

---

## Edge Cases

| Edge Case | Risk | Severity |
|-----------|------|----------|
| **Race condition in betting** | Multiple players submit actions via WebSocket simultaneously. `BettingLogic` assumes sequential calls, no locking | HIGH |
| **Server restart = total data loss** | In-memory `Dictionary` state is gone. Every active game destroyed with no recovery | HIGH |
| **Concurrent room state mutation** | Two SignalR connections modify same room simultaneously (player joins while game starts) | HIGH |
| **Disconnect during all-in** | Side pot logic assumes all players present through showdown | MEDIUM |
| **Host disconnects** | No host migration. Orphaned room, no one can start/restart | MEDIUM |
| **Player returns illegal action** | No validation that a player's action is legal given the game state. Engine trusts `IPlayer` completely | MEDIUM |
| **Mutable game state leaked to players** | `GetTurnContext` passes `SidePots` as `List<Pot>` (not `IReadOnlyList`). Malicious player could modify | MEDIUM |

---

## Performance at 10x Scale

| Scale | What Happens |
|-------|-------------|
| **100 concurrent games** | Fine. In-memory state fits easily |
| **1,000 concurrent games** | Thread pool exhaustion if using approach (B) for sync-to-async bridge. Each game blocks a thread for its entire duration |
| **10,000 concurrent games** | In-memory approach collapses. No sharding, no distribution, single point of failure |

**Specific bottlenecks identified in the engine:**
- `PotCreator.MainPot` and `.SidePots` are computed properties that rebuild from scratch every access -- called many times per betting round
- `GetHandRankValue` is O(n^2) per showdown (evaluates each player against all opponents)
- `BettingLogic` runs O(n) LINQ scans inside O(n) loops per betting action
- `HandEvaluator.GetBestHand` allocates many short-lived `List<CardType>` objects per call, creating GC pressure in simulation mode

**The plan's scalability section (7.3) is three lines.** "Add Redis for multi-instance" is correct direction but needs a concrete migration strategy.

---

## Security Vulnerabilities

| Vulnerability | Detail | Severity |
|--------------|--------|----------|
| **Deck uses `System.Random`, not CSPRNG** | Plan Section 8 claims "cryptographically secure random from game engine." **This is false.** `RandomProvider.cs` uses `System.Random` seeded from `Environment.TickCount`. A motivated attacker can predict the deck | HIGH |
| **No raise amount validation** | `InternalPlayerMoney.DoPlayerAction` has a TODO: "no limit in the raise amount." No `MinRaise` enforcement at the action level | MEDIUM |
| **Room code brute-force** | No rate limiting on join. 36^6 = 2.1B combos but WebSocket requests are fast | MEDIUM |
| **XSS via display names** | Player names rendered in UI with no sanitization mentioned | MEDIUM |
| **No CORS / origin validation** | WebSocket connections need origin checks to prevent cross-site hijacking | MEDIUM |
| **SQL injection listed but irrelevant** | Plan Section 8 mentions SQL injection but v1 has no SQL. Indicates a generic checklist was pasted, not actual threat modeling | INFO |

---

## Top 3 Risks

1. **The synchronous game engine doesn't fit a web multiplayer architecture.** `TexasHoldemGame.Start()` blocks a thread for the entire game. `IPlayer.GetTurn()` blocks until it returns. For a web game, every player action is an async WebSocket round-trip. The plan allocates one task line to what is the project's defining architectural challenge.

2. **The engine has critical correctness bugs.** Multi-player showdown ranking is broken, full house evaluation can crash, pots leak money on splits, and the game loop never terminates. Building a web platform on this foundation will multiply these bugs across every game session.

3. **Testing is deferred 4 months.** Phase 5 (Weeks 17-20) is the first time comprehensive testing appears. By then, the sync/async adapter, room management, WebSocket protocol, and entire frontend are all built on unvalidated assumptions.

---

## Simplest Shippable Version (Revised MVP)

### Phase 0: Engine Fixes (1-2 weeks) -- NEW
- Fix bugs #1-#7 listed above
- Add money-conservation invariant tests
- Replace `System.Random` with `System.Security.Cryptography.RandomNumberGenerator`
- Enable blind escalation (uncomment + make configurable)
- Disable auto-rebuy (players bust out when they hit 0)
- Decide sync-to-async strategy and prototype the `IPlayer` WebSocket adapter

### Phase 1: MVP Backend + Minimal Frontend (4-5 weeks)
**Include:**
- Room creation with shareable 6-char code
- 2-6 players
- Core betting: fold / call / raise / all-in
- SignalR real-time communication
- 30s turn timer (auto-fold)
- Auto-fold on disconnect (no grace period)
- Fixed blind level
- Bust = out (no rebuy)
- Basic responsive UI -- no animations, no sound
- Host starts game
- Integration tests from day 1

**Exclude (defer to later phases):**
- Sound effects, card animations, showdown animations
- Hand strength indicator
- Reconnection handling
- Host kick player
- Rebuy system
- Spectator mode
- AI opponents
- Blind escalation

**Estimated: 6-8 weeks total** (Phase 0 + Phase 1), vs. 20 weeks in current plan.

---

## Revised Phase Plan

| Phase | Weeks | Focus |
|-------|-------|-------|
| **0: Engine Fixes** | 1-2 | Fix critical bugs, add tests, prototype sync-to-async adapter |
| **1: MVP** | 3-8 | Backend + frontend built in parallel with shared API contract. Integration testing continuous |
| **2: Polish** | 9-12 | Animations, sound, reconnection, host controls, hand strength indicator |
| **3: Scale** | 13-16 | Redis for multi-instance, blind escalation, rebuy option, load testing |
| **4: Production** | 17-18 | CI/CD, custom domain, monitoring, production deployment |

Key changes from original plan:
- **Added Phase 0** for engine fixes (non-negotiable)
- **Frontend and backend are interleaved**, not sequential
- **Testing is continuous from Phase 0**, not deferred to Phase 5
- **API contract defined in Phase 0**, shared between frontend and backend
- **Total timeline: 18 weeks** (vs. 20), but the MVP ships at week 8

---

## Tech Stack Decisions (Resolve Before Phase 1)

The plan leaves these as "or" choices. They need to be decided before writing the first line of code:

| Decision | Recommendation | Rationale |
|----------|---------------|-----------|
| Frontend framework | **React 18 + TypeScript** | Larger ecosystem for poker UI components, better TypeScript integration with SignalR client |
| State management | **Zustand** | Simpler than Redux for real-time game state; less boilerplate |
| Sync-to-async strategy | **Evaluate both**, prototype in Phase 0 | Option A (async rewrite) is cleaner long-term; Option B (thread + TaskCompletionSource) is faster to ship |

---

## Original Engine

This project is forked from [NikolayIT/TexasHoldemGameEngine](https://github.com/NikolayIT/TexasHoldemGameEngine). Original build status and NuGet package are retained for reference:

[![Build Status](https://nikolayit.visualstudio.com/TexasHoldemGameEngine/_apis/build/status/NikolayIT.TexasHoldemGameEngine?branchName=master)](https://nikolayit.visualstudio.com/TexasHoldemGameEngine/_build/latest?definitionId=18&branchName=master)

NuGet: <https://www.nuget.org/packages/TexasHoldemGameEngine>
