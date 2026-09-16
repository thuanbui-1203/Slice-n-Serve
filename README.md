# Slice & Serve

**Action-arcade · cooking management · semi-idle** — for PC and mobile, built in Unity 6.

> Overcooked meets Peggle/Pong: bounce a kitchen knife off the walls to slice the ingredients your
> customers are waiting for.

---

## About

Slice & Serve is an action-arcade cooking game with a management layer, for PC and mobile.

You are a cook running a restaurant that is far too busy for you. There is no cutting board: instead
you aim and launch a kitchen knife across a board packed with ingredients, and it caroms off the
walls like a ping-pong ball, slicing everything it touches. What you slice is what you cook with.

Customers arrive with specific requests. The ingredients you gather fill those orders automatically,
but every customer has a patience bar and it does not wait for you. Serve in time and you are paid;
serve too slowly and they leave angry, and your reputation pays for it.

Cash from served dishes buys upgrades in two directions. Some make you faster — a quicker knife, a
chance for it to split mid-flight, a denser board. Others work while you don't: sous-chef helpers
that launch knives on their own. That split is the point. The game starts as a reflex test and ends
as something closer to an idle game you nudge, with your aim still the thing that separates a good
run from a bad one.

### The loop

| | | |
| --- | --- | --- |
| **1. Launch** | drag and release | aim and fire the knife with a calculated velocity and angle |
| **2. Slice** | the knife ricochets | it collects whatever it crosses, and only walls change its direction |
| **3. Serve** | ingredients credit orders | gathered ingredients fill the oldest customer still waiting on them |
| **4. Spend** | cash becomes upgrades | knife speed, split chance, spawn rate, patience, auto-knives |

The skill is geometry, not reflexes alone: where you launch, and which walls you use. The upgrades
are what let you keep up when the orders get faster than you.

---

## Status

**Pre-implementation.** The project is a fresh Unity 6 URP 2D template. The design and the planning
documents are written and reconciled; **no gameplay code exists yet**. `Assets/Scripts/GameManager.cs`
is an empty auto-generated stub, not a design decision.

| | |
| --- | --- |
| Engine | Unity `6000.3.16f1`, URP `17.3.0` with the 2D Renderer |
| Genre | Action-arcade / cooking management / semi-idle |
| Targets | Windows x64 (primary dev + PC), Android ARM64 (primary mobile), iOS configured but not built |
| Orientation | Landscape only (not yet locked in `ProjectSettings` — see the §1 runbook) |
| Input | `com.unity.inputsystem` `1.19.0`; Input System package only, legacy `Input` is disabled |
| Tests | `com.unity.test-framework` `1.6.0` |

---

## Documentation

[`docs/concept.md`](docs/concept.md) is the source of truth for *what* the game is. The plans are the
source of truth for *how* it gets built.

| Document | What it is |
| --- | --- |
| [`docs/concept.md`](docs/concept.md) | The game concept document |
| [`docs/plans/README.md`](docs/plans/README.md) | Planning index: baseline, locked decisions, open decisions |
| [`docs/plans/section-1-game-overview.md`](docs/plans/section-1-game-overview.md) | §1 plan — the cross-platform frame |
| [`docs/plans/section-1-implementation-steps.md`](docs/plans/section-1-implementation-steps.md) | §1 runbook |
| [`docs/plans/section-2-core-mechanics.md`](docs/plans/section-2-core-mechanics.md) | §2 plan — the board, knife, ingredients, orders, patience |
| [`docs/plans/section-2-implementation-steps.md`](docs/plans/section-2-implementation-steps.md) | §2 runbook |

**A plan is not the project.** Both runbooks are written to be followed *by hand* in the Unity
Editor. Treat the plans as the specification and the repository as the current state: this README
describes the game **as designed**, not as built.

---

## Opening the project

1. Install **Unity `6000.3.16f1`** through Unity Hub. Opening with a different version silently
   rewrites `ProjectSettings/`, which produces a large, meaningless diff.
2. Add **Android Build Support** (+ OpenJDK + Android SDK & NDK Tools) and **Windows Build Support
   (IL2CPP)** through Hub → Installs → gear → Add Modules. Neither is needed to open the project,
   only to build it.
3. Open the folder. `Library/`, `Temp/` and `Logs/` are generated and gitignored — never commit them.

---

## Repository notes

- Two remotes: `origin` → GitHub (`Slice-n-Serve`), `gitlab` → `gitlab.duthu.net/520h0425/cuoikylaptrinhgame`.
- `.gitattributes` defines a Git LFS attribute macro (`filter=lfs diff=lfs merge=lfs -text`) but no
  paths currently use it. Decide the LFS policy **before** importing binary art.
- `reasonix.toml` (Reasonix tooling config) is intentionally gitignored; it is local-only.
