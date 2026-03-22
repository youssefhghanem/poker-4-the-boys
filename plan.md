# Comprehensive Plan & Design Brief: Online Texas Hold'em Game (Version 2 - Revised)

---

## VISION

This document outlines the comprehensive development plan for creating an elegant, web-based online Texas Hold'em poker game. 

### Original Request
The goal is to build a modern, accessible poker platform that combines clean aesthetics with robust gameplay mechanics. The project leverages:
- A forked Texas Hold'em game engine from GitHub (written in C#) that provides complete poker logic including hand evaluation, betting rounds, pot management, and side pots
- Reference UI screenshots from an existing poker game that demonstrates the desired "clean minimalist" aesthetic

### Design Goals
- **Elegant UI/UX**: Clean interface with smooth animations, matching the minimalist luxury aesthetic seen in the reference screenshots
- **Simple Access**: No account required - players can immediately join or create games
- **Unique Matchmaking**: Games identified by unique codes that players can share
- **Responsive**: Works on both desktop and mobile browsers
- **Correct Gameplay**: The engine must be bug-free before building the online platform

### Core Requirements
1. Use the existing C# Texas Hold'em game engine as the backend logic core (AFTER fixing critical bugs)
2. Implement real-time multiplayer gameplay via WebSockets
3. Create an intuitive UI with the layout: top = player avatars/chips, middle = community cards, bottom = user's hand + controls
4. Support up to 6 players per game
5. Host-controlled game settings (starting chip amount)

### Important: Engine Audit Findings

> ⚠️ **CRITICAL**: A code audit of the forked game engine revealed **7 critical bugs** that must be fixed BEFORE building the online platform. See Section 2.3 for details.

---

## 1. EXECUTIVE SUMMARY

Based on my detailed analysis of your forked Texas Hold'em game engine and the reference UI screenshots, I've developed a comprehensive plan to build an elegant, web-based online poker platform. The game engine is well-structured C# code that implements complete Texas Hold'em rules including hand evaluation, betting logic, pot management, and side pots for all-in scenarios.

---

## 2. GAME ENGINE ANALYSIS & CRITICAL MODULES

### 2.1 Core Modules Identified (Must Integrate with Backend)

| Module | File Path | Purpose | Integration Priority |
|--------|-----------|---------|---------------------|
| **Game Engine Core** | `TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs` | Main game loop, hand management | CRITICAL |
| **Hand Logic** | `TexasHoldem.Logic/GameMechanics/HandLogic.cs` | Round-by-round gameplay (Pre-flop, Flop, Turn, River) | CRITICAL |
| **Betting Logic** | `TexasHoldem.Logic/GameMechanics/BettingLogic.cs` | All betting operations, pot calculations | CRITICAL |
| **Hand Evaluator** | `TexasHoldem.Logic/Helpers/HandEvaluator.cs` | Determines winning hands at showdown | CRITICAL |
| **Card System** | `TexasHoldem.Logic/Cards/` | Deck management, card representation | CRITICAL |
| **Player Interface** | `TexasHoldem.Logic/Players/IPlayer.cs` | Player actions (fold, call, raise, check) | CRITICAL |
| **AI Players** | `AI/TexasHoldem.AI.SmartPlayer/` | Bot opponents for single-player mode | HIGH |
| **Pot Management** | `TexasHoldem.Logic/GameMechanics/Pot.cs`, `PotCreator.cs` | Main pot and side pot handling | HIGH |

### 2.2 Key Game Engine Interfaces for Real-Time Communication

The engine uses these callback interfaces that will need WebSocket adaptation:
- `IPlayer.GetTurn(IGetTurnContext)` - Player decision point
- `IPlayer.StartHand(IStartHandContext)` - Deal hole cards
- `IPlayer.StartRound(IStartRoundContext)` - Community cards dealt
- `IPlayer.EndRound(IEndRoundContext)` - Round ends
- `IPlayer.EndHand(IEndHandContext)` - Showdown results

---

### 2.3 Critical Engine Bugs (MUST FIX BEFORE BUILDING)

