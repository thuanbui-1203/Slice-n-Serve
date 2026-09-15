# Superseded — see `section-2-core-mechanics.md`

This file was a **Section 2 plan draft**, written while this folder briefly used a
`NN-<slug>.md` naming convention. It is superseded by the reconciled plan.

**Read instead:**

| Document | What it is |
| --- | --- |
| [`section-2-core-mechanics.md`](./section-2-core-mechanics.md) | The Section 2 plan, reconciled with Section 1. It owns **Phase 0** of the project. |
| [`section-2-implementation-steps.md`](./section-2-implementation-steps.md) | The Section 2 runbook. |
| [`README.md`](./README.md) | The index, the locked-decision log and the reconciliation notes. |

Most of this draft's *decisions* survived the reconciliation and are carried forward in
`section-2-core-mechanics.md`: the 19.2 × 10.8 board box, the `0.02` fixed timestep, non-solid
ingredients collected by a swept `Rigidbody2D.Cast`, the `Knife`/`Ingredient`/`BoardWall`/
`Obstacle` layer scheme at indices 8–11, the `Board` action map, and the `BoardConfig`
ScriptableObject.

Two things in it did **not** survive, and are worth knowing if you are reading this file for
history:

1. **This draft assumed it owned the project skeleton in isolation.** It treated Session 1's
   folder tree, assemblies and action-map names as things Session 2 consumed. The reconciliation
   settled ownership the other way round — Session 2's Phase 0 *is* the skeleton — but it also
   established what Section 1 contributes on top of it (platform targets, the orientation lock,
   the aspect policy, safe-area handling, performance budgets and cross-platform verification).
2. **Its §4 "amendment" is moot.** This draft believed it was changing a locked
   `Knife ↔ Ingredient` collision decision that another session had set. The reconciliation
   records that decision as Section 2's own (`README.md` D6), so there was nothing to amend.

Do not follow this file. Follow `section-2-core-mechanics.md` and
`section-2-implementation-steps.md`.
