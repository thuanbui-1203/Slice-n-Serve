# Game Concept Document: Slice & Serve

> Source of truth for **what** the game is. The plans in [`plans/`](./plans/) are the source of
> truth for **how** it gets built. Captured into the repository so that every plan's citations
> resolve to a real document.

---

## 1. Game Overview

**Story:** You play as a passionate cook trying to manage and grow a bustling restaurant. To
prepare dishes, you don't just chop on a cutting board. You must gather ingredients by launching
your trusty kitchen knife across a dynamic ingredient board in a fast-paced, ping-pong-style game.

**Genre:** Action-Arcade / Cooking Management / Semi-Idle

**Engine:** Unity

**Platforms:** PC and Mobile (cross-platform support)

**Vibe:** Think *Overcooked* meets *Peggle/Pong*, layered with a satisfying idle-game progression
loop.

---

## 2. Core Mechanics

### 2.1 Ingredient Gathering (The Board)

**The Knife Ping-Pong:** Players launch a knife into a board filled with spawning ingredients. The
knife bounces around (like a ping-pong ball or a pinball), slicing and collecting the specific
ingredients it hits.

**Controls:** Intuitive drag-and-release mechanics for Mobile (touch) and point-and-click/drag for
PC (mouse) to aim and shoot the knife with calculated velocity and angles.

### 2.2 Fulfilling Orders & Customer Patience

**Orders:** Customers arrive at the counter with specific ingredient requests. The ingredients
gathered from the board automatically contribute to completing their dishes.

**Patience Mechanic:** Each customer has a visible patience bar. If it depletes before their order
is served, they leave angry, resulting in lost revenue or lost lives/reputation.

---

## 3. Economy & Upgrades

Every successfully served order rewards the player with **Cash**. Cash is reinvested into the
restaurant and the board, gradually transitioning the gameplay from heavy manual action to a
satisfying semi-idle experience.

| Upgrade Category | Effect / Description |
| --- | --- |
| **Knife Speed** | Increases the velocity of the knife, allowing it to bounce and collect ingredients much faster before losing momentum. |
| **Knife Split Chance** | Adds a percentage chance for the knife to split into multiple knives upon launch or upon hitting a board wall. |
| **Ingredient Spawn Rate** | Increases the density and respawn speed of ingredients on the board, ensuring the knife always has targets to hit. |
| **Customer Patience** | Increases the base patience level of customers, giving the player more time to fulfill complex orders. |
| **Sous-Chef Helpers** | Unlocks extra automated helpers that periodically launch additional auto-knives onto the board. This is the core Idle Mechanic. |

---

## 4. Difficulty Scaling

**Time-Based Pressure:** The longer a game session lasts (or the higher the level), the faster the
customers' patience meters deplete.

**The Strategy:** Players must rely heavily on balancing their manual aiming skills with idle
upgrades to keep up with the hyper-demanding late-game rush.

---

## 5. Multiplayer (PvP Mode)

The game features a competitive PvP mode supporting both **1v1** and **2v2** matchmaking.

**Shared Tickets, Separate Boards:** Players manage their own ingredient boards but compete to
fulfill a shared queue of customer orders. The first to serve the dish gets the cash.

**Sabotage (Optional Mechanic):** By achieving specific bounce combos, players can send "bad
ingredients" or obstacles over to their opponent's board, slowing down their knife.

---

## 6. Future Updates Roadmap (Placeholder)

- New restaurant themes (e.g. Sushi Bar, Pizzeria) featuring unique board layouts and obstacles.
- Unique knife skins and classes (e.g. a Cleaver that smashes through barriers, or a fast Dagger
  with lower momentum).
- Special VIP customers that act as "Boss Battles."