⚠️ **A code audit revealed 7 critical bugs in the game engine that must be fixed before building the online platform:**

| # | Bug | File | Impact |
|---|-----|------|--------|
| 1 | **Full house evaluation can exceed 5 cards** | `HandEvaluator.cs:70-98` | Runtime crash (`BestHand` constructor throws) for certain 7-card combinations with two three-of-a-kind groups plus a pair |
| 2 | **Multi-player showdown ranking is broken** | `Helpers.cs:39-60` | `GetHandRankValue` uses relative opponent-count scoring, not absolute hand comparison. Two players with identical hands get different scores and don't split the pot correctly |
| 3 | **Integer division loses chips on split pots** | `HandLogic.cs:164` | `pot / count` truncates. A 101-chip pot split 2 ways gives 50 each; 1 chip vanishes permanently. Money leaks every split hand |
| 4 | **Heads-up showdown ignores side pots** | `HandLogic.cs:100-117` | 2-player path gives entire `pot` to winner, ignoring main/side pot structure. All-in for less than opponent's bet is not handled |
| 5 | **Infinite game loop** | `TexasHoldemGame.cs:116-141` | `Rebuy()` restores busted players after every hand, so `WithMoney().Count() > 1` is always true. The game never terminates |
| 6 | **Constructor validates after delegation** | `TexasHoldemGame.cs:22-40` | Null checks run after the private constructor has already iterated the collection. Null player causes `NullReferenceException` instead of the intended `ArgumentNullException` |
| 7 | **Mutable shared singleton actions** | `PlayerAction.cs` | `Fold()` and `CheckOrCall()` return shared static instances, but `Money` has an `internal set`. Any mutation corrupts the singleton for all future uses |

**Additional important issues:**
- Blind posting can index out-of-bounds with 2 active players from a larger game
- No raise amount validation (TODO in code)
- Blind escalation is commented out
- `Pot` struct default has null `ActivePlayer` list
- Inconsistent default buy-in between constructors (1000 vs 200)

> **⚠️ ACTION REQUIRED:** Fix bugs #1-#5 and add money-conservation invariant tests before Phase 1. This is NOT optional - these bugs produce wrong game results.

---

## 3. TECH STACK RECOMMENDATION

### 3.1 Recommended Architecture (Free & Cost-Effective)

