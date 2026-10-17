# Changelog

## 1.1.0

- **Delivery mode for deathmatch:** a courier with a feed sack appears every 45 seconds. Anyone takes it (or steals it)
  with E and walks it to a marked drop zone for scoreboard points, a heal and armour. Shooting it panics it and drops its
  handler. Free-for-all aware. Times out after 150 seconds.
- **`Mode`:** `auto` (Delivery on deathmatch, Bomb elsewhere), `bomb` or `delivery`.
- **PUG support:** `PauseAware` freezes the courier and its clocks during pauses and timeouts. `RequireC4` skips rounds
  where nobody spawned with the bomb (knife rounds, retakes).
- The courier stands still during freeze time.
- `!courier` shows the mode and when the next delivery is due. `css_courier_spawn` works in both modes.
- 16 new messages in all six languages. 20 unit tests.
- Not yet tested in game.

## 1.0.0

- First release: the C4 rides on a chicken.
- Terrorists escort it with E. Counter-Terrorists shoot it (it panics) or scare it.
- It lays the bomb at a bombsite for the Terrorists to plant.
- Courage, panic, auto-handler for bots, money rewards, a floating marker, and a laying progress bar.
- Six languages, 10 unit tests.
- Not yet tested in game.
