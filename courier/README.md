# Chicken Courier

> The bomb has feathers now.

**A Counter-Strike 2 mode where a chicken carries the C4, with a deathmatch delivery mode and PUG support. A standalone plugin inspired by
[ChickenCoop](../README.md). Created by Gdaki, co-authored with Claude (Anthropic) using Claude Code.**

Chicken Courier is its own plugin: it doesn't need ChickenCoop and works on any server. It borrows ChickenCoop's
approach of driving CS2's own chicken AI.

On bomb maps, nobody spawns with the C4. It's strapped to the back of a big chicken instead:
- **Terrorists** have to escort the chicken to a bombsite, where it lays the bomb for them to plant.
- **Counter-Terrorists** can shoot it to break its nerve and send it running, or walk up and scare it.

The chicken never dies, so the bomb is never lost. Every round becomes an escort mission.

On **deathmatch** servers it switches to [Delivery mode](#delivery-mode-deathmatch): every so often a courier turns up
with a sack of feed, and whoever walks it to the drop zone scores. On **PUG / competitive** servers it
[stays out of the way](#pug-and-competitive-servers) of pauses, timeouts and knife rounds.

**Status:** version 1.1.0. It compiles against CounterStrikeSharp 1.0.376 (.NET 10) and passes 20 unit tests, but
**hasn't been tested in game yet**. See the [first-run checklist](#first-run-checklist).

---

## How a round plays

1. **Round start.** The bomb is taken off whoever spawned with it, and the **courier chicken** appears next to them with
   the C4 on its back. A floating **💣 COURIER** label and courage bar are visible to everyone, and it clucks so
   everyone can hear it.
2. **Escort.** A Terrorist presses **E** next to the chicken to become its **handler**, and it follows them using CS2's
   own chicken AI.
   - Anyone else on the team can take over with E.
   - If the handler dies or wanders more than 900 units away, the chicken stops and waits.
   - With nobody leading it for 10 seconds, the nearest Terrorist takes over automatically. If only bots are left
     alive, it happens straight away.
3. **Counter-Terrorist defence.**
   - **Shoot it:** each hit drains its **courage**. At zero it **panics**: it runs away from the shooter for 6 seconds
     and nobody can lead it.
   - **Scare it:** press **E** next to it for a short panic that also costs some courage. Each CT can do this once
     every 8 seconds.
   - **It recovers:** courage regenerates after 3 seconds without being hit.
   - **Money:** breaking its nerve pays $150, and a scare pays $50.
4. **Laying.** Inside a bombsite, with its handler within 400 units and not panicking, the chicken **sits and lays the
   bomb** over 4 seconds.
   - Both teams are warned, and the handler sees a progress bar in the centre of the screen.
   - A panic, a scare, or the handler leaving interrupts it, and laying starts over.
5. **The bomb is laid** as a normal C4 on the ground. Any Terrorist picks it up and plants it as usual, and the handler
   gets $300. The chicken flaps off and disappears.

From then on it's a normal round: plant, defuse, retake.

During **freeze time** the courier stands still and its clocks stop, so the auto-handler and panics don't run while
nobody can move.

**Optional:** with `DropBombOnPanic`, a panicking chicken drops the bomb where it stands. Terrorists can grab it and
plant it the normal way, and the courier's job is over.

---

## Delivery mode (deathmatch)

There's no bomb in deathmatch, so the courier carries a sack of feed instead and the whole server races for it.

1. **A courier appears** at a random spawn point 15 seconds into the match, then 45 seconds after each one ends. A
   **📦 DROP ZONE** label appears at another spawn point at least 1400 units away, with a puff of feathers every 3 seconds.
2. **Anyone presses E** next to it to lead it. Anyone else can press E to **steal** it.
3. **Shoot whoever has it.** When the handler dies or runs off, the courier is free. Damage from anyone except the
   handler (and, in team deathmatch, their teammates) drains its courage. At zero it panics, drops its handler and runs.
   In free-for-all, everyone counts as an enemy.
4. **The handler's screen** shows the distance to the drop zone and the time left.
5. **Delivering:** in the drop zone, with the handler nearby, it takes 3 seconds. The handler gets **+3 scoreboard
   points**, a full heal and full armour.
6. **No delivery within 150 seconds** and the courier flies off. The next one comes 45 seconds later.

`"Mode": "auto"` (the default) picks Delivery when the server runs deathmatch (`game_type 1`, `game_mode 2`) and Bomb
otherwise. Community deathmatch plugins that run on casual or competitive need `"Mode": "delivery"`.

Deathmatch servers often run more than `de_` maps, so add them to `Maps`, for example `["de_*", "aim_*", "dm_*"]`.

---

## PUG and competitive servers

- **Pauses and timeouts** (tactical, technical, `mp_pause_match`) freeze the courier with `PauseAware` on. It stands
  still, ignores damage and E presses, and every clock (panic, auto-handler, delivery deadline) resumes where it was.
  Its label reads **PAUSED**.
- **Knife rounds and bomb-less rounds** are left alone with `RequireC4` on. When no Terrorist spawned with the C4, there's
  no courier that round. This covers knife rounds from PUG plugins (MatchZy, PugSharp and others), retakes and practice
  modes.
- **Warmup** never has a courier.
- **Turn it off for a match:** `css_courier off`. Turn it on again with `css_courier on` (it starts from the next round).

Suggested settings for competitive play (no extra money in the economy, quieter):
```json
"RequireC4": true, "PauseAware": true,
"LayReward": 0, "PanicReward": 0, "ScareReward": 0,
"ClucksEverySeconds": 12
```

---

## Installation

1. You need CounterStrikeSharp **1.0.376 or newer** (`with-runtime`).
2. Download [`ChickenCourier-1.1.0.zip`](../releases/ChickenCourier-1.1.0.zip) from the repo's `releases/` folder and
   unzip it into `game/csgo/`. You should end up with
   `addons/counterstrikesharp/plugins/ChickenCourier/ChickenCourier.dll` plus a `lang/` folder.
3. **Change map once** so the chicken, C4 and feed-sack models are loaded, or restart the server.
4. The config is created on first load at `configs/plugins/ChickenCourier/ChickenCourier.json`. Every setting and its
   default is in [`ChickenCourier.example.json`](ChickenCourier.example.json).

By default it runs on every `de_*` map with at least 2 players, after warmup: Bomb mode normally, Delivery mode on
deathmatch.

---

## Commands

| Command | Who | What it does |
|---|---|---|
| `!courier` / `css_courier` | everyone | Courier status: mode, courage and handler, or when the next delivery comes |
| `css_courier on` / `off` | admins (`@css/generic`) | Turn the mode on or off live |
| `css_courier_spawn` | admins | Testing: spawn a courier where you stand (Bomb mode takes the bomb off players; Delivery mode picks a drop zone) |
| **E** near the chicken | Terrorists (Bomb) | Become its handler |
| **E** near the chicken | Counter-Terrorists (Bomb) | Scare it |
| **E** near the chicken | anyone (Delivery) | Take it, or steal it from its handler |

---

## Configuration

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Mode on or off |
| `Mode` | `auto` | `auto` (Delivery on deathmatch, Bomb elsewhere), `bomb` or `delivery` |
| `Maps` | `["de_*"]` | Maps where it runs (`*` = prefix wildcard) |
| `MinPlayers` | `2` | Below this many players, normal bomb rules apply |
| `Language` / `ChatPrefix` | `en` / `{orange}[Courier]{default}` | Messages |
| `AdminPermissions` | `["@css/generic"]` | Flags for the admin commands |
| `Model` / `Scale` | `models/chicken/chicken.vmdl` / `1.15` | Courier look (also `chicken_polish.vmdl`, `chicken_silkie.vmdl`) |
| `Courage` / `DamageToCourage` | `120` / `1.0` | How much CT damage it takes to panic it |
| `PanicSeconds` / `CourageAfterPanic` / `CourageRegen` | `6` / `70` / `4 per second` | Panic length and recovery |
| `HandlerRange` / `LeashRange` | `130` / `900` | Range to take over / range before the handler lets go |
| `AutoHandlerSeconds` / `AutoRange` | `10` / `700` | Automatic handler (0 = off; bots always auto-handle) |
| `LaySeconds` / `LayHandlerRange` | `4` / `400` | Laying time, and how close the handler must stay |
| `SiteRadius` | `280` | Bombsite size when a map's bombsite bounds can't be read |
| `CtCanScare` / `ScareRange` / `ScareCourage` / `ScarePanicSeconds` / `ScareCooldownSeconds` | on / `150` / `30` / `2.5` / `8` | The CT scare |
| `DropBombOnPanic` | `false` | A panicking chicken drops the bomb |
| `LayReward` / `PanicReward` / `ScareReward` | `$300` / `$150` / `$50` | Money rewards |
| `RequireC4` | `true` | Bomb mode: skip the round when no Terrorist spawned with the C4 (knife rounds, retakes) |
| `PauseAware` | `true` | Freeze the courier during pauses and timeouts |
| `DeliveryEverySeconds` / `DeliveryTimeLimit` | `45` / `150` | Delivery: gap between couriers, and time to deliver one |
| `DropZoneMinDistance` / `DropZoneRadius` | `1400` / `140` | Delivery: how far away the drop zone is, and its size |
| `DeliverySeconds` | `3` | Delivery: time in the drop zone to deliver |
| `StealWithE` | `true` | Delivery: E takes the courier from its handler |
| `DeliveryScore` / `DeliveryHeal` / `DeliveryArmor` / `DeliveryMoney` | `3` / on / on / `0` | Delivery rewards |
| `CargoModel` | `models/chicken_feed/chicken_feed_bag.vmdl` | Delivery: what the courier carries |
| `Marker` / `CentreHud` / `ClucksEverySeconds` | on / on / `6` | Label, progress bar and drop-zone distance, clucks |

### Example setups

**Chaos:** the chicken panics easily and drops the bomb.
```json
"Courage": 60, "DropBombOnPanic": true, "ScareCourage": 60
```

**Tanky courier, fast laying:**
```json
"Courage": 250, "LaySeconds": 2.5, "CtCanScare": false
```

---

## How it works

- **The courier is CS2's own `chicken` entity.** It's spawned with the `chicken_model` setting, made tougher and
  bigger, and set not to block players.
- **Following uses the chicken's built-in leader (`m_leader`),** pointed at the handler five times a second. This also
  undoes other players' E presses, since pressing E on a chicken normally makes it follow you.
- **Panics** use the chicken's own flee target (`m_fleeFrom`) and its panic animation.
- **The C4** is a prop attached to the chicken's back. The real bomb is taken from the players at round start and
  spawned as a normal dropped `weapon_c4` when the chicken lays it.
- **Bombsites** are read from the map's `func_bomb_target` bounds, with the bombsite centres stored by the game as a
  fallback.
- **Damage** to the courier is always blocked; CT damage only drains its courage. The courier can never die.
- **The mode** comes from the `game_type` / `game_mode` ConVars, and free-for-all from `mp_teammates_are_enemies`.
- **Pauses** are read from the game rules (`FreezePeriod`, `MatchWaitingForResume`, the timeout flags). When play
  resumes, every deadline is pushed back by the length of the pause.
- **Drop zones** are picked from the map's `info_deathmatch_spawn` points, or its team spawns when it has none.
- **The rules** (courage, panic, laying, bombsite checks, handler choice) live in engine-free code (`CourierState`)
  covered by unit tests.

**Languages:** English, Spanish, Portuguese, Russian, German and Polish. Each player sees their own CS2 language. To add
one, copy `lang/en.json`.

---

## First-run checklist

1. **The bomb is removed at round start** and the chicken appears next to the former carrier, with the C4 on its back.
   If the C4 sits at an odd angle, tell me and I'll adjust the angle.
2. **E makes it follow you,** and other players' E presses don't steal it.
3. **CT shots panic it,** and it runs off.
4. **In a bombsite it sits and lays.** The C4 appears on the ground and can be planted normally.
5. **Bots:** with bots as Terrorists, the chicken should follow the nearest bot.
6. **PUG:** call a tactical timeout. The label reads PAUSED and the courier stands still until play resumes. A knife
   round has no courier.
7. **Deathmatch:** about 15 seconds in, a courier with a feed sack and a DROP ZONE label appear. Walking it in gives
   +3 points. If the feed sack sits oddly on its back, tell me and I'll adjust it.

---

## Credits

- **Chicken Courier** is created by **Gdaki**, co-authored with **Claude** (Anthropic) using Claude Code. It's a
  standalone plugin inspired by **ChickenCoop**.
- **The chicken, C4 model, sounds and effects** are © Valve Corporation and ship with CS2.
- **Built on** [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).