```
┌─────────────────────────────────────────────────────────────────┐
│                        FRONTEND                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐ │
│  │   React.js or   │  │  CSS3 + Framer  │  │   Zustand or   │ │
│  │   Vue.js 3      │  │  Motion         │  │   Redux Toolkit │ │
│  │   (UI Framework) │  │  (Animations)   │  │   (State Mgmt) │ │
│  └────────┬────────┘  └────────┬────────┘  └───────┬────────┘ │
└───────────┼─────────────────────┼──────────────────┼──────────┘
            │                     │                  │
            │    WebSocket        │                  │ REST API
            ▼                     ▼                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BACKEND                                   │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │              ASP.NET Core 8.0 (C# Game Server)            ││
│  │  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  ││
│  │  │ Game Engine  │  │  SignalR     │  │  No Auth       │  ││
│  │  │ Integration  │  │  Hub          │  │  (v1)          │  ││
│  │  └──────────────┘  └──────────────┘  └────────────────┘  ││
│  └─────────────────────────────────────────────────────────────┘│
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐   │
│  │  In-Memory      │  │   Free          │  │  Docker        │   │
│  │  Dictionary     │  │   (Single       │  │  (Deployment)  │   │
│  │  (Room State)  │  │   Instance)     │  │                │   │
│  └─────────────────┘  └─────────────────┘  └────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Technology Selection Rationale (Cost-Focused)

| Component | Recommended | Cost | Reasoning |
|-----------|------------|------|-----------|
| **Frontend Framework** | **React 18+** | FREE | Larger ecosystem for poker UI components, better TypeScript integration with SignalR client |
| **State Management** | **Zustand** | FREE | Simpler than Redux for real-time game state; less boilerplate |
| **Game Rendering** | CSS3 + Framer Motion | FREE* | Smooth card animations; Framer Motion has generous free tier |
| **Real-time Communication** | SignalR (Backend) | FREE | Native C#; built into ASP.NET Core |
| **Backend Server** | ASP.NET Core 8.0 | FREE | Open source, native C# game engine integration |
| **Database** | In-Memory (v1) / SQLite (future) | FREE | No persistence needed for v1; rooms exist only during session |
| **Authentication** | None (v1) | FREE | No account system - instant join |
| **Deployment** | Render.com / Railway / Coolify | FREE-$5/mo | Affordable self-hosted options |

*Framer Motion is free for commercial use with attribution

### ⚠️ CRITICAL: The Sync-to-Async Challenge

> ⚠️ **This is the #1 architectural challenge in the entire project.** The original plan allocated ONE task line to "Bridge IPlayer interface to SignalR." This is insufficient.

**The Problem:**
- The game engine's `TexasHoldemGame.Start()` runs a **synchronous blocking loop**
- Inside each hand, `BettingLogic.Bet()` calls `player.GetTurn()` **synchronously and blocks** until it returns
- For a web game, you need: send "your turn" → **wait asynchronously** for WebSocket response → resume

**Two Approaches:**

| Approach | Pros | Cons |
|----------|------|------|
| **(A) Async Rewrite** | Clean architecture, scalable | More work upfront |
| **(B) TaskCompletionSource Bridge** | Faster to ship | Blocks threads, limited scale |

**Recommended Action:** Prototype both in Phase 0 and decide before Phase 1.

### 3.3 Deployment & Infrastructure (Budget-Friendly)

#### Domain & Hosting Options

| Service | Cost | Features |
|---------|------|----------|
| **Domain** | Namecheap/Cloudflare | ~$10-15/year for .com |
| **Render.com** | Free tier available | Web services, auto-deploy from GitHub |
| **Railway** | $5 credit/month | Easy deployment, pay-as-you-go |
| **Coolify** | Self-hosted FREE | Run on your own VPS |
| **DigitalOcean** | $4-6/month | Droplet with Docker |

#### Recommended Deployment Path

**Phase 1-2 (Development/Staging):**
- Deploy to Render.com free tier
- Uses GitHub Actions for CI/CD
- Automatic deployments from main branch

**Phase 3+ (Production):**
- Buy domain (~$12/year from Namecheap)
- Migrate to Render Pro ($7/mo) or DigitalOcean ($4/mo)
- Add Cloudflare CDN (free tier)

#### Deployment Steps

1. **Get a Domain** (~$12/year)
   - Go to Namecheap.com or Cloudflare
   - Search for available poker-related domain
   - Purchase and configure DNS

2. **Set Up Hosting** (Free-$7/month)
   - Create account on Render.com
   - Connect GitHub repository
   - Deploy backend as Web Service
   - Deploy frontend as Static Site

3. **Configure DNS**
   - Add CNAME record pointing to Render
   - Enable Cloudflare proxy (free)
   - Get free SSL certificate (automatic)

4. **Scale Up** (When Needed)
   - Upgrade to paid tier for more resources
   - Add Redis for game state (~$5/mo on Redis Cloud free tier)
   - Move to VPS with Coolify for full control

---

## 4. UI DESIGN BRIEF

### 4.1 Design Philosophy (Based on Reference Screenshots)

The reference UI demonstrates a **minimalist luxury aesthetic**:

**Color Palette:**
- Primary: Deep green felt (`#1a472a` to `#2d5a3f`)
- Accent: Gold/champagne (`#d4af37`) for highlights
- Background: Dark charcoal (`#1a1a2e`) to black gradients
- Cards: Classic white with subtle shadows
- Player chips: Multi-colored with gradients

**Typography:**
- Primary Font: Clean sans-serif (Inter, SF Pro Display, or custom poker font)
- Numbers: Monospace for chip counts and betting amounts
- Sizes: Large readable chips, medium action buttons, small info text

**Layout Principles:**
- Centered poker table with radial player arrangement
- Clear visual hierarchy: Community cards → Current player → Other players
- Minimal chrome - focus on the game action
- Subtle animations for card dealing, chip movements, and pot collection

