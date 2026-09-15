# Superseded — see `section-2-implementation-steps.md`

This file was a **Section 2 runbook draft**, written while this folder briefly used a
`NN-<slug>.md` naming convention. It is superseded on two counts: the filename, and the
foundation it was built on.

**Read instead:**

| Document | What it is |
| --- | --- |
| [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) | The Section 2 plan. It owns **Phase 0** of the project (folders, `SliceNServe.Runtime`, layers, `Physics2D` settings, `Game.unity`, `BoardConfig`). |
| [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) | The Section 2 runbook — ordered, executable steps with a verification after each. |
| [`README.md`](./README.md) | The index, the locked-decision log and the reconciliation notes. |

**Do not follow the guidance in this file.** It was written against Section 1's first draft,
which the reconciliation replaced. In particular it proposed:

| It proposed | The project uses |
| --- | --- |
| An `Assets/_Project/` folder tree | `Assets/{Scripts/Runtime,Prefabs,ScriptableObjects,Art,Scenes}` |
| Five assemblies (`Core`/`Gameplay`/`UI`/`App`/`Editor`) | One `SliceNServe.Runtime.asmdef` plus two test assemblies |
| `Fixed Timestep = 1/60` | `0.02` (50 Hz) — §2's tunneling budget is computed against it |
| A 16 × 9 board box | `19.2 × 10.8`, `worldBounds = Rect(-9.6, -5.4, 19.2, 10.8)` |
| Layers `Board`/`Knife`/`Ingredient`/`Obstacle` at 8–11 | `Knife`/`Ingredient`/`BoardWall`/`Obstacle` at 8–11 |
| A `Gameplay` action map with `Aim`/`AimPress` | A `Board` action map with `Aim`/`Launch` |
| A `GameConfig` ScriptableObject | `BoardConfig` in `SliceNServe.Data` |
| `Boot`/`MainMenu`/`Restaurant` scenes | `Game.unity` |

The one thing it got right by accident — that ingredients are non-solid pickups collected by a
swept cast rather than by trigger callbacks — is carried forward in the real runbook and is
locked as `README.md` decision D6.
