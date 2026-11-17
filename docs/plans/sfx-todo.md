# SFX to replace (note for later — nothing changed yet)

Every sound today is **synthesised at startup** in `Assets/Scripts/Runtime/UI/GameAudio.cs`
(`Tone` / `Sweep` / `Arpeggio`) and played from an event on the `EventBus`. To replace one, the
plan is: add an optional `[SerializeField] AudioClip` per sound in `GameAudio` (e.g. `bounceClip`);
when it is assigned, play it instead of the synthesised one. Drop the files in `Assets/Art/Audio/Sfx`
and let Build Everything assign them by file name.

| Sound to change | Today | Plays on (event → handler) | Note |
| --- | --- | --- | --- |
| Ending the run | `_gameOver` — falling square-wave arpeggio (GameAudio.cs:40) | `GameOver` → `OnGameOver` | |
| Knife bouncing | `_bounce` — short 1400 Hz blip (GameAudio.cs:35) | `KnifeBounced` → `OnBounced` | Fires a lot (every border hit, every knife): keep it short, pitch is randomised ±8 %. Could differ on the last bounce (`e.Remaining == 0`) and on the hit that ends the knife (`e.Remaining < 0`). |
| Finishing an order | `_serve` — rising 3-note arpeggio (GameAudio.cs:38) | `OrderServed` → `OnServed` | Plays when the player taps a ready ticket. There is **no** sound yet for an order becoming ready (`OrderReady`) — worth adding one. |
| Rolling the die | **none yet** | `DiceRolled` (published by `GameRunner.RollDie`) | Needs a subscription in `GameAudio`. The tumble lasts `DiceView.RollSeconds` (0.6 s): a rattle that length, or a rattle + a "clack" on landing. |
| Launching the knife | `_launch` — noisy upward sweep (GameAudio.cs:34) | `KnifeLaunched` → `OnLaunched` | Also plays for sous-chef, split and auto throws (`e.Source`) — maybe quieter or skip for helpers/splits. |
| Rush hour start | `_rush` — alarm arpeggio (GameAudio.cs:43) | `RushHourChanged` (`Active = true`) → `OnRush` | |
| Rush hour end | **none yet** | `RushHourChanged` (`Active = false`) | `OnRush` currently ignores the end; add a clip there. |

Volume for all of them goes through the Settings panel's SFX slider (`GameAudio.Play`).