### 4.2 Screen Designs Required (v1 - No Account Required)

| Screen | Purpose | Key Elements |
|--------|---------|--------------|
| **Home** | Entry point | Create Game, Join Game buttons |
| **Create Game** | Host sets up room | Room settings (starting chips), generates room code |
| **Join Game** | Enter existing room | Room code input field |
| **Lobby** | Waiting room | Player list, host controls, ready status, "Start Game" button (host only) |
| **Game Table** | Main gameplay | Players (top), community cards (center), your hand + controls (bottom) |

### 4.3 UI Layout - Vertical Stack Design

Based on the reference screenshots, the game uses a **minimalist vertical layout** without a traditional poker table:

```
┌─────────────────────────────────────────┐
│  TOP BAR - Player Avatars & Chips       │
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐   │
│  │🤠  │ │👽  │ │🤖  │ │👻  │ │🦊  │   │
│  │AAA │ │BBB │ │CCC │ │DDD │ │EEE │   │
│  │500 │ │500 │ │500 │ │500 │ │500 │   │
│  └────┘ └────┘ └────┘ └────┘ └────┘   │
│                                         │
├─────────────────────────────────────────┤
│  MIDDLE - Community Cards & Pot         │
│                                         │
│       🂡 🂱 🂧  Pot: $150               │
│       ┌──┐ ┌──┐ ┌──┐                    │
│       │  │ │  │ │  │                    │
│       └──┘ └──┘ └──┘                    │
│                                         │
├─────────────────────────────────────────┤
│  BOTTOM - Your Hand & Controls          │
│                                         │
│  ┌────┐  Your Hand: [🂡][🂱]           │
│  │😀 │  Balance: $450                   │
│  │YOU│                                   │
│  │450│  [FOLD] [CHECK] [CALL] [RAISE]  │
│  └────┘                                   │
└─────────────────────────────────────────┘
```

### 4.4 Component Specifications

**Player Avatar Component (Top Bar):**
- Emoji avatar (user-selected from picker)
- Username below avatar
- Chip count below username
- Status indicator: "thinking...", "folded", "all-in"
- Current bet amount displayed when betting

**Community Cards Component (Middle):**
- 5 card slots for Flop/Turn/River
- Cards shown face-up
- Pot amount prominently displayed above cards
- Dealer indicator (small "D" badge)

**Your Hand Component (Bottom Left):**
- 2 hole cards in hand
- Face-up to the player
- Balance/chips remaining

**Betting Controls Component (Bottom Right):**
- Fold button (red accent)
- Check button (when available)
- Call button (shows amount)
- Raise button + slider/input
- All-in button
- Timer countdown for turn

**Card Component:**
- Clean white cards with suit icons
- Red suits: ♥ ♦ (hearts, diamonds)
- Black suits: ♠ ♣ (spades, clubs)
- Face-down state: card back pattern
- Subtle shadow for depth

### 4.5 User Flow (No Account Required)

```
1. User opens app
   │
   ├──► Create Game
   │     ├── Enter display name
   │     ├── Select emoji avatar
   │     ├── Set starting chips (host only)
   │     └── Get room code (e.g., "ABC123")
   │
   └──► Join Game
         ├── Enter room code
         ├── Enter display name  
         ├── Select emoji avatar
         └── Wait in lobby

2. Lobby (waiting for host)
   ├── See other players joining
   ├── Host sees "Start Game" button
   └── Non-host see "Waiting for host..."

3. Game Play
   ├── Each round: see community cards
   ├── Your turn: betting controls appear
   ├── Showdown: see winner + hand
   └── Repeat until game ends

4. Game End
   ├── See final standings
   ├── "Play Again" button (host)
   └── "Back to Home" button
```

---

## 5. DEVELOPMENT PHASES (Revised Timeline: 18 weeks)

### PHASE 0: Engine Fixes & Architecture (Weeks 1-2) -- **NEW**

⚠️ **This phase is MANDATORY before any other work.**

