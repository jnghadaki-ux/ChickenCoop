# ChickenCoop

> Your Chickogatchi can survive outside the menu, just don't let the enemy team catch him first.

**Pet chickens for Counter-Strike 2, created by Gdaki.**

ChickenCoop is a [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) server plugin that takes the
pet chicken from the CS2 main menu and brings it into the match. Every player on your server gets their own chicken.

- **Raise it:** find an egg, hatch it, and raise the chick into a pullet and then a hen.
- **Look after it:** feed it, name it, pet it, teach it tricks and dress it in hats.
- **Take it into the round:** it follows you, panics when you die, and the enemy team can shoot it, so look after it.

**Status: ✅ confirmed working.** Tested by Gdaki on a live CS2 server. Version 1.0.0, built against
CounterStrikeSharp 1.0.376 on .NET 10, with 30 passing unit tests for the growth, food, hat and save rules.

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Player guide](#player-guide)
  - [The life of a pet](#the-life-of-a-pet)
  - [Breeds and coats](#breeds-and-coats)
  - [Food and feed bags](#food-and-feed-bags)
  - [Petting, tricks, sit and come](#petting-tricks-sit-and-come)
  - [Names](#names)
  - [Hats](#hats)
  - [When your pet dies](#when-your-pet-dies)
  - [The journal](#the-journal)
- [Commands](#commands)
- [Configuration](#configuration)
  - [Example setups](#example-setups)
  - [Full settings reference](#full-settings-reference)
- [Saving and data](#saving-and-data)
- [How it works](#how-it-works)
- [Game assets used](#game-assets-used)
- [Troubleshooting and FAQ](#troubleshooting-and-faq)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [Credits](#credits)

---

## Features

- **The real CS2 pet chicken.** It uses the models, 40 coats, egg, nest, feed, photo-booth hats, sounds and effects
  Valve added with the Chicken Egg in September 2026. They're loaded from the game's own files, so **players download
  nothing**.
- **Four life stages:** egg, chick, pullet and hen. The pet grows while its owner plays on your server.
- **A hatching scene.** The egg waits in a nest at your spawn and rocks more as hatching gets close. Then it taps,
  cracks, puffs out feathers, and the chick runs over to you.
- **Breed and coat revealed at the pullet stage.** There are three breeds (Catalana, Polish, Silkie) and 40 coats,
  and the reveal is announced to the server.
- **Real chicken behaviour.** The pet follows its owner using CS2's own chicken AI, so it walks, runs, jumps and
  glides with the real animations. If it gets left behind or stuck, it's teleported back.
- **Interaction:** pet it (press `E`), feed it, do tricks, make it sit or come, rename it, give it hats, hide it at home.
- **Ten unlockable hats** from Valve's photo booth, each earned through a milestone.
- **Floating name tags** such as "Gdaki's Nugget". The game also shows its own **"<owner>'s Pet Chicken"** label over
  every pet, because the plugin sets the chicken's owner.
- **Danger:** by default the enemy team can kill your pet. You get told how it died, it comes back next round, and you
  get a revenge message if you kill the killer.
- **Hunger:** feed bags are earned by playing rounds. A hungry pet stops growing, but by default it never dies of
  hunger.
- **Journal:** lifetime stats for your pet and a list of the pets you've had before.
- **Crash-safe saving** to one JSON file. No database needed.
- **Admin tools** to give pets, feed bags and hats, change breeds, and switch the whole plugin off and on.
- **Everything is configurable,** and every message can be translated.
- Works on **any map and any game mode:** casual, competitive, retakes, deathmatch, surf, jailbreak and so on.

---

## Requirements

| Component | Version |
|---|---|
| Counter-Strike 2 dedicated server | current build |
| [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) | 2.0 (dev build) |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) | **1.0.376 or newer**, the `with-runtime` download (it includes .NET 10) |

Your CS2 server needs to be on a build that includes the chicken pet assets, which arrived in September 2026. Any
up-to-date server has them.

---

## Installation

1. **Download** `ChickenCoop-1.0.0.zip` from the [`releases/`](releases) folder.
2. **Unzip it into `game/csgo/`** on your server. You should end up with:
   ```
   game/csgo/addons/counterstrikesharp/plugins/ChickenCoop/ChickenCoop.dll
   game/csgo/addons/counterstrikesharp/plugins/ChickenCoop/lang/en.json
   ```
3. **Start the server**, or type `css_plugins load ChickenCoop` in the server console. You should see:
   ```
   Chicken Coop 1.0.0 by Gdaki loaded: 0 pets on file
   ```
4. **Optional: edit the config.** The first load creates
   `game/csgo/addons/counterstrikesharp/configs/plugins/ChickenCoop/ChickenCoop.json`. Edit it, then run
   `css_plugins reload ChickenCoop` or change map. [`ChickenCoop.example.json`](ChickenCoop.example.json) shows every
   setting with its default value.

**Updating:** replace the files in `plugins/ChickenCoop/` but keep the `data/` folder, which holds everyone's pets.
Then reload the plugin.

**Uninstalling:** delete `plugins/ChickenCoop/` and `configs/plugins/ChickenCoop/`. Keep `plugins/ChickenCoop/data/`
if you might bring the pets back later.

---

## Quick start

To see everything straight away on your own server:

```
css_pet_give @me chick          // you get a chick right now
css_pet_givehat @me all         // unlock every hat
css_pet_bags @me 5              // some feed bags
```

Then in chat:
1. `!pet` opens the menu.
2. `!pet name Nugget` names it.
3. `!pet hat party` puts a hat on it.
4. `!pet trick` makes it hop.
5. `!pet feed` feeds it.
6. Look at it up close and press `E` to pet it.

To test alone, set `"BotsGetPets": true` in the config and add a few bots (`bot_add`). Every bot gets a random pet,
some wearing hats.

---

## Player guide

### The life of a pet

| Stage | Default length | What happens |
|---|---|---|
| **No pet** | 5 minutes of play | You find a **Chicken Egg**. |
| **Egg** | 30 minutes | The egg sits in a straw nest next to where you spawn each round. Past 25% it starts to rock and tap now and then, and more often as it gets close. When it's ready it hatches in front of you. |
| **Chick** | 120 minutes | A small yellow chick that follows you everywhere. Every chick looks the same. You get starter food and a random name. |
| **Pullet** | 240 minutes | It grows up a bit, and its **breed and coat are revealed** and announced to the whole server. |
| **Hen** | forever (by default) | Full-grown. Server owners can make hens retire after a while, and then a new egg turns up. |

- **Time is minutes of play, not real time.** Growth only counts while you're on a team (T or CT) on that server.
  Spectating or being offline doesn't count.
- **A chick or older pet needs food to grow.** See [Food](#food-and-feed-bags).
- Type `!pet info` at any time to see a progress bar and how long until the next stage.

### Breeds and coats

The breed and coat are rolled at random when you get your egg, but stay secret until the pullet stage, just like the
real game. By default the odds are **Catalana 50%, Polish 30%, Silkie 20%**, and every coat within a breed is equally
likely.

| Breed | Look | Coats |
|---|---|---|
| **Catalana** | the classic CS chicken | Black · Black White Head · Black with Red Head · Blue · Blue and Light Orange · Dark Orange · Grey · Light Blue and Red · Orange Blue Neck · Red Yellow Gradient · Tan · White · White Tan Head · Yellow Red Gradient · Yellow and Red |
| **Polish** | the one with the feather crest | Black with White Head · Blue · Blue Yellow · Brown · Brown with Black Wing Tips · Green · Green with Red Head · Orange White Blue · Orange with Black Stripes · Pink · White Black Head · White with Black Wing Tips |
| **Silkie** | the fluffy one | Aqua with Yellow · Black · Blue with Red Beard · Brown · Brown with Black Head · Leopard · Orange · Purple · Red · Red Beetle · Splash · Tiger · White |

### Food and feed bags

- **A hatched pet eats while you play.** Its food is counted in minutes of play.
- **A new chick starts with 120 minutes of food.**
- **Each feed bag adds 90 minutes,** up to a full bowl of 360 minutes. Feed with `!pet feed`.
- **You start with 2 feed bags** and earn **1 more every 4 rounds you play,** up to 10 bags.
- **When food runs low** (under 20 minutes) you get a chat warning, and your pet clucks for food and acts hungry.
- **With no food, your pet stops growing.** By default that's all: it never dies or leaves. Server owners can turn on
  `StarveRetireMinutes` if they want hunger to have real consequences.
- **Feeding scatters seed** in front of your pet. It stops to peck for a few seconds, then follows you again.
- **Eggs don't eat.**

### Petting, tricks, sit and come

- **Pet it:** look at your chicken from close up (about 2 metres) and press **`E`**. It clucks, sits happily for a
  moment, and you see a ♥ on screen. Petting counts toward the Glasses hat.
- **Trick:** `!pet trick` makes it hop into the air and flap back down. There's a short cooldown, and tricks count
  toward the Nose Glasses hat.
- **Sit:** `!pet sit` makes it stay where it is.
- **Come:** `!pet come` calls it back.
- **Hide or show:** `!pet hide` leaves it at home, so it doesn't come into the round. `!pet show` brings it back.
- **If it gets lost** (more than about 650 units away, or stuck behind something for 4 seconds), it's teleported back
  to you in a puff of feathers.
- **Other players can't steal it.** Only you can lead your pet.

### Names

- **Your pet gets a random name when it hatches,** such as Nugget, Omelette or Henrietta.
- **Rename it with `!pet name <name>`.** Names can be up to 20 characters and can't use colour codes or blocked words.
- **You can rename it once per life stage,** like the real game: once as a chick, once as a pullet and once as a hen.
- **Eggs can't be named.**
- The name shows on the floating name tag ("Gdaki's Nugget") and in chat messages.

### Hats

Pets can wear the ten hats from Valve's pet photo booth. Each one is unlocked by a milestone:

| Hat | How to unlock it |
|---|---|
| 🌱 Sprout | Hatch your egg |
| 🎉 Party Hat | Feed your pet 5 times |
| 👓 Glasses | Pet your chicken 25 times |
| ⛑️ Helmet | Grow into a pullet |
| 🪖 Armor Helmet | Play 50 rounds together |
| 🥸 Nose Glasses | Do 30 tricks |
| 🍌 Banana | Get 50 kills with your pet out |
| 🎩 Top Hat | Grow into a hen |
| 🧙 Wizard Hat | Avenge your pet 3 times |
| 👽 Alien | Lose your pet to fire, a Zeus **and** the bomb |

- **You're told in chat** the moment you unlock a hat.
- **Choose a hat** with `!pet hat` (menu), `!pet hat <name>`, `!pet hat off` or `!pet hat list`.
- **Server owners can unlock every hat** for everyone with `AllHatsUnlocked`.
- **Admins can gift hats** with `css_pet_givehat`.

### When your pet dies

- **By default only the enemy team can hurt your pet.** Teammates can't, and you can never hurt your own.
- **When it dies,** you're told how and who did it: "It took a Zeus to the beak", "It got a bit too close to the
  fire", "It was sitting on the bomb", and so on. The killer gets a message too.
- **Revenge:** kill the player who killed your pet in the same round and you get a **Revenge!** message. Three
  revenges unlock the Wizard Hat.
- **It comes back next round** (or after a set time, if the server owner sets one).
- **When you die,** your chicken panics, then sits down beside your body for the rest of the round.
- **Toughness by default:** chicks have 20 HP, pullets 35, hens 50.

### The journal

`!pet journal` shows:
- your pet's name, breed and coat, hatch date, and how long you've played together;
- rounds together, meals, pets, tricks, deaths (by fire, Zeus and bomb), revenges, and kills with it out;
- your **past pets**: retired or reset pets are remembered, with their breed, the stage they reached and how many
  rounds you had together.

---

## Commands

In chat, start a command with `!`, or with `/` to hide it from everyone else. In the server console, use the `css_`
form. For example, `!pet` and `css_pet` are the same command.

### Player commands

| Command | Also | What it does |
|---|---|---|
| `!pet` | `!chicken`, `!pet menu` | Opens the pet menu |
| `!pet info` | `status`, `stats` | Shows stage, growth bar, food, feed bags and hats |
| `!pet feed` | `eat` | Uses a feed bag |
| `!pet trick` | `jump` | Your pet hops and flaps |
| `!pet sit` | `stay` | Your pet waits where it is |
| `!pet come` | `follow`, `heel` | Calls your pet back |
| `!pet name <name>` | `rename` | Renames your pet |
| `!pet hat` | `hats` | Opens the hat menu |
| `!pet hat <name>` | | Puts on a hat, e.g. `!pet hat top hat` or `!pet hat wizard_hat` |
| `!pet hat off` | `none` | Takes the hat off |
| `!pet hat list` | | Lists every hat and how to unlock it |
| `!pet hide` / `!pet show` | | Leaves your pet at home / brings it along |
| `!pet journal` | `book`, `history` | Shows stats and your past pets |
| `!pet help` | | Shows the commands in chat |
| `!pets` | | Lists everyone's pets on the server |
| **`E`** on your pet | | Pets it |

### Admin commands

By default these need the `@css/generic` flag. Change that with `AdminPermissions` in the config.

| Command | What it does |
|---|---|
| `css_pet_give <target> <egg\|chick\|pullet\|hen>` | Gives a player a pet at that stage. Replaces their current one. |
| `css_pet_reset <target>` | Removes a player's pet. It's kept in their journal, and they find a new egg after the usual wait. |
| `css_pet_bags <target> <amount>` | Gives feed bags. A negative amount takes them away. |
| `css_pet_givehat <target> <hat id\|all>` | Unlocks a hat, or every hat, for a player. |
| `css_pet_breed <target> <catalana\|polish\|silkie> [coat #]` | Changes a pet's breed and coat. Coat numbers start at 1 and follow the [coat lists](#breeds-and-coats); leave the number out for a random coat. |
| `css_pet_skin <index\|group>` | Testing tool: shows that skin number (or material group) on **your own** pet, for checking coat colours. |
| `css_pet_toggle` | Turns pets off or on for the whole server, until the map changes or the config is reloaded. |
| `css_pet_save` | Writes the pet file to disk now. |

**Targets:** a player name (or part of one, if only one player matches), `#userid`, a SteamID64, or `@me` for
yourself.

**Hat ids:** `sprout`, `party`, `glasses`, `helmet`, `armor_helmet`, `nose_glasses`, `banana`, `top_hat`,
`wizard_hat`, `alien`.

---

## Configuration

The config file is `game/csgo/addons/counterstrikesharp/configs/plugins/ChickenCoop/ChickenCoop.json`. It's created
with the default values the first time the plugin loads. After editing it, run `css_plugins reload ChickenCoop` or
change map.

### Example setups

**Pets as a VIP perk:**
```json
"OwnerPermissions": ["@css/vip"]
```

**Fast and fun (a pet in minutes, no hunger, every hat):**
```json
"Growth": { "MinutesToFindEgg": 0, "EggMinutes": 2, "ChickMinutes": 10, "PulletMinutes": 20 },
"Food":   { "Enabled": false },
"Looks":  { "AllHatsUnlocked": true }
```

**Hardcore (pets can be shot by anyone, starving pets leave, hens retire):**
```json
"Combat": { "WhoCanHurtPets": "everyone", "AnnounceDeaths": true },
"Food":   { "StarveRetireMinutes": 120 },
"Growth": { "HenRetireMinutes": 600 }
```

**Peaceful (pets can't be hurt):**
```json
"Combat": { "WhoCanHurtPets": "nobody" }
```

**No pets on aim and 1v1 maps:**
```json
"DisabledMaps": ["aim_*", "1v1_*", "awp_*"]
```

### Full settings reference

#### General

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turns the whole plugin on or off. |
| `ChatPrefix` | `"{gold}[Coop]{default}"` | Chat prefix. Colour tags: `{default} {green} {lime} {gold} {yellow} {orange} {red} {lightred} {blue} {lightblue} {purple} {grey} {olive}`. |
| `Language` | `"en"` | Which file in `lang/` to use for messages. |
| `OwnerPermissions` | `[]` | Flags needed to own a pet. Empty means everyone. Admins can always own one. |
| `AdminPermissions` | `["@css/generic"]` | Flags needed for the admin commands. |
| `DisabledMaps` | `[]` | Maps where pets stay home. A trailing `*` matches any map starting with that text. |
| `HideDuringWarmup` | `false` | Keeps pets home during warmup. |
| `MaxActivePets` | `24` | Most pets out at once, server-wide (1 to 64). |
| `BotsGetPets` | `false` | Gives bots random pets that aren't saved, for testing. |
| `UseCenterMenu` | `true` | `true` uses the centre-screen menu; `false` uses the chat menu. |

#### Growth

| Setting | Default | What it does |
|---|---|---|
| `MinutesToFindEgg` | `5` | Minutes of play before a player without a pet finds an egg. 0 means straight away. |
| `EggMinutes` | `30` | Minutes from egg to chick. |
| `ChickMinutes` | `120` | Minutes from chick to pullet. |
| `PulletMinutes` | `240` | Minutes from pullet to hen. |
| `HenRetireMinutes` | `0` | Minutes before a hen retires and a new egg comes. 0 means never. |
| `BreedWeights` | `Catalana 50, Polish 30, Silkie 20` | Relative odds for each breed. Set one to 0 to never roll it. |

#### Food

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Set to `false` and pets never need feeding. |
| `StarterFoodMinutes` | `120` | Food a new chick starts with. |
| `MinutesPerBag` | `90` | Food one feed bag adds. |
| `MaxFoodMinutes` | `360` | Most food a pet can hold. |
| `StarterBags` | `2` | Feed bags a new owner starts with. |
| `RoundsPerBag` | `4` | Rounds played per feed bag earned. 0 means never. |
| `BagsPerRoundWin` | `0` | Extra bags for each round your team wins. |
| `MaxBags` | `10` | Most feed bags a player can hold. |
| `HungryWarningMinutes` | `20` | Warns the owner when food drops below this. |
| `StarveRetireMinutes` | `0` | Minutes of play without food before a pet leaves. 0 means it only stops growing. |

#### Behaviour

| Setting | Default | What it does |
|---|---|---|
| `TeleportDistance` | `650` | How far (in game units) a pet can fall behind before it's teleported back. |
| `StuckSeconds` | `4` | Seconds a pet can make no progress toward you before it's teleported back. |
| `PetDistance` | `110` | How close you must be to pet it with `E`. |
| `PetCooldownSeconds` | `2` | Wait between pets. |
| `TrickCooldownSeconds` | `4` | Wait between tricks. |
| `FeedCooldownSeconds` | `8` | Wait between feeds. |
| `StayWithBodySeconds` | `0` | How long the pet stays by its dead owner. 0 means the rest of the round. |
| `OwnerOnlyFollow` | `true` | Stops other players leading your pet away with `E`. |
| `IdleSoundSeconds` | `25` | Rough gap between idle clucks. 0 means none. |
| `NoBlock` | `true` | Pets don't block players. |

#### Combat

| Setting | Default | What it does |
|---|---|---|
| `WhoCanHurtPets` | `"enemies"` | `everyone`, `enemies` (the owner's enemy team) or `nobody`. Owners can never hurt their own pet. |
| `ChickHealth` / `PulletHealth` / `HenHealth` | `20` / `35` / `50` | Pet health at each stage. |
| `RespawnSeconds` | `0` | 0 means a dead pet comes back next round; above 0, it comes back after that many seconds. |
| `AnnounceDeaths` | `false` | Tells the whole server when a pet dies. |
| `Revenge` | `true` | Sends a revenge message when you kill your pet's killer. |
| `DeathParticles` | `true` | Shows the game's feather effect when a pet dies. |

#### Looks

| Setting | Default | What it does |
|---|---|---|
| `NameTags` | `true` | Shows a floating name above each pet. |
| `NameTagFormat` | `"{owner}'s {pet}"` | Name tag text. `{owner}` and `{pet}` are filled in. |
| `NameTagFontSize` | `40` | Name tag size. |
| `NameTagColor` | `"255,236,170"` | Name tag colour as R,G,B. |
| `ChickScale` / `PulletScale` / `HenScale` | `1.0` / `0.78` / `1.0` | Model size at each stage. |
| `ShowEggAtSpawn` | `true` | Shows the egg in its nest at your spawn. |
| `AllHatsUnlocked` | `false` | Makes every hat available without milestones. |
| `HatAttachment` | `""` | Model attachment to fix hats to. Empty means hats follow the body. |
| `ChickHatHeight` / `PulletHatHeight` / `HenHatHeight` | `13` / `19` / `24` | Hat height above the pet's feet. |
| `HatForward` | `4` | Moves hats forward toward the head. |
| `ChickHatScale` / `AdultHatScale` | `0.45` / `0.6` | Hat size. |
| `CoatMode` | `"Skin"` | How coats are switched: `Skin` (by number) or `MaterialGroup` (by name). |
| `CoatOverrides` | `{}` | Fixes a coat showing the wrong colour, e.g. `{ "Silkie/Leopard": "5" }`. |

#### Naming

| Setting | Default | What it does |
|---|---|---|
| `MaxLength` | `20` | Longest name allowed. |
| `RenamesPerStage` | `1` | Renames allowed per life stage. 0 means unlimited. |
| `BlockedWords` | `[]` | Words a name may not contain. Not case-sensitive. |
| `DefaultNames` | 20 names | Names picked at random for newly hatched pets. |

### Messages and translations

Every message is in `plugins/ChickenCoop/lang/en.json`. To change the wording, edit that file. To add a language:

1. Copy `en.json` to, for example, `de.json`.
2. Translate the text.
3. Set `"Language": "de"`.

Any message missing from your file falls back to English. `{0}`, `{1}` and so on are filled in by the plugin, and
colour tags like `{green}` work.

---

## Saving and data

- **Where:** all pets are stored in `game/csgo/addons/counterstrikesharp/plugins/ChickenCoop/data/pets.json`, one
  entry per player, keyed by SteamID64.
- **When:** the file is written every minute when something changed, when a player disconnects, on map change, when
  the plugin unloads, and on `css_pet_save`.
- **Crash-safe:** each save goes to a temporary file first, and the previous version is kept as `pets.json.bak`. If
  `pets.json` is ever damaged, the plugin loads the backup automatically.
- **Wiping everyone's pets:** stop the server and delete `pets.json` and `pets.json.bak`.
- **Bots** never save anything.

---

## How it works

For the curious, and for anyone changing the code.

- **The pet is the game's own `chicken` entity.** CS2's chicken accepts a `chicken_model` setting, so the plugin
  spawns a normal chicken with the chick, Catalana, Polish or Silkie model. The plugin then sets:
  - its health for the current stage;
  - its size (pullets are slightly smaller);
  - its coat, using the model's skin number;
  - its owner (`m_owner`). This is what makes CS2 draw its own "Reid's Pet Chicken" label above the pet, the same
    label the official pets use.
- **Following uses the chicken's built-in leader.** CS2 chickens have a hidden `m_leader` field, which is what makes a
  chicken follow you when you press `E` on it. The plugin points that at the owner, and the game's own chicken AI does
  the walking, running, jumping, gliding and animating.
- **Re-checking five times a second,** the plugin:
  - points the chicken back at its owner (this is what stops other players stealing it);
  - teleports it back if it's too far away or stuck;
  - plays the occasional idle cluck;
  - shows hunger when food is low.
- **Petting:** every server tick the plugin checks whether an owner has just pressed `E` while looking at their pet
  from close up.
- **Behaviours** (feeding, sitting, panicking, tricks) use the chicken's built-in pet activities, which have names
  like Feed, Sleep, Squat, Panic, Trick and LowOnFood.
- **Hats and name tags** are separate entities attached to the chicken. A hat is a prop using a photo-booth hat model;
  a name tag is a `point_worldtext` that turns to face whoever is looking at it.
- **The egg** is two props (nest and egg). The rocking is done by tilting the egg back and forth. When the egg hatches,
  it's swapped for the game's hatching-egg model, and then the chick spawns in its place.
- **Growth, food, hats and names** are handled in a separate, engine-free part of the code (`PetLogic`), which is
  fully unit-tested. Every 5 seconds the plugin tells it how much each player has played; it returns events such as
  "hatched", "became a pullet" or "hungry", and the plugin plays the matching sounds and effects.
- **Damage** goes through a check before it lands. Hits from the owner are always blocked, and teammates or world
  damage are blocked according to `WhoCanHurtPets`. Pet deaths are picked up from the game's `other_death` event,
  which also gives the cause of death.

---

## Game assets used

Everything below ships with CS2. The plugin only refers to these files by path and never includes or redistributes
them.

| Kind | Files |
|---|---|
| Pet models | `models/chicken/chick.vmdl`, `chicken.vmdl` (Catalana), `chicken_polish.vmdl`, `chicken_silkie.vmdl` |
| Egg and nest | `models/chicken/egg_pristine.vmdl`, `models/chicken/chicknegg.vmdl` (the hatching egg), `models/nest/nest.vmdl` |
| Feed | `models/chicken_feed/chicken_feed.vmdl`, `chicken_feed_bag.vmdl` |
| Hats | `models/photobooth/silly_hats/` (alien, armor_helmet, banana, glasses, helmet, nose_glasses, party, sprout, top_hat, wizard_hat) |
| Coats | `materials/models/chicken/chicken_{catalan,polish,silkie}_*` (40 materials) |
| Sounds | `Chicken.Egg.Hatch*`, `Chicken.Evolve.To.*`, `Chicken.Feed*`, `Chicken.Hungry*`, `Chicken.Idle.*`, `Chicken.Interact`, `Chicken.Flap.*`, `Chicken.Panic*`, `Chicken.Peck*` |
| Effects | `particles/critters/chicken/` (feathers, dust, burst) |

---

## Troubleshooting and FAQ

**Nobody gets a pet.**
- Check `Enabled` in the config, and that `css_pet_toggle` hasn't switched pets off.
- Check `OwnerPermissions` isn't limiting pets to a flag nobody has.
- Check the map isn't listed in `DisabledMaps`.
- Check `HideDuringWarmup` if you're in warmup.
- Remember a new player needs `MinutesToFindEgg` minutes **on a team** first.

**"The coop is full."**
- `MaxActivePets` has been reached. Raise it, up to 64.

**A pet shows the wrong coat colour.**
1. Get a pet of that breed: `css_pet_give @me hen`, then `css_pet_breed @me silkie`.
2. Step through the skins with `css_pet_skin 0`, `css_pet_skin 1`, and so on, until you see the colour you want.
3. Pin it in the config:
   ```json
   "CoatOverrides": { "Silkie/Leopard": "5" }
   ```

**Hats float above the head or sink into it.**
- Adjust `ChickHatHeight`, `PulletHatHeight`, `HenHatHeight`, `HatForward` and the hat scales.
- For hats that move with the head, try `"HatAttachment": "head"`.

**The pet keeps teleporting instead of walking.**
- Raise `TeleportDistance` or `StuckSeconds`.
- Maps with lots of ladders, drops or vents will always need the odd teleport, because chickens can't climb ladders.

**Players bump into pets.**
- `NoBlock` makes pets non-blocking where the engine allows it. A small nudge now and then is harmless.

**Can I reset one player's pet, or everyone's?**
- One player: `css_pet_reset <player>`.
- Everyone: stop the server and delete `data/pets.json` and `data/pets.json.bak`.

**Does it work with my game mode or other plugins?**
- Yes. ChickenCoop only adds its own entities and commands, and it works on any map and mode, including jailbreak.
- If a mode needs pets out of the way, use `DisabledMaps`, `HideDuringWarmup` or `css_pet_toggle`.

**Can pets be killed by the bomb or fire?**
- Yes, when `WhoCanHurtPets` allows it, and those deaths count toward the Alien hat.

---

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./build.sh
```

This runs the unit tests, builds a Release DLL, and writes the server package to `artifacts/ChickenCoop-<version>.zip`.

To run only the tests:

```bash
dotnet test tests/ChickenCoop.Tests.csproj
```

---

## Project layout

```
ChickenCoop.example.json     every setting with its default value
build.sh                     test + build + package
releases/                    ready-to-install zips
src/
  ChickenCoopPlugin.cs       plugin entry, events, growth timer, damage rules, deaths
  Pets.Spawn.cs              spawning the chicken, egg, nest, hat, name tag; hatching scene
  Pets.Behaviour.cs          following, teleporting, petting, tricks, sit/come, feeding
  Commands.cs                player and admin commands, menus
  Config.cs                  all settings with defaults
  Logic/PetLogic.cs          growth, food, breeds, hats, names (engine-free, unit-tested)
  Logic/PetStore.cs          crash-safe JSON saving
  Model/PetData.cs           what is saved per player
  Model/Catalog.cs           every game asset path, coat, hat and sound name
  Runtime/                   name tags, effects, message loader, per-pet entity state
  lang/en.json               every player-facing message
tests/
  PetLogicTests.cs           30 unit tests
```

---

## Credits

- **ChickenCoop** is created by **Gdaki**.
- **Chicken models, coats, hats, sounds and effects:** © Valve Corporation, part of Counter-Strike 2. They're loaded
  from the game's own files and nothing is redistributed.
- **Asset paths and engine fields** were found using the public
  [SteamDatabase GameTracking-CS2](https://github.com/SteamDatabase/GameTracking-CS2) file lists and schema dumps.
- **Built on** [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).
