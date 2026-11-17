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

## Next
- User: run Build Everything; download the chandelier/dice packs if wanted, then rebuild.
- If a downloaded dice pack's layout isn't picked up (needs 6 faces in order), adjust `BuildDice`.
- PvP: follow "Next steps" in `section-5-pvp.md`; answer its open questions first.
