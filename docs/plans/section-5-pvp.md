# §5 PvP — design and groundwork

Status: **groundwork only** (2026-11-17). No PvP gameplay exists yet. This document is the plan;
`docs/concept.md` §5 is the source of truth for what PvP *is*.

## What the concept asks for

- 1v1 and 2v2.
- **Shared tickets, separate boards:** every player has their own board, knife, die and kitchen;
  all of them work one shared line of customers. The first to serve a dish gets the cash.
- **Sabotage (optional):** specific bounce combos send bad ingredients or obstacles to an opponent's board.

## Two ways in (user decision, 2026-11-16)

Two main-menu entries, one match model underneath:

| Entry | What it is | Transport |
| --- | --- | --- |
| **Play with Friends** | Same device: split screen, one board per player | `LocalMatchTransport` (in-process) |
| **Enter PvP match** | Online matchmaking against other players | `NetworkMatchTransport` (see below) |

Everything above the transport — the shared queue, scoring, sabotage, round flow — is written once
and does not know which transport it runs on.

## How the pieces map onto the existing code

| Need | Existing seam | PvP addition |
| --- | --- | --- |
| Same board for everyone | `GameRandom` (seeded) — **done** | The match host picks a seed; each board reseeds from it per round |
| Shared customer line | `IOrderQueue` (designed for this) | `SharedOrderQueue`: per-player progress on each order; `TryServe(player, orderId)` — first valid serve wins, the order leaves for everyone |
| Who did what | `EventBus` events | A `PlayerId` on launch / slice / ready / serve / bounce events |
| Sabotage trigger | `KnifeBounced` published for every bounce (with `Remaining` now) | A `SabotageRule` listening for combos; `IngredientSpawner.ForceSpawn` already exists for "spawn this here" |
| Pause / rounds | `RunClock` | The match owns the clock; no local pause online |
| Fair patience | Patience is fixed at spawn (plan §6.4) | Spawned once by the host, identical for all |

### What is per player vs. shared

- **Per player:** board (ingredients, knives, die, sous-chefs), wallet for the match, combo,
  auto-throw. Each player's `ServiceRegistry` slice: `Tuning`, `Random`, `Events`.
- **Shared:** the customer line (`SharedOrderQueue`), the match clock, reputation (or per-team
  reputation for 2v2 — open), the score.

### Online: what goes over the wire

Boards are **not** synced. Each client simulates its own board locally (physics never crosses the
network); only small events do:

- host → clients: match seed, order spawned (id, dish, customer, patience), order served by X,
  order expired, sabotage incoming, round start/end.
- client → host: "my progress on order N is now …", "serve order N", "sabotage combo hit".

The host is authoritative for the shared queue (who served first). This keeps bandwidth tiny and
avoids lockstep physics. Candidate stack: Netcode for GameObjects + Unity Relay + Lobby
(matchmaking). To decide when the online part starts.

### Same device

Split screen: the camera rig and `BoardViewportAdapter` become per player (two viewports), the
HUD per player, the dining room shared in the middle. Input: on touch, each half of the screen
belongs to one player (`AimInput` gains a screen region); on PC, a second input scheme is needed
(open: mouse + keyboard aiming, or a gamepad).

## Groundwork done

- **Seeded gameplay randomness** — `Core/GameRandom` in the `ServiceRegistry`: the die, auto-throw
  directions, split rolls, ingredient placement and picks, and order picks all draw from it
  (`DiceTests.SameSeed_SameRolls_SoBothPvpPlayersGetTheSameRun`). Cosmetic randomness stays on
  `UnityEngine.Random`. Not yet on it: `RushHour` (its own optionally-seeded `System.Random`) and the
  sous-chefs' aim when there is nothing to aim at (`UnityEngine.Random`).
- `KnifeBounced.Remaining` — bounce-combo sabotage rules can read how far into its roll a knife is.

## Next steps, in order

1. Seed `RushHour` from the match seed and move `SousChefCrew` onto `GameRandom`.
2. Add `PlayerId` to the gameplay events (default 0 = the single player; nothing changes in solo).
3. `SharedOrderQueue` + tests (first serve wins; progress per player; expiry for all) — EditMode only.
4. `PvpMatch` model + `IMatchTransport`, with `LocalMatchTransport` and a fake for tests.
5. Main menu: "Play with Friends" and "Enter PvP match" (the latter "coming soon" until online works).
6. Split-screen scene setup for Play with Friends (two boards, shared dining room).
7. Online: pick the stack, then `NetworkMatchTransport`, lobby/matchmaking UI.
8. Sabotage rules and the bad-ingredient / obstacle content.

## Open questions

- Reputation: shared, per player, or per team in 2v2? What ends a match — a timer, or reputation?
- Does the upgrade shop apply in PvP (progression vs. fairness)? Likely: PvP uses a fixed loadout.
- PC same-device input scheme (second player on keyboard or gamepad?).
