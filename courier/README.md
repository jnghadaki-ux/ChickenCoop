# Chicken Courier

> The bomb has feathers now.

**A Counter-Strike 2 bomb mode where a chicken carries the C4. A standalone plugin inspired by
[ChickenCoop](../README.md). Created by Gdaki, co-authored with Claude (Anthropic) using Claude Code.**

Chicken Courier is its own plugin: it doesn't need ChickenCoop and works on any server. It borrows ChickenCoop's
approach of driving CS2's own chicken AI.

On bomb maps, nobody spawns with the C4. It's strapped to the back of a big chicken instead:
- **Terrorists** have to escort the chicken to a bombsite, where it lays the bomb for them to plant.
- **Counter-Terrorists** can shoot it to break its nerve and send it running, or walk up and scare it.

The chicken never dies, so the bomb is never lost. Every round becomes an escort mission.

**Status:** version 1.0.0. It compiles against CounterStrikeSharp 1.0.376 (.NET 10) and passes 10 unit tests, but
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

**Optional:** with `DropBombOnPanic`, a panicking chicken drops the bomb where it stands. Terrorists can grab it and
plant it the normal way, and the courier's job is over.

---

## Installation

1. You need CounterStrikeSharp **1.0.376 or newer** (`with-runtime`).
2. Download [`ChickenCourier-1.0.0.zip`](../releases/ChickenCourier-1.0.0.zip) from the repo's `releases/` folder and
   unzip it into `game/csgo/`. You should end up with
   `addons/counterstrikesharp/plugins/ChickenCourier/ChickenCourier.dll` plus a `lang/` folder.
3. **Change map once** so the chicken and C4 models are loaded, or restart the server.
4. The config is created on first load at `configs/plugins/ChickenCourier/ChickenCourier.json`. Every setting and its
   default is in [`ChickenCourier.example.json`](ChickenCourier.example.json).

By default it runs on every `de_*` map with at least 2 players, after warmup.

---

## Commands

| Command | Who | What it does |
|---|---|---|
| `!courier` / `css_courier` | everyone | Courier status: courage and handler |
| `css_courier on` / `off` | admins (`@css/generic`) | Turn the mode on or off live |
| `css_courier_spawn` | admins | Testing: spawn the courier where you stand (takes the bomb off players) |
| **E** near the chicken | Terrorists | Become its handler |
| **E** near the chicken | Counter-Terrorists | Scare it |

---

## Configuration

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Mode on or off |
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
| `Marker` / `CentreHud` / `ClucksEverySeconds` | on / on / `6` | Label, laying progress bar, clucks |

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

---

## Credits

- **Chicken Courier** is created by **Gdaki**, co-authored with **Claude** (Anthropic) using Claude Code. It's a
  standalone plugin inspired by **ChickenCoop**.
- **The chicken, C4 model, sounds and effects** are © Valve Corporation and ship with CS2.
- **Built on** [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).
