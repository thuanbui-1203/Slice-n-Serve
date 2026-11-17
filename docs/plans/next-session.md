# Session plan — status

## Done (2026-11-17)
- **Asset search** (user downloads manually — scripted downloads are blocked):
  - Chandelier: [Street Lamps, Chandeliers and Candles](https://robertoanta.itch.io/street-lamp) — CC0,
    $1 minimum, 64×32 and 64×64 chandeliers, static. Free alternative with no stated licence:
    [Floor lamps, candelabra, chandeliers](https://unistaja007.itch.io/floor-lamps-candelabra-chandeliers).
    Put one PNG in `Assets/Art/Chandelier`; the code sways it and flickers its glow.
  - Dice: [32x32 pixel d6 dice roll & results](https://opengameart.org/content/32x32-pixel-d6-dice-roll-results)
    (CC0, has a roll animation — layout unverified) or [Pixel Dice Pack](https://outercloudstudio.itch.io/pixel-dice-pack)
    (CC0, free, faces only; the code tumbles them). Put them in `Assets/Art/Dice`.
- **Die = bounce budget, no friction** — `KnifeController.BounceBudget`, `KnifeManager.BounceBudget`,
  `DiceView`, `DiceRolled` event, `bounceRetention` = 1.
- **Auto-throw** — `AutoThrower`, `UpgradeService.AutoThrowUnlocked`, `GameSettings.AutoThrow`, HUD
  switch, `AimInput.TapMode` + `IngredientSpawner.SpawnNear` (restock taps).
- **PvP groundwork** — `GameRandom` seeded service; design in `section-5-pvp.md`.
- Tests: EditMode 75/75, PlayMode 24 + 5 explicit skips, on the verify copy.

## Done (2026-11-17, later)
- Pause / Kitchen closed cards use the upgrade cards' font (Sol Schori) for text and buttons.
- Chandelier: no free animated one exists; [Medieval Street Lamps](https://mirquiso.itch.io/medieval-street-lamps)
  ($1, animated flame) is the best match. Any sheet in `Assets/Art/Chandelier` now animates (a row of
  3+ square frames, or a sheet already sliced into several sprites); a single image sways.
- SFX list to replace: `docs/plans/sfx-todo.md`.
- Dev buttons (add money / reset) in the detachable `Assets/Scripts/DevTools`.
- **PvP v1** — see `section-5-pvp.md` "Implemented (v1)".

## Next
- User: run Build Everything; download the chandelier/dice packs if wanted, then rebuild.
- If a downloaded dice pack's layout isn't picked up (needs 6 faces in order), adjust `BuildDice`.
- PvP: follow "Next steps" in `section-5-pvp.md`; answer its open questions first.