**Objectives:**
- Fix critical bugs in the game engine
- Replace insecure random with CSPRNG
- Prototype sync-to-async bridge
- Define API contract (shared between frontend/backend)
- Start continuous testing

**Deliverables:**
- [ ] Fixed game engine (bugs #1-7)
- [ ] CSPRNG for card dealing
- [ ] Money-conservation invariant tests
- [ ] Sync-to-async prototype (both approaches)
- [ ] API contract document
- [ ] Integration tests from day 1

**Technical Tasks:**
```
0.1 Fix critical bugs:
    - Full house > 5 cards crash (HandEvaluator.cs)
    - Multi-player showdown ranking (Helpers.cs)
    - Integer division chip loss (HandLogic.cs)
    - Heads-up side pot handling (HandLogic.cs)
    - Infinite game loop (TexasHoldemGame.cs)
    - Constructor validation order (TexasHoldemGame.cs)
    - Mutable singleton actions (PlayerAction.cs)

0.2 Replace System.Random with CSPRNG:
    - Use System.Security.Cryptography.RandomNumberGenerator
    - Update RandomProvider.cs

0.3 Disable auto-rebuy:
    - Players bust out when they hit 0
    - Game terminates when 1 player remains

0.4 Prototype sync-to-async bridge:
    - Approach A: Async event-driven rewrite
    - Approach B: TaskCompletionSource + dedicated thread
    - Prototype both, evaluate tradeoffs

0.5 Define API contract:
    - Document all SignalR hub methods
    - Document game state structure
    - Share with frontend team

0.6 Add integration tests:
    - Test full hand flow
    - Test split pot scenarios
    - Test all-in edge cases
```

---

### PHASE 1: Foundation & Backend Core (Weeks 3-5)

**Objectives:**
- Set up ASP.NET Core project with SignalR
- Integrate TexasHoldem game engine
- Build room management (no-auth)
- Basic WebSocket communication

**Deliverables:**
- [ ] ASP.NET Core 8.0 project with SignalR
- [ ] Game engine wrapper/adaptor class
- [ ] In-memory room management (Dictionary)
- [ ] Player session management (no persistence)
- [ ] SignalR Hub for real-time communication
- [ ] Unit tests for game engine integration

**Technical Tasks:**
```
1.1 Create solution: PokerGame.sln
1.2 Add TexasHoldem.Logic as project reference
1.3 Create PokerGame.Api project (ASP.NET Core Web API)
1.4 Create PokerGame.Hub project (SignalR Hub)
1.5 Implement RoomManager:
    - CreateRoom(hostName, startingChips) → roomCode
    - JoinRoom(roomCode, playerName, emoji)
    - LeaveRoom(roomCode, playerId)
    - GetRoomState(roomCode)
1.6 Implement WebSocket player adapter:
    - Bridge IPlayer interface to SignalR
    - Handle GetTurn → prompt client
    - Handle callbacks → push to client
1.7 Create game loop integration:
    - StartGame(roomCode)
    - ProcessAction(roomCode, playerId, action)
1.8 Write xUnit tests for core game flow
```

---

### PHASE 2: Real-Time Gameplay (Weeks 6-8)

**Objectives:**
- Full game state synchronization
- All betting actions working
- Turn timer system
- Host controls

**Deliverables:**
- [ ] Complete SignalR game methods
- [ ] All player actions (fold, call, raise, check, all-in)
- [ ] Game state broadcast to all players
- [ ] Turn timeout handling
- [ ] Host controls (kick player, start game)
- [ ] Reconnection handling

**Technical Tasks:**
```
2.1 Expand GameHub methods:
    - JoinRoom(roomCode, playerName, emojiAvatar)
    - LeaveRoom(roomCode)
    - SubmitAction(actionType, amount?)
    - StartGame() [host only]
    - KickPlayer(playerId) [host only]
2.2 Implement game state broadcasting:
    - OnPlayerJoined
    - OnPlayerLeft
    - OnGameStateChanged
    - OnPlayerAction
    - OnRoundEnd
    - OnHandEnd
2.3 Create turn timer:
    - 30-second default per turn
    - Auto-fold on timeout
    - Visual countdown sent to client
2.4 Implement host controls:
    - Host can kick players
    - Host starts game
    - Host can adjust settings pre-game
2.5 Handle disconnections:
    - Grace period for reconnection
    - Auto-fold if disconnected
```

---

### PHASE 3: Frontend Development (Weeks 7-12)

**Objectives:**
- Build complete UI based on design brief
- All screens implemented
- Smooth animations

**Deliverables:**
- [ ] React 18+ application with TypeScript
- [ ] All screens (Home, Create/Join, Lobby, Game)
- [ ] WebSocket client integration
- [ ] Card animations with Framer Motion
- [ ] Betting UI with slider
- [ ] Responsive design (mobile-first)

**Technical Tasks:**
```
3.1 Set up React 18 project with TypeScript
3.2 Create component library:
    - Button, Input, Modal
    - Card (face-up, face-down)
    - Avatar (emoji picker)
    - Chip display
3.3 Build Home screen:
    - "Create Game" button
    - "Join Game" button
3.4 Build Create Game screen:
    - Username input
    - Emoji avatar picker
    - Starting chips slider/input
    - Room code display
3.5 Build Join Game screen:
    - Room code input (6 chars)
    - Username input
    - Emoji avatar picker
3.6 Build Lobby screen:
    - Player list with avatars/emojis
    - "Waiting for players..." status
    - "Start Game" button (host only)
3.7 Build Game screen:
    - Top bar: player avatars + chips (horizontal scroll)
    - Middle: community cards + pot
    - Bottom: your hand + betting controls
3.8 Implement WebSocket client:
    - Connect to SignalR
    - Handle all game events
    - Update UI reactively
3.9 Add animations:
    - Card dealing (slide in)
    - Card flipping (showdown)
    - Chip movements
    - Button interactions
3.10 Responsive styling for mobile
```

---

### PHASE 4: Polish & Features (Weeks 13-16)

**Objectives:**
- Complete game experience
- Sound effects
- Visual polish

**Deliverables:**
- [ ] Sound effects (card shuffle, chips, wins)
- [ ] Hand strength indicator
- [ ] Showdown animations
- [ ] Game summary screen
- [ ] "Play Again" functionality

**Technical Tasks:**
```
4.1 Add sound effects:
    - Card flip
    - Chip placement
    - Winner announcement
    - Button clicks
4.2 Showdown sequence:
    - Reveal all player hands
    - Highlight winning hand
    - Animate pot to winner
4.3 Hand strength indicator:
    - Show "You have: Flush" etc.
    - Optional (can be hidden)
4.4 Game end screen:
    - Final standings
    - "Play Again" button
    - "Back to Home" button
4.5 Visual polish:
    - Smooth transitions
    - Loading states
    - Error handling UI
    - Empty states
```

---

### PHASE 5: Testing & Deployment (Weeks 17-20)

**Objectives:**
- Comprehensive testing
- Bug fixes
- Production deployment

**Deliverables:**
- [ ] Unit tests for backend
- [ ] Integration tests for game flow
- [ ] Load testing
- [ ] Production deployment
- [ ] Custom domain (optional)

**Technical Tasks:**
```
5.1 Write xUnit tests for:
    - Room management
    - Game engine adapter
    - SignalR hub
5.2 Create integration tests:
    - Full game flow
    - Player actions
    - Edge cases
5.3 Perform load testing:
    - Simulate multiple rooms
    - Multiple concurrent games
5.4 Set up CI/CD:
    - GitHub Actions
    - Auto-deploy to Render.com
5.5 Deploy to production:
    - Set up Render.com deployment
    - Configure environment
5.6 (Optional) Custom domain:
    - Buy domain (~12/year)
    - Configure DNS
    - Enable SSL
```

---

## 6. FEATURE PRIORITY MATRIX (v1 - No Account System)

| Priority | Feature | Phase | Complexity |
|----------|---------|-------|------------|
| P0 | Real-time multiplayer gameplay | 2 | High |
| P0 | Game engine integration | 1 | High |
| P0 | Room creation with unique code | 1 | Medium |
| P0 | No-auth player sessions | 1 | Low |
| P1 | Betting actions (fold/call/raise) | 2 | High |
| P1 | Host controls (kick, start) | 2 | Medium |
| P1 | Turn timer system | 2 | Medium |
| P2 | Sound effects | 4 | Low |
| P2 | Showdown animations | 4 | Low |
| P3 | AI opponents | Future | Medium |
| P3 | In-game chat | Future | Medium |
| P3 | Tournaments | Future | High |

---

## 7. INFRASTRUCTURE RECOMMENDATIONS (v1)

### 7.1 Room/Matchmaking System

```
Room Code Format: 6-character alphanumeric (e.g., "ABC123")
- Generate using crypto-secure random
- Display as large, readable text
- Max 6 players per room (based on your requirement)
- In-memory storage (no database needed for v1)
```

### 7.2 Player Session Management (No Auth)

```
Session Data:
- Player ID: GUID (generated on join)
- Display Name: string (user input)
- Avatar Emoji: string (user selected)
- Connection ID: SignalR connection ID

Room Data:
- Room Code: string (6 chars)
- Host Player ID: GUID
- Players: List<PlayerSession>
- Game State: TexasHoldemGame instance
- Settings: startingChips
```

### 7.3 Scalability Considerations (Future)

- v1: Single server instance, in-memory storage
- v2: Add Redis for multi-instance support
- v3: Add database for game history

### 7.4 Custom Domain Setup

**Getting a Domain:**
1. Visit Namecheap.com or Cloudflare registrar
2. Search for available poker-related domain
3. Recommended: poker4theboys.com, yournamepoker.com
4. Cost: ~$10-15/year for .com

**Deploying with Custom Domain:**
1. Deploy to Render.com (free-$7/mo)
2. Get your app URL (e.g., poker-game.onrender.com)
3. Go to your domain registrar DNS settings
4. Add CNAME record: poker -> poker-game.onrender.com
5. Enable Cloudflare proxy for free SSL
6. Wait 24-48 hours for propagation

---

## 8. SECURITY CONSIDERATIONS

1. **Server-authoritative game state** - All decisions validated server-side
2. **Card dealing** - Replace System.Random with CSPRNG (CRITICAL)
3. **Rate limiting** - Prevent action spam on WebSocket
4. **Input validation** - Sanitize all player inputs
5. **Room code brute-force** - Add rate limiting on join attempts
6. **XSS prevention** - Sanitize display names and chat messages
7. **CORS / origin validation** - Validate WebSocket origins

---

## 9. TOP RISKS & MITIGATION

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Sync-to-async bridge complexity** | HIGH | Prototype in Phase 0, choose simpler approach if needed |
| **Engine correctness bugs** | CRITICAL | Fix all 7 bugs in Phase 0 before building |
| **Server restart = data loss** | HIGH | Accept for v1; add Redis in v2 |
| **Race conditions in betting** | HIGH | Add locking around game state mutations |
| **Host disconnection** | MEDIUM | Auto-close room if host disconnects |
| **Concurrent room mutations** | HIGH | Add thread-safety to RoomManager |

---

## 10. OPEN QUESTIONS FOR CLARIFICATION

Before proceeding to implementation, please confirm:

1. **Player Count**: Will games be 2-player only, or up to 10 players?
2. **Stake Levels**: What chip denominations and buy-in amounts?
3. **AI Requirement**: Is single-player (vs AI) mode required, or primarily multiplayer?
4. **Real Money**: Will this be play-money only, or real-money gambling?
5. **Geographic Restrictions**: Any regions to exclude due to legal requirements?
6. **Account Simplicity**: Is "username + password" sufficient, or need social login (Google, Apple)?
7. **Mobile Priority**: Is mobile experience as important as desktop?
8. **Sound Design**: Do you have preferences for sound effects style?

---

## 11. NEXT STEPS

Once you confirm the above details, I can:

1. Create detailed technical specifications for Phase 1
2. Generate initial project structure and code scaffolding
3. Define API contracts for frontend-backend communication
4. Create the UI component specifications for your design team

Please let me know your preferences and any clarifications needed!