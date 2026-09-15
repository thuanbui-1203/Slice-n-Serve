# Superseded — see `section-1-game-overview.md`

This file was the **first draft** of the Section 1 plan. It is superseded and kept only as a
signpost.

It was written before `section-2-core-mechanics.md` — an already-existing sibling plan — was
discovered in the repository. That plan had independently claimed the project's foundations as
its own "Phase 0", and the two drafts disagreed on folder layout, assembly count, namespaces,
layer indices, the fixed timestep, the knife/ingredient collision rule, board size, scene
naming, the action-map name and the config type name.

**Read instead:**

| Document | What it is |
| --- | --- |
| [`section-1-game-overview.md`](./section-1-game-overview.md) | The reconciled Section 1 plan |
| [`section-1-implementation-steps.md`](./section-1-implementation-steps.md) | The Section 1 runbook |
| [`README.md`](./README.md#reconciliation-notes) | Why the reconciliation went the way it did |

**Do not follow the guidance in the git history of this file.** In particular it proposed an
`Assets/_Project/` tree, five assemblies, layers 8–11 named differently, a `1/60` fixed timestep,
a 16 × 9 board, `Boot`/`MainMenu`/`Restaurant` scenes and a `Gameplay` action map — all of which
the reconciled plan replaced.
