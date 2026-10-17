# ChickenCoop

> Your Chickogatchi can survive outside the menu, just don't let the enemy team catch him first.

**Pet chickens for Counter-Strike 2. Created by Gdaki, co-authored with Claude (Anthropic) using Claude Code.**

ChickenCoop is a [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) server plugin that takes the
pet chicken from the CS2 main menu and brings it into the match. Every player can:

- **raise one:** find an egg, hatch it, and raise it from chick to pullet to hen;
- **look after it:** feed it, name it and pet it, and collect the eggs a hen lays;
- **have fun with it:** teach it emotes, dress it in hats, let it ride on your shoulder;
- **protect it:** the enemy team can shoot it, so keep it safe.

| Version | Status |
|---|---|
| **1.0.0** | ✅ **Confirmed working in game** (tested by Gdaki on a live CS2 server) |
| **1.1.0** | Compiles and passes its unit tests; **not yet tested in game**. See [First-run checklist](#first-run-checklist-for-110). |
| **1.2.0** | Adds the [Coop Arena](#coop-arena-pet-duels). Compiles and passes 64 unit tests; **not yet tested in game**. |
| **1.3.0** (current) | 🆕 [Moods, abilities, collections, admin tools, MySQL and more](#whats-new-in-130). Compiles and passes 106 unit tests (4 of them against a real MariaDB); **not yet tested in game**. See [First-run checklist for 1.3.0](#first-run-checklist-for-130). |

### Also in this repo: Chicken Courier

[**Chicken Courier**](courier/) is a **standalone plugin inspired by ChickenCoop**: a bomb game mode where a chicken
carries the C4. Terrorists escort it to a bombsite; Counter-Terrorists shoot or scare it. It's a plugin of its own and
works on any server, with or without ChickenCoop installed. Download it from
[`releases/ChickenCourier-1.0.0.zip`](releases/ChickenCourier-1.0.0.zip).

### Downloads

| Plugin | Version | File |
|---|---|---|
| ChickenCoop | **1.3.0** (moods, abilities, collections, admin tools; not yet tested in game) | [`releases/ChickenCoop-1.3.0.zip`](releases/ChickenCoop-1.3.0.zip) |
| ChickenCoop MySQL module | **1.0.0** (optional, for 1.3.0; not yet tested in game) | [`releases/ChickenCoop.MySql-1.0.0.zip`](releases/ChickenCoop.MySql-1.0.0.zip) |
| ChickenCoop | 1.2.0 (Coop Arena) | [`releases/ChickenCoop-1.2.0.zip`](releases/ChickenCoop-1.2.0.zip) |
| ChickenCoop | 1.1.0 | [`releases/ChickenCoop-1.1.0.zip`](releases/ChickenCoop-1.1.0.zip) |
| ChickenCoop | 1.0.0 (confirmed working) | [`releases/ChickenCoop-1.0.0.zip`](releases/ChickenCoop-1.0.0.zip) |
| Chicken Courier | 1.0.0 (not yet tested in game) | [`releases/ChickenCourier-1.0.0.zip`](releases/ChickenCourier-1.0.0.zip) |

Every version's zip stays in [`releases/`](releases), so you can always go back to an older one.

---

## Contents

- [What's new in 1.3.0](#whats-new-in-130)
- [Coop Arena: pet duels](#coop-arena-pet-duels)
- [What's new in 1.1.0](#whats-new-in-110)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [The menus](#the-menus)
- [Player guide](#player-guide)
  - [The life of a pet](#the-life-of-a-pet)
  - [Breeds, coats and shiny pets](#breeds-coats-and-shiny-pets)
  - [Food and feed bags](#food-and-feed-bags)
  - [Eggs: laying, gifting, trading, stealing](#eggs-laying-gifting-trading-stealing)
  - [Commands, petting and the shoulder perch](#commands-petting-and-the-shoulder-perch)
  - [Emotes](#emotes)
  - [Hats and looks](#hats-and-looks)
  - [When your pet dies](#when-your-pet-dies)
  - [Journal, leaderboards and settings](#journal-leaderboards-and-settings)
  - [Seasonal events](#seasonal-events)
- [Commands](#commands)
- [Configuration](#configuration)
- [Languages](#languages)
- [Plugin API (for other plugins)](#plugin-api-for-other-plugins)
- [Saving and data](#saving-and-data)
- [How it works](#how-it-works)
- [Game assets used](#game-assets-used)
- [Troubleshooting and FAQ](#troubleshooting-and-faq)
- [First-run checklist for 1.3.0](#first-run-checklist-for-130)
- [First-run checklist for 1.1.0](#first-run-checklist-for-110)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [Credits](#credits)

---

## What's new in 1.3.0

### Moods

Every pet has a **happiness** from 0 to 100, shown as hearts (♥♥♥♡) in the menu and the info screen. It decides the
pet's mood:

| Mood | Happiness | What it does |
|---|---|---|
| **Over the moon** | 85+ | Does tricks on its own now and then, and grows 10% faster |
| **Happy** | 60–84 | Does tricks on its own now and then |
| **Content** | 35–59 | Nothing special |
| **Grumpy** | 15–34 | A warning sign |
| **Sulking** | below 15 | Sits down and ignores you now and then; **refuses tricks, emotes and perching** until cheered up |

- **What cheers it up:**
  - petting (+4), feeding (+12), a trick or emote (+2), perching (+3) and every round together (+0.5);
  - petting, emotes and perching only count once per 45 seconds each, so spamming E doesn't work.
- **What wears it down:**
  - every hour of play (−8);
  - going hungry (−40 an hour instead);
  - being killed (−15).
- **Telling the owner:** the owner is told in chat whenever the mood changes. `!pet mood` shows it any time.
- **Settings:** everything is in the `Moods` config section. `Enabled: false` turns moods off. Optionally,
  `MoodInNameTag` shows hearts on the name tag.

### Abilities

Pets unlock **abilities** as they grow, and the owner picks one in **`!pet` › Abilities**, or with `!pet ability <name>`.
None of them help in a gunfight.

| Ability | Unlock | What it does | On by default |
|---|---|---|---|
| **Cheerleader** (`cheer`) | Hatch | Does a celebration trick when you get a kill or win a round | ✅ |
| **Forager** (`forager`) | Pullet | 20% chance per round to find a feed bag, if it was out with you and survived | ✅ |
| **Broody** (`broody`) | Hen | Lays eggs 30% faster | ✅ |
| **Charmer** (`charmer`) | Pet it 50 times | Its mood drops half as fast | ✅ |
| **Sentry** (`sentry`) | Pullet + 25 rounds together | While it sits (`!pet sit`), warns you when an enemy walks past it | ❌ gives away enemy positions |
| **Lookout** (`lookout`) | Hen + 100 rounds together | Panics and warns you when an enemy comes close | ❌ gives away enemy positions |

Sentry and Lookout are off by default because they reveal enemies; a server turns them on by adding them to
`Abilities.Allowed`. A map rule can switch abilities off on some maps; see
[Map and mode rules](#map-and-mode-rules).

### Collection

Every coat and shiny you raise goes into your **collection**, which never resets. Open it with **`!pet` › Collection**
or `!pet collection`.
- **What's in it:** each breed's coats (15 Catalana, 12 Polish, 13 Silkie) and the 4 shinies, 44 entries in all.
  Pets you raised before 1.3.0 count too: the journal's past pets are added on the first run.
- **The menu:** coats you haven't found show as **? ? ?**. A progress line covers hats, emotes and looks.
- **Rewards:** **1 feed bag** for each new entry, and **5** for completing every coat of a breed. A completed breed is
  announced to the server.
- **Leaderboard:** there's a new **Biggest collections** leaderboard.

### Admin tools

| Command | What it does |
|---|---|
| `css_pet_inspect <target\|steamid64>` | Everything about a player's pet, **even when they're offline** |
| `css_pet_restore <target\|steamid64> <n>` | Brings back past pet *n* from the journal (1 = most recent); the current pet goes to the journal |
| `css_pet_backup <target\|steamid64> [list\|bak\|yyyy-MM-dd]` | Restores a player from a backup (the previous save, or a daily copy) |
| `css_pet_rename <target\|steamid64> <name>` | Renames a pet (no rename limit) |
| `css_pet_mood <target> <0-100>` | Sets a pet's happiness |
| `css_pet_log [lines] [transfers]` | Recent admin actions, or recent gifts, wagers and bets |

The admin menu (`css_pet_admin` › Players › a player) has the same tools: Inspect, Cheer up, Bring back a past pet,
and Restore from a backup.
- **`data/admin.log`** records every admin action.
- **`data/transfers.log`** records every egg gift, wager and bet.

### Map and mode rules

`MapRules` switches features per map or per game mode. Rules apply in order, and later ones win:
```json
"MapRules": [
  { "Maps": ["jb_*", "ba_jail_*"], "Perch": false, "Arena": false, "Abilities": false },
  { "Modes": ["deathmatch"], "Pets": false },
  { "Maps": ["aim_*"], "MaxActivePets": 8, "NameTags": false }
]
```
- **Switches:** `Pets`, `Perch`, `Arena`, `NameTags`, `Abilities`, `Emotes` and `MaxActivePets`. Leave one out to keep
  it as it is.
- **Modes:** `casual`, `competitive`, `wingman`, `deathmatch`, `armsrace` and `custom`.

### Saving, backups and MySQL

- **Faster saves:**
  - only players whose data changed are serialized again;
  - the file is written on a background thread, so a save no longer stalls the server;
  - pets are also saved at the end of every round.
- **Daily backups:** a copy goes to `data/backups/pets-yyyy-MM-dd.json` once a day. The last 7 are kept
  (`Storage.KeepDailyBackups`). `css_pet_backup` restores one player from any of them.
- **MySQL / MariaDB** is a separate, optional module (`ChickenCoop.MySql`); see [MySQL module](#mysql-module).

### Limits against alt accounts

- **Egg gifts:**
  - 10 minutes between gifts and at most 3 a day (`Eggs.GiftCooldownMinutes`, `Eggs.MaxGiftsPerDay`);
  - the receiver must have played 20 minutes on the server (`Eggs.GiftMinPlayMinutes`).
- **Arena wagers and bets** need 20 minutes of play (`Arena.MinPlayMinutesForStakes`).
- **Play time** is counted for everyone from 1.3.0. Older players get their pet's age as a starting point.

### Performance

- **Name tag range:** name tags farther than 1500 units from a player aren't sent to them
  (`Performance.TagDrawDistance`).
- **Visible pet cap:** `Performance.MaxVisiblePets` (off by default) shows each player only their closest few pets.
- **Hat tweaks per look:** `Looks.HatTweaks` sets lift and size per look (`Chick`, `Catalana`, `Polish`, `Silkie`), in
  case a hat sits wrong on one breed only.

### API

New members in the shared API:

| Kind | Members |
|---|---|
| Methods | `GetMood`, `GetHappiness`, `AddHappiness`, `GetAbility`, `GetCollection` |
| Events | `MoodChanged`, `DuelFinished`, `EggGifted`, `CollectionAdded` |
| Capability | `IChickenCoopStorage`, for storage modules |

---

## Coop Arena: pet duels

*New in 1.2.0.* Two players' pets fight a turn-based duel in a small ring in front of the challenger, while the real
chickens act out every move with Valve's photo-booth effects.

**How to start one**
1. **Challenge someone:** `!pet duel [player]`, or **Arena › Challenge a player** in the menu.
2. **Pick a wager:** just for fun, or 1–3 feed bags (`Arena.MaxWager`). Both players put the bags in; the winner takes
   the pot.
3. **Your opponent answers:** they get a menu prompt, or can type `!pet accept` / `!pet decline`. The challenge waits
   20 seconds.
4. **Spectators can bet** during a 15-second countdown before the first turn (`!pet bet`). The whole betting pool is
   split between the people who backed the winner. A draw, or nobody backing the winner, refunds everyone.

**Each turn,** both owners pick a move at the same time (12 seconds; no answer counts as Guard):

| Move | Does |
|---|---|
| **Peck** | A normal hit. Half the time it misses a Flap. |
| **Guard** | Takes only 30% of a Peck and counter-pecks. Charges the special twice as fast. |
| **Flap** | A light hit. Dodges Specials completely. |
| **Special** | 150% damage and smashes through Guard, but a Flap dodges it. Needs a full charge (3). |

**Stats come from the pet's life:**
- **HP** grows with stage (hens are toughest) and rounds played together.
- **Attack** grows with meals, tricks and emotes.
- **Breed:** Silkies are tanky, Polish chickens hit harder, Catalanas are balanced. Shiny pets get +5% HP.
- **The special's look** is the flashiest effect emote the pet has unlocked (explosion, lightning, fire, lasers and so
  on). It's only cosmetic: every special hits equally hard.

**Winning:** the first pet to faint loses. After 12 turns, the pet with more of its HP left wins. The winner gets
confetti and a server announcement, and arena wins, losses and draws go into the journal and a new **Most arena wins**
leaderboard.

**Safety:**
- Arena chickens can't be hurt by gunfire.
- The owners' normal pets step aside during the duel.
- A round restart or a player leaving ends the duel fairly: the one who stayed wins; a restart is a draw with the
  wager refunded.

**Config** (`Arena` section):

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Arena on or off |
| `When` | `anytime` | `anytime`, `dead` (both duellists dead or spectating) or `warmup` |
| `MinStage` | `Chick` | Lowest stage that can fight |
| `RequestSeconds` | `20` | How long a challenge waits for an answer |
| `TurnSeconds` | `12` | How long each turn waits for moves |
| `MaxTurns` | `12` | Turn limit before the HP comparison decides |
| `CooldownSeconds` | `45` | Rest between duels for the same player |
| `MaxWager` | `3` | Most feed bags a duellist can wager (0 = no wagers) |
| `Betting` / `BetSeconds` / `MaxBet` | `true` / `15` / `3` | Spectator betting |
| `RingSize` | `80` | Gap between the two fighters |
| `Announce` | `true` | Tell the whole server about duels and results |

---

## What's new in 1.1.0

- **New menus, in the style of the SWBS Jailbreak mod.**
  - Drawn in the centre of the screen with a title, a subtitle, progress bars and pages.
  - **W/S** to move, **E** to select, **R** to go back, or type **!1–!9**.
  - A fix stops the panel flashing while a menu is open.
  - Submenus: Commands, Emotes, Wardrobe, Egg basket, Pet info, Journal, Leaderboards, Settings, Help and Admin.
- **Shoulder perch:** chicks and pullets can ride on your shoulder.
- **Hens lay eggs.** Pick them up and keep them in a basket, give one to a friend who has no pet (it becomes their egg),
  or trade eggs for feed. Optionally, enemies can steal eggs.
- **Shiny pets:** a 1-in-50 rare Golden, Rainbow, Shadow or Celestial chicken, revealed with confetti and announced to
  the server.
- **14 emotes** using Valve's pet photo-booth effects: confetti, bubbles, sparkles, fire, lightning, explosion, lasers,
  UFO, plus sit, nap, peck, panic, flap and spin. Some unlock through play.
- **Hide other players' pets** or **name tags**, per player.
- **Leaderboards:** oldest pets, most rounds, revenges, tricks, pets given and eggs laid.
- **Bird watch** (optional): your pet panics when an enemy comes close.
- **Seasonal events:** Halloween (pets rise from the grave and you unlock the *Ghost* look) and Christmas (*Frost* look
  and a *Snowman Hat*).
- **Translations:** English, Spanish, Portuguese, Russian, German and Polish. Each player sees their own language.
- **A plugin API** so other plugins (a store, a jailbreak mod, events) can give feed bags, eggs and hats, or keep pets
  out of the way during special rounds.
- **Hat fix:** hats now attach the way Valve's photo booth fits them, and there's a live switch to try the other
  placement modes (`css_pet_hatmode`). The plugin also warns admins when it was loaded mid-map, which stops hats and
  pet models from loading. See [Troubleshooting](#hats-dont-show-up-or-sit-in-the-wrong-place).

---

## Features

- **The real CS2 pet chicken.** It uses Valve's own models, 40 coats, egg, nest, feed, photo-booth hats, effects and
  sounds, loaded from the game files, so **players download nothing**.
- **Four life stages** (egg › chick › pullet › hen) that grow while the owner plays on your server.
- **A hatching scene.** The egg rocks in a nest at your spawn, taps, cracks, and the chick runs to you.
- **Real chicken AI.** The pet follows you with the game's own walking, running, jumping and gliding animations, and is
  teleported back if it gets lost or stuck.
- **Lots to do:** pet it (`E`), feed it, emotes, sit or come, perch, rename, hats and looks, hide at home.
- **A floating name tag** ("Gdaki's Nugget"), plus the game's own **"Gdaki's Pet Chicken"** label.
- **Danger and revenge:** enemies can kill your pet, deaths are explained, and avenging it is rewarded.
- **Hunger and feed bags.** By default a hungry pet only stops growing; it never dies.
- **Egg economy:** hens lay eggs, which you can gift or trade, and enemies can optionally steal them.
- **Rare shiny pets, unlockable hats, emotes and seasonal looks.**
- **Journal, past pets, leaderboards** and per-player settings.
- **Admin menu and commands**, a **plugin API**, **6 languages**, and **crash-safe JSON saving**.
- Works on **any map and any mode**: casual, competitive, retakes, deathmatch, surf, jailbreak and more.

---

## Requirements

| Component | Version |
|---|---|
| Counter-Strike 2 dedicated server | current build (needs the September 2026 pet assets) |
| [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) | 2.0 (dev build) |
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) | **1.0.376 or newer**, the `with-runtime` download (it includes .NET 10) |

---

## Installation

1. **Download** `ChickenCoop-1.2.0.zip` (or an older version) from [`releases/`](releases).
2. **Unzip it into `game/csgo/`.** It contains two folders, and both are needed:
   ```
   game/csgo/addons/counterstrikesharp/plugins/ChickenCoop/ChickenCoop.dll   (+ lang/)
   game/csgo/addons/counterstrikesharp/shared/ChickenCoop.Shared/ChickenCoop.Shared.dll
   ```
   `ChickenCoop.Shared.dll` is the plugin API. It goes in `shared/` so other plugins can use it.
3. **Restart the server**, or load the plugin and then change map once. The pet models and photo-booth hats are only
   loaded when a map starts. If you load the plugin mid-map, admins get a reminder to change map. On load the console
   shows:
   ```
   Chicken Coop 1.2.0 by Gdaki loaded: 0 pets on file, languages: de, en, es, pl, pt, ru
   ```
4. **Optional: edit the config.** It's created on first load at
   `game/csgo/addons/counterstrikesharp/configs/plugins/ChickenCoop/ChickenCoop.json`.
   [`ChickenCoop.example.json`](ChickenCoop.example.json) shows every setting with its default. After editing, reload
   the plugin or change map.

**Updating from 1.0.0:** replace both folders and keep `plugins/ChickenCoop/data/`, which holds everyone's pets. Old
saves load fine; new fields start at their defaults.

**Uninstalling:** delete `plugins/ChickenCoop/`, `shared/ChickenCoop.Shared/` and `configs/plugins/ChickenCoop/`.

---

## Quick start

Try everything on your own server:

```
css_pet_give @me pullet        // a pullet right now (breed and coat revealed)
css_pet_givehat @me all        // every hat
css_pet_shiny @me rainbow      // make it a rainbow shiny
css_pet_bags @me 5             // feed bags
css_pet_eggs @me 3             // eggs in your basket
```

Then type **`!pet`** and explore the menu. To test alone, set `"BotsGetPets": true` and add a few bots: each one gets
a random pet, some shiny and some wearing hats.

---

## The menus

`!pet` opens the main menu. Menus work like the SWBS Jailbreak menus:

| Key | Does |
|---|---|
| **W / S** | Move the cursor |
| **E** | Select |
| **R** | Back (or exit) |
| **!1 – !6** | Pick an item directly |
| **!7 / !8 / !9** | Back or previous page / next page / exit |

Info lines are grey and can't be selected. Locked items show how to unlock them. Menus close after 40 seconds without
input, and you can set them to show in chat instead (`Menu.Style`).

```
Chicken Coop
 Mabel · Pullet · Golden Silkie (Leopard)
 Growth ██████░░░░░░ 52% · Food ████░░
 ► 1. Feed (3 bags)
   2. Commands ▸          sit / come, perch, trick, leave at home
   3. Emotes ▸            14 emotes, locked ones say how to unlock
   4. Wardrobe ▸          Hats ▸ · Looks ▸
   5. Egg basket (2) ▸    gift to a friend, trade for feed
   6. Rename              type the new name in chat
   7. Pet info ▸          stage, growth, food, bags, hats
   8. Journal ▸           stats + past pets
   9. Leaderboards ▸      6 boards
  10. Settings ▸          other pets / name tags / bring my pet
  11. Help ▸
  12. Admin ▸             (admins only)
 7.Back 8.Next 9.Exit | W/S move · E select · R back
```

The menu shows six items per page; items 7–12 are on the second page.

---

## Player guide

### The life of a pet

| Stage | Default length (minutes of play) | What happens |
|---|---|---|
| **No pet** | 5 | You find a **Chicken Egg**, or a friend gives you one. |
| **Egg** | 30 | It waits in a straw nest next to your spawn each round, rocking and tapping more as hatching gets close. |
| **Chick** | 120 | A yellow chick follows you everywhere. You get starter food and a random name. |
| **Pullet** | 240 | Its **breed, coat and shine** are revealed and announced. Small enough to perch on your shoulder. |
| **Hen** | forever (by default) | Full-grown and **lays eggs**. Servers can make hens retire, and then a new egg turns up. |

- **Time is minutes of play** on a team (T or CT), not real time.
- **From the chick stage, growth needs food.**

### Breeds, coats and shiny pets

The breed, coat and shine are rolled when you get the egg and kept secret until the pullet stage. The default odds are
**Catalana 50%, Polish 30%, Silkie 20%**.

| Breed | Coats |
|---|---|
| **Catalana** (classic) | Black · Black White Head · Black with Red Head · Blue · Blue and Light Orange · Dark Orange · Grey · Light Blue and Red · Orange Blue Neck · Red Yellow Gradient · Tan · White · White Tan Head · Yellow Red Gradient · Yellow and Red |
| **Polish** (crest) | Black with White Head · Blue · Blue Yellow · Brown · Brown with Black Wing Tips · Green · Green with Red Head · Orange White Blue · Orange with Black Stripes · Pink · White Black Head · White with Black Wing Tips |
| **Silkie** (fluffy) | Aqua with Yellow · Black · Blue with Red Beard · Brown · Brown with Black Head · Leopard · Orange · Purple · Red · Red Beetle · Splash · Tiger · White |

**Shiny pets.** About **1 in 50** eggs (`Shiny.Chance`) holds a rare shiny chicken:

| Shiny | Look |
|---|---|
| **Golden** | Gold-tinted, with a sparkle now and then |
| **Rainbow** | Slowly cycles through pastel colours |
| **Shadow** | Dark and mysterious |
| **Celestial** | Pale starlight blue, with a sparkle now and then |

When a shiny is revealed it bursts with confetti and the server is told. Its name tag turns gold with ✦ marks. You can
switch back to its natural colours in **Wardrobe › Looks** at any time.

### Food and feed bags

- A hatched pet eats while you play. A new chick starts with **120 minutes** of food, and each **feed bag** adds
  **90 minutes**, up to **360**.
- **Getting bags:** you start with **2**, earn **1 every 4 rounds**, can **trade eggs** for more, and may get them as
  rewards from other plugins.
- **When food runs low** your pet clucks for food and acts hungry. With **no food it stops growing**. By default that's
  all: it never dies or leaves.
- **Feeding** scatters seed, and the pet stops to peck.

### Eggs: laying, gifting, trading, stealing

- **Laying:** a well-fed **hen lays an egg every 40 minutes of play.** It drops beneath her; walk over it to put it in
  your **basket** (up to 6).
  - If your hen isn't out, the egg goes straight into the basket.
  - Eggs left lying around are collected for you at the end of the round.
- **Gifting:** in **Egg basket › Gift an egg**, choose a friend who has **no pet**. Your egg becomes **their** new
  pet's egg. This is a nice way to bring new players in.
- **Trading:** swap eggs for feed bags (1 egg = 1 bag by default).
- **Stealing** (`Eggs.Pickup = "anyone"`): anyone can grab a laid egg, enemies included. The owner is told who did it.

### Commands, petting and the shoulder perch

- **Pet it:** look at your chicken from close up and press **E**. It clucks, sits happily, and you see a ♥.
- **Sit / Come:** make it wait where it is, or call it back.
- **Perch:** chicks and pullets hop onto your **shoulder** and ride along. You don't see it yourself, because it would
  block your view, but everyone else does. Hens are too big. If you die, it jumps down where you fell.
- **Leave at home / Bring along:** keep it out of rounds whenever you like.
- **Lost pets** come back to you in a puff of feathers. **Only you** can lead your pet.

### Emotes

| Emote | Unlock |
|---|---|
| Sit · Nap · Peck · Panic · Flap · Spin | from the start |
| Confetti | hatch your egg |
| Bubbles | grow into a pullet |
| Sparkle | do 10 tricks or emotes |
| Fire | lose your pet to fire once |
| Lightning | lose your pet to a Zeus once |
| Kaboom | lose your pet to the bomb once |
| Lasers | avenge your pet once |
| Abduction (UFO) | unlock the Alien hat |

The effects are the ones Valve made for the pet photo booth. Use them from **Emotes** or with `!pet emote <name>`.

### Hats and looks

The ten photo-booth hats, plus a seasonal one:

| Hat | How to unlock it |
|---|---|
| Sprout | Hatch your egg |
| Party Hat | Feed your pet 5 times |
| Glasses | Pet it 25 times |
| Helmet | Grow into a pullet |
| Armor Helmet | Play 50 rounds together |
| Nose Glasses | Do 30 tricks or emotes |
| Banana | Get 50 kills with your pet out |
| Top Hat | Grow into a hen |
| Wizard Hat | Avenge your pet 3 times |
| Alien | Lose your pet to fire, a Zeus **and** the bomb |
| Snowman Hat | Play during the Christmas event (yours to keep) |

**Looks:**
- **Natural**
- your **shiny** colours, if your pet is shiny
- **Ghost**, a see-through look, unlocked by playing at Halloween
- **Frost**, an icy blue look, unlocked by playing at Christmas

### When your pet dies

- **Who can hurt it:** by default only the enemy team (`WhoCanHurtPets`). Owners can never hurt their own pet.
- **You're told how it died:** "took a Zeus to the beak", "sitting on the bomb", "too close to the fire", and so on.
- **Revenge:** kill the killer in the same round and you get a **Revenge!** message. Revenges count toward the Lasers
  emote and the Wizard Hat.
- **It comes back next round,** or after `RespawnSeconds` if set.
- **When you die,** your pet panics, then sits beside you.
- **Health:** chicks have 20 HP, pullets 35 and hens 50.

### Journal, leaderboards and settings

- **Journal:**
  - your pet's age;
  - rounds, meals and pets;
  - tricks, emotes and perches;
  - eggs laid, gifted and traded;
  - deaths by cause;
  - revenges and kills;
  - and every **past pet** (retired or reset).
- **Leaderboards:** oldest pets · most rounds together · most revenges · most tricks and emotes · most loved · most
  eggs laid. The top 10 show; you're highlighted, and first place is gold.
- **Settings** (saved per player):
  - show other players' pets;
  - show name tags;
  - bring my pet.

  Hiding other pets or tags only changes what **you** see.

### Seasonal events

By default these switch on by date, using server time (`Seasons`):

| Event | Dates | What happens |
|---|---|---|
| 🎃 **Halloween** | 15 Oct – 5 Nov | Pets rise from the ground with zombie dirt, deaths crumble in Halloween style, name tags turn orange, and everyone who plays unlocks the **Ghost** look for good |
| ❄ **Christmas** | 10 Dec – 6 Jan | Pets arrive in a puff of snow, name tags turn icy blue, and everyone who plays unlocks the **Frost** look and the **Snowman Hat** for good |

---

## Commands

In chat use `!` (or `/` to hide the message). In the console use `css_`.

### Players

| Command | Also | What it does |
|---|---|---|
| `!pet` | `!chicken`, `!coop` | Main menu |
| `!pet info` | `status` | Status in chat |
| `!pet feed` | `eat` | Feed (uses a bag) |
| `!pet trick` | `jump`, `flap` | Hop and flap |
| `!pet emote [name]` | `emotes` | Emote menu, or play one emote |
| `!pet sit` / `!pet come` | `stay` / `follow`, `heel` | Wait here / come back |
| `!pet perch` | `shoulder` | Hop on or off your shoulder |
| `!pet name [name]` | `rename` | Rename (with no name, it asks you in chat) |
| `!pet hat [name\|off\|list]` | `hats` | Hat menu, or put on a hat |
| `!pet looks` | `style` | Looks menu |
| `!pet eggs` | `basket` | Egg basket |
| `!pet gift [player]` | | Give a basket egg |
| `!pet trade [n]` | | Trade eggs for feed bags |
| `!pet hide` / `!pet show` | | Leave at home / bring along |
| `!pet journal` | `book` | Journal |
| `!pet top` | `leaderboards` | Leaderboards |
| `!pet settings` | `options` | Settings |
| `!pet arena` | | Arena menu: your record, challenge, accept, bet |
| `!pet duel [player]` | `fight`, `challenge` | Challenge a player's pet to a duel |
| `!pet accept` / `!pet decline` | | Answer a challenge |
| `!pet bet` | | Bet feed bags on a duel that's about to start |
| `!pet mood` | | How your pet feels, and what cheers it up |
| `!pet ability [name\|none]` | `abilities` | Abilities menu, or pick one |
| `!pet collection` | `dex` | Your collection of coats and shinies |
| `!pet help` | | Shortcuts in chat |
| `!pets` | | Everyone's pets on the server |
| **E** on your pet | | Pet it |

### Admins

By default these need `@css/generic` (`AdminPermissions`).

| Command | What it does |
|---|---|
| `css_pet_admin` | Admin menu: per-player tools, pause all pets, hat placement mode, save |
| `css_pet_give <target> <egg\|chick\|pullet\|hen>` | Give a pet at a stage |
| `css_pet_reset <target>` | Remove a pet (it stays in the player's journal) |
| `css_pet_bags <target> <amount>` | Give feed bags (a negative amount takes them) |
| `css_pet_eggs <target> <amount>` | Put eggs in a player's basket |
| `css_pet_givehat <target> <hat\|all>` | Unlock a hat or every hat |
| `css_pet_breed <target> <catalana\|polish\|silkie> [coat #]` | Set breed and coat (coat numbers start at 1, as in the tables above) |
| `css_pet_shiny <target> <golden\|rainbow\|shadow\|celestial\|off>` | Make a pet shiny, or not |
| `css_pet_hatmode [bonemerge\|attachment\|offset]` | Change how hats are attached, live, on every pet (leave it out to cycle) |
| `css_pet_skin <index\|group>` | Testing: show a skin number on your own pet, to check coat colours |
| `css_pet_toggle` | Turn pets off or on server-wide |
| `css_pet_save` | Save now |
| `css_pet_inspect <target\|steamid64>` | Everything about a pet (works offline) |
| `css_pet_restore <target\|steamid64> <n>` | Bring back a past pet from the journal |
| `css_pet_backup <target\|steamid64> [list\|bak\|date]` | Restore a player from a backup |
| `css_pet_rename <target\|steamid64> <name>` | Rename a pet |
| `css_pet_mood <target> <0-100>` | Set a pet's happiness |
| `css_pet_log [lines] [transfers]` | Recent admin actions, or gifts, wagers and bets |

**Targets:** a name (or part of one), `#userid`, a SteamID64, or `@me`.

**Hat ids:** `sprout` `party` `glasses` `helmet` `armor_helmet` `nose_glasses` `banana` `top_hat` `wizard_hat` `alien`
`snowman`.

**Emote ids:** `sit` `sleep` `peck` `panic` `flap` `spin` `confetti` `bubbles` `sparks` `fire` `lightning` `explosion`
`lasers` `ufo`.

---

## Configuration

The config is `configs/plugins/ChickenCoop/ChickenCoop.json`. Every setting and default is in
[`ChickenCoop.example.json`](ChickenCoop.example.json).

### Example setups

**Pets as a VIP perk:**
```json
"OwnerPermissions": ["@css/vip"]
```

**Fast and fun (pet in minutes, no hunger, everything unlocked, more shinies):**
```json
"Growth": { "MinutesToFindEgg": 0, "EggMinutes": 2, "ChickMinutes": 10, "PulletMinutes": 20 },
"Food":   { "Enabled": false },
"Looks":  { "AllHatsUnlocked": true },
"Shiny":  { "Chance": 0.2 }
```

**Hardcore (anyone can shoot pets, eggs can be stolen, pets starve, hens retire):**
```json
"Combat":    { "WhoCanHurtPets": "everyone", "AnnounceDeaths": true },
"Eggs":      { "Pickup": "anyone" },
"Food":      { "StarveRetireMinutes": 120 },
"Growth":    { "HenRetireMinutes": 600 },
"BirdWatch": { "Enabled": true }
```

**Peaceful (pets can't be hurt):**
```json
"Combat": { "WhoCanHurtPets": "nobody" }
```

**Chat menus, for servers running another plugin that draws centre-screen menus:**
```json
"Menu": { "Style": "Chat" }
```

### Settings reference

**General**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turns the plugin on or off |
| `ChatPrefix` | `{gold}[Coop]{default}` | Chat prefix; colour tags allowed |
| `Language` | `en` | Fallback language for players whose CS2 language has no file |
| `OwnerPermissions` | `[]` | Flags needed to own a pet; empty means everyone |
| `AdminPermissions` | `["@css/generic"]` | Flags for the admin menu and commands |
| `DisabledMaps` | `[]` | Maps where pets stay home; `*` works as a prefix wildcard |
| `HideDuringWarmup` | `false` | Keeps pets home during warmup |
| `MaxActivePets` | `24` | Most pets out at once (1–64) |
| `BotsGetPets` | `false` | Gives bots random, unsaved pets (for testing) |

**Menu**

| Setting | Default | What it does |
|---|---|---|
| `Style` | `Center` | `Center`, `Chat` or `Both` |
| `Wasd` | `true` | W/S/E/R navigation in centre menus |
| `TimeoutSeconds` | `40` | Closes idle menus after this long (0 = never) |
| `FixFlashing` | `true` | Stops the centre panel flashing while a menu is open |

**Growth**

| Setting | Default | What it does |
|---|---|---|
| `MinutesToFindEgg` | `5` | Minutes of play before a new player finds an egg |
| `EggMinutes` / `ChickMinutes` / `PulletMinutes` | `30` / `120` / `240` | Length of each stage |
| `HenRetireMinutes` | `0` | When hens retire (0 = never) |
| `BreedWeights` | `Catalana 50, Polish 30, Silkie 20` | Odds for each breed |

**Food**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Set to `false` and pets never need feeding |
| `StarterFoodMinutes` / `MinutesPerBag` / `MaxFoodMinutes` | `120` / `90` / `360` | Food amounts |
| `StarterBags` / `RoundsPerBag` / `BagsPerRoundWin` / `MaxBags` | `2` / `4` / `0` / `10` | Feed bags |
| `HungryWarningMinutes` | `20` | Hungry warning below this |
| `StarveRetireMinutes` | `0` | An unfed pet leaves after this long (0 = never) |

**Eggs**

| Setting | Default | What it does |
|---|---|---|
| `Laying` / `LayingStages` / `MinutesPerEgg` | `true` / `["Hen"]` / `40` | Egg laying |
| `MaxBasket` | `6` | Basket size |
| `Pickup` | `owner` | `owner`, or `anyone` (stealing allowed) |
| `Gifting` | `true` | Allow gifting eggs to players without a pet |
| `BagsPerEgg` | `1` | Feed bags per traded egg (0 = no trading) |
| `GiftCooldownMinutes` / `MaxGiftsPerDay` / `GiftMinPlayMinutes` | `10` / `3` / `20` | Gift limits (against alt accounts) |

**Behaviour**

| Setting | Default | What it does |
|---|---|---|
| `TeleportDistance` / `StuckSeconds` | `650` / `4` | When a lost pet is teleported back |
| `PetDistance` / `PetCooldownSeconds` | `110` / `2` | Petting with E |
| `EmoteCooldownSeconds` / `FeedCooldownSeconds` | `4` / `8` | Cooldowns |
| `StayWithBodySeconds` | `0` | How long a pet stays by its dead owner (0 = all round) |
| `OwnerOnlyFollow` | `true` | Others can't lead your pet away |
| `IdleSoundSeconds` | `25` | Rough gap between idle clucks |
| `NoBlock` | `true` | Pets don't block players |

**Perch**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` / `Stages` | `true` / `["Chick","Pullet"]` | Which stages can ride on your shoulder |
| `Up` / `Right` / `Forward` / `Scale` | `57` / `9` / `-2` / `0.75` | Shoulder position and size |
| `Attachment` | `""` | Optional player-model attachment to perch on |
| `HideFromOwner` | `true` | The owner doesn't see their own perched pet |

**Combat**

| Setting | Default | What it does |
|---|---|---|
| `WhoCanHurtPets` | `enemies` | `everyone`, `enemies` or `nobody` |
| `ChickHealth` / `PulletHealth` / `HenHealth` | `20` / `35` / `50` | Health at each stage |
| `RespawnSeconds` | `0` | 0 = back next round; above 0 = back after that many seconds |
| `AnnounceDeaths` / `Revenge` / `DeathParticles` | `false` / `true` / `true` | Messages and effects |

**BirdWatch**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `false` | Pets panic when an enemy is near (this gives information away) |
| `Range` / `CooldownSeconds` / `WarnOwner` | `600` / `12` / `true` | Bird watch tuning |

**Looks**

| Setting | Default | What it does |
|---|---|---|
| `NameTags` / `NameTagFormat` / `NameTagFontSize` / `NameTagColor` | on / `{owner}'s {pet}` / `40` / `255,236,170` | Name tag |
| `ChickScale` / `PulletScale` / `HenScale` | `1` / `0.78` / `1` | Model size per stage |
| `ShowEggAtSpawn` | `true` | Shows the egg in its nest at your spawn |
| `AllHatsUnlocked` | `false` | Every hat and emote unlocked (seasonal ones still need the event) |
| `HatMode` | `bonemerge` | `bonemerge`, `attachment` or `offset`; see [hats](#hats-dont-show-up-or-sit-in-the-wrong-place) |
| `HatAttachment` | `hat` | Attachment name used by the `attachment` mode |
| `ChickHatHeight` / `PulletHatHeight` / `HenHatHeight` / `HatForward` / `HatScale` | `13` / `19` / `24` / `4` / `1` | Placement for the `offset` mode, and hat size |
| `CoatMode` / `CoatOverrides` | `Skin` / `{}` | Coat switching, and fixes for coats showing the wrong colour |
| `HatTweaks` | `{}` | Per look (`Chick`, `Catalana`, `Polish`, `Silkie`): `Lift` (offset mode) and `Scale` (every mode) |

**Shiny**

| Setting | Default | What it does |
|---|---|---|
| `Chance` | `0.02` | Chance an egg is shiny (0.02 = 1 in 50) |
| `Variants` | all four | Which shiny variants can roll |
| `Announce` | `true` | Tell the server when a shiny is revealed |

**Seasons**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Seasonal events on or off |
| `HalloweenStart` / `HalloweenEnd` | `10-15` / `11-05` | Halloween dates (MM-dd) |
| `ChristmasStart` / `ChristmasEnd` | `12-10` / `01-06` | Christmas dates (MM-dd) |

**Naming**

| Setting | Default | What it does |
|---|---|---|
| `MaxLength` / `RenamesPerStage` | `20` / `1` | Name rules |
| `BlockedWords` | `[]` | Words a name may not contain |
| `DefaultNames` | 20 names | Names picked for newly hatched pets |

**Arena** (1.3.0 addition)

| Setting | Default | What it does |
|---|---|---|
| `MinPlayMinutesForStakes` | `20` | Minutes of play before a player can wager or bet feed bags |

**Moods**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` / `Start` | `true` / `70` | Moods on or off, and a new pet's happiness |
| `DecayPerHour` / `StarvingDecayPerHour` | `8` / `40` | Happiness lost per hour of play, and while hungry |
| `PetGain` / `FeedGain` / `EmoteGain` / `PerchGain` / `RoundGain` | `4` / `12` / `2` / `3` / `0.5` | Happiness gained |
| `DeathLoss` | `15` | Happiness lost when the pet is killed |
| `AttentionCooldownSeconds` | `45` | Petting, emotes and perching count once per this long each |
| `EcstaticGrowthBonus` | `0.1` | Extra growth speed when over the moon |
| `SulkingRefuses` / `SpontaneousTricks` / `MoodInNameTag` | on / on / off | Mood behaviour and display |

**Abilities**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Abilities on or off |
| `Allowed` | `cheer, forager, broody, charmer` | Abilities players may pick; add `sentry` and `lookout` to allow them |
| `ForagerChance` / `BroodyLayMultiplier` / `CharmerDecayMultiplier` | `0.2` / `1.3` / `0.5` | Ability strength |
| `WarnRange` / `WarnCooldownSeconds` | `500` / `10` | Sentry and lookout warnings |

**Collections**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Collection on or off |
| `BagsPerNewEntry` / `BreedCompleteBags` | `1` / `5` | Feed-bag rewards |
| `Announce` | `true` | Tell the server when someone completes a breed |

**Storage**

| Setting | Default | What it does |
|---|---|---|
| `KeepDailyBackups` | `7` | Daily copies in `data/backups` (0 = none) |
| `AdminLog` / `TransferLog` | on / on | `data/admin.log` and `data/transfers.log` |
| `KeepJsonCopy` | `true` | With the MySQL module, keep writing `pets.json` as a local copy |

**Performance**

| Setting | Default | What it does |
|---|---|---|
| `TagDrawDistance` | `1500` | Name tags farther away aren't sent (0 = always) |
| `MaxVisiblePets` | `0` | Each player sees only their closest N other pets (0 = no limit) |

**MapRules**: see [Map and mode rules](#map-and-mode-rules).

---

## Languages

Messages and menus come in **English, Spanish, Portuguese, Russian, German and Polish**.

- **Each player sees their own CS2 language.** It's read from CounterStrikeSharp's player language (`!lang`).
- **Players whose language has no file** get the server's `Language` setting, then English.

To add a language:
1. Copy `plugins/ChickenCoop/lang/en.json` to, for example, `fr.json`.
2. Translate the text. Keep `{0}`, `{1}` and so on, and colour tags like `{green}`, as they are.
3. Reload the plugin.

Any message you leave out falls back to English.

The translations were written alongside the plugin. Native speakers are welcome to improve them.

---

## Plugin API (for other plugins)

Other CounterStrikeSharp plugins can read pets and give rewards. Reference
`shared/ChickenCoop.Shared/ChickenCoop.Shared.dll` with `<Private>false</Private>`; don't ship a copy of it.

```csharp
using ChickenCoop.Shared;

var coop = IChickenCoopApi.Capability.Get();      // null when Chicken Coop isn't loaded
if (coop != null)
{
    coop.GiveFeedBags(player, 1);                 // reward a round win
    coop.GiveBasketEggs(player, 1);               // or an egg
    coop.UnlockHat(player, "wizard_hat");         // or a hat
    coop.SuppressPet(player, true);               // keep the pet out of a duel / special round
    coop.SuppressAll(false);                      // release everyone
    coop.StageChanged += (owner, stage) => { };   // 1 egg, 2 chick, 3 pullet, 4 hen
    coop.PetKilled += (owner, killer) => { };
}
```

| Member | What it does |
|---|---|
| `GetStage`, `GetPetName`, `GetBreed`, `GetShiny`, `IsPetOut`, `GetFeedBags` | Read a player's pet |
| `GetMood`, `GetHappiness`, `GetAbility`, `GetCollection` | Read mood, ability and collection (1.3.0) |
| `GiveFeedBags`, `GiveBasketEggs`, `GiveEgg`, `UnlockHat`, `AddHappiness` | Give rewards (the player is told in chat) |
| `SuppressPet`, `SuppressAll` | Keep pets out of the world for a while, without changing the player's settings |
| `StageChanged`, `PetKilled` | Events |
| `MoodChanged`, `DuelFinished`, `EggGifted`, `CollectionAdded` | Events (1.3.0) |

**Storage modules** register `IChickenCoopStorage` (capability `chickencoop:storage`). It loads every player's JSON
once and saves the ones that changed, on a background thread. The MySQL module is the reference implementation.

**Example for a jailbreak server:** suppress both players' pets when a last request starts, release them when it ends,
and give a feed bag to the winner.

---

## Saving and data

- **Where:** `plugins/ChickenCoop/data/pets.json`, one entry per player, keyed by SteamID64. It stores the pet,
  settings, stats, unlocks and past pets.
- **When:**
  - every minute when something changed, and at the end of every round;
  - on disconnect, on map change and on unload;
  - on `css_pet_save`.
- **Cheap:** only players whose data changed are serialized again, and the file is written on a background thread.
- **Crash-safe:** each save goes to a temporary file first, and the previous version is kept as `pets.json.bak`. A
  damaged file is replaced by the backup automatically.
- **Daily backups:** `data/backups/pets-yyyy-MM-dd.json`, the last 7 by default. Restore one player with
  `css_pet_backup`.
- **Logs:**
  - `data/admin.log`: admin actions;
  - `data/transfers.log`: gifts, wagers and bets.
  - Each rolls over to `.1` at 5 MB.
- **Wiping everyone's pets:** stop the server and delete `pets.json`, `pets.json.bak` and `data/backups`.
- **Bots** never save anything.

### MySQL module

For a network that shares pets across servers, or for anyone who prefers a database, install the optional
**ChickenCoop.MySql** module next to ChickenCoop 1.3.0.

1. **Unzip** [`ChickenCoop.MySql-1.0.0.zip`](releases/ChickenCoop.MySql-1.0.0.zip) into `game/csgo/`.
2. **Start the server once.** That creates `configs/plugins/ChickenCoop.MySql/ChickenCoop.MySql.json`. Fill in `Host`,
   `Port`, `Database`, `User`, `Password`, `Table` and `SslMode`
   (see [`ChickenCoop.MySql.example.json`](mysql/ChickenCoop.MySql.example.json)).
3. **Restart the server.** On the first run every player in `pets.json` is uploaded. After that:
   - the database is the main copy;
   - `pets.json` stays as a local copy (`Storage.KeepJsonCopy`);
   - a player's record with more play time wins if the two ever disagree, for example after the database was down.
4. **Check the log.** The server log says `using MySQL (...)` when it's working. `css_pet_admin` shows the storage in
  use.

**Database details:**
- **The table:** one table, `chickencoop_pets`, created automatically. Each row is a SteamID64, the pet as JSON, and
  when it changed.
- **Speed:** saves only send changed players, in one transaction, on a background thread.
- **If the database is down:** the plugin keeps going on `pets.json` and retries the upload with the next save.
- **Sharing between servers:** several servers can share the table, but a player should only be on one of them at a
  time (the last save wins).

---

## How it works

- **The pet is CS2's own `chicken` entity.** It's spawned with the `chicken_model` setting set to the chick, Catalana,
  Polish or Silkie model. The plugin also sets:
  - health and size for the stage;
  - the coat, by skin number;
  - the owner (`m_owner`), which makes the game draw its own "Pet Chicken" label.
- **Following uses the chicken's built-in leader (`m_leader`).** The game's chicken AI does all the walking and
  animating.
- **Five times a second** the plugin:
  - points the chicken back at its owner (this is what stops other players stealing it);
  - teleports it back if it's lost or stuck;
  - plays idle and hunger clucks;
  - runs bird watch and the shiny sparkle and rainbow colours;
  - checks whether anyone is standing on a laid egg.
- **Behaviours** use the chicken's pet activities: Feed, Sleep, Squat, Panic, Trick, Glide and LowOnFood.
- **Hats** are photo-booth props. By default they're **bone-merged** onto the chicken's skeleton, which is how one hat
  model fits every size of chicken. The `attachment` and `offset` modes are fallbacks.
- **Name tags** are `point_worldtext` entities that turn to face whoever is looking at them.
- **The perch** is a copy of the pet model, attached to the owner's player model while the real chicken is removed.
- **Per-player visibility** (hiding other pets or tags, and hiding your own perched pet) uses CounterStrikeSharp's
  `CheckTransmit` with cached entity lists.
- **The rules** (growth, food, eggs, shiny, unlocks, seasons, names, leaderboards, moods, abilities, collections, map
  rules, gift limits) live in engine-free code (`PetLogic`). The menu paging and the save system are engine-free too.
  All of it is covered by 102 unit tests, plus 4 MySQL tests that run against a real database.
- **Menus** are a port of the SWBS Jailbreak menu system. They're drawn in the centre of the screen, registered with
  CounterStrikeSharp so `!1`–`!9` work, and use the same panel-steadying fix.

---

## Game assets used

Everything ships with CS2. The plugin only refers to these files by path.

| Kind | Files |
|---|---|
| Pets | `models/chicken/chick.vmdl`, `chicken.vmdl`, `chicken_polish.vmdl`, `chicken_silkie.vmdl` |
| Egg and nest | `models/chicken/egg_pristine.vmdl`, `chicknegg.vmdl`, `models/nest/nest.vmdl` |
| Feed | `models/chicken_feed/chicken_feed.vmdl` |
| Hats | `models/photobooth/silly_hats/*.vmdl` (10), `models/props/cs_office/snowman_hat.vmdl` |
| Effects | `particles/pet_photo/*` (confetti, bubbles, sparks, fire, lightning, explosion, lasers, ufo, feathers), `particles/critters/chicken/*` |
| Sounds | `Chicken.Egg.Hatch*`, `Chicken.Evolve.To.*`, `Chicken.Feed*`, `Chicken.Hungry*`, `Chicken.Idle.*`, `Chicken.Interact`, `Chicken.Flap.*`, `Chicken.Panic*`, `Chicken.Peck*`, `Chicken.Scratch*` |

---

## Troubleshooting and FAQ

### Hats don't show up, or sit in the wrong place

1. **Change map once after installing.** The photo-booth hats are normally only used in the main menu, so a server
   only loads them when the plugin asks during map load. If the plugin was loaded mid-map, hats (and maybe pet models)
   are invisible until the next map. Admins get a reminder in chat.
2. **Try the other placement modes live:** `css_pet_hatmode` cycles **bonemerge › attachment › offset** and re-dresses
   every pet straight away. Keep whichever looks right, then set `Looks.HatMode` in the config.
   - **bonemerge** (the default): the hat follows the chicken's head bones at every size.
   - **attachment**: the hat snaps to the model attachment named in `HatAttachment`.
   - **offset**: the hat sits at a fixed height above the body. Tune `ChickHatHeight`, `PulletHatHeight`,
     `HenHatHeight`, `HatForward` and `HatScale`.
3. **Check the hat is unlocked.** Locked hats show as *locked* in the Wardrobe. `css_pet_givehat @me all` unlocks every
   hat for testing.

### Other questions

**Nobody gets a pet.**
- Check `Enabled` and `css_pet_toggle`.
- Check `OwnerPermissions` isn't limiting pets to a flag nobody has.
- Check `DisabledMaps` and `HideDuringWarmup`.
- New players need `MinutesToFindEgg` minutes on a team first.

**A coat shows the wrong colour.**
1. Get a pet of that breed: `css_pet_breed @me silkie`.
2. Step through the skins with `css_pet_skin 0`, `1`, `2`, and so on.
3. Pin the right one: `"CoatOverrides": { "Silkie/Leopard": "5" }`.

**The menu flashes, or fights with another plugin's menu.**
- Keep `Menu.FixFlashing` on.
- If another plugin also draws in the centre of the screen (a jailbreak HUD, for example), set `"Menu": { "Style": "Chat" }`
  or `"Both"`.

**Pets teleport a lot.**
- Raise `TeleportDistance` or `StuckSeconds`. Maps with ladders and drops will always need the odd teleport.

**Does it work with jailbreak or other modes?**
- Yes. For special rounds, use the [plugin API](#plugin-api-for-other-plugins) (`SuppressPet` / `SuppressAll`), or
  `DisabledMaps`.

---

## First-run checklist for 1.3.0

1. **Moods:**
   - `css_pet_mood @me 95`: your pet should do a trick on its own within a minute or two.
   - `css_pet_mood @me 5`: `!pet trick` and `!pet perch` should be refused, and the pet sits down now and then.
2. **Abilities:**
   - `css_pet_give @me hen`, then `!pet ability cheer`, then get a kill: the pet should celebrate.
   - Try `!pet ability forager` and finish a few rounds.
3. **Collection:** `!pet collection` lists your coats. `css_pet_breed @me silkie 3` should add a new entry and a feed
   bag.
4. **Admin tools:**
   - `css_pet_inspect <your steamid64>` works after you disconnect.
   - `css_pet_reset @me`, then `css_pet_restore @me 1`, brings your pet back.
   - `css_pet_backup @me list` shows the backups.
5. **Map rules:** add `{ "Maps": ["de_*"], "Perch": false }`, change map, and `!pet perch` should be refused.
6. **Performance:** with `"TagDrawDistance": 300`, name tags should vanish when you walk away from other pets.
7. **MySQL (optional):** install the module, restart, and check the log line, then `SELECT COUNT(*) FROM chickencoop_pets`.

---

## First-run checklist for 1.1.0

Version 1.0.0's core (spawning, following, labels, name tags) is confirmed working. These 1.1.0 parts touch the engine
in new ways, so check them first:

1. **Menus:** `!pet` opens; W/S, E and R work; `!1`–`!9` work; the panel doesn't flash.
2. **Hats:** after a map change, try each mode with `css_pet_hatmode`.
3. **Perch:** `css_pet_give @me chick`, then `!pet perch`. Your friends should see it on your shoulder. Adjust
   `Perch.Up`, `Right` and `Forward` if needed.
4. **Egg laying:** `css_pet_give @me hen` with `"Eggs": { "MinutesPerEgg": 1 }`. Walk over the egg to pick it up.
5. **Shiny:** `css_pet_shiny @me rainbow` should slowly cycle colours; `golden` should sparkle.
6. **Emotes:** confetti, fire, lightning and so on. The photo-booth effects might be sized for the menu rather than the
   world.
7. **Settings:** turn **Show other players' pets** off; other pets should vanish for you only.
8. **Languages:** set your language with `!lang es` (or another language) and open `!pet`.

Please report anything that looks off. Most of it can be tuned in the config.

---

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./build.sh                                  # tests + build + artifacts/ChickenCoop-<version>.zip (+ the MySQL module zip)
dotnet test tests/ChickenCoop.Tests.csproj  # tests only

# MySQL module tests against a real database (otherwise they pass without one):
COOP_MYSQL="Server=localhost;Database=coop_test;User ID=coop;Password=..." dotnet test mysql/tests/ChickenCoop.MySql.Tests.csproj
```

---

## Project layout

```
ChickenCoop.example.json        every setting with its default
CHANGELOG.md                    version history
build.sh                        test + build + package
releases/                       ready-to-install zips
shared/ChickenCoop.Shared/      the plugin API (IChickenCoopApi) and the storage contract (IChickenCoopStorage)
mysql/                          the optional MySQL / MariaDB storage module, its tests and example config
src/
  ChickenCoopPlugin.cs          entry point, events, growth timer, damage rules, deaths
  Pets.Spawn.cs                 chicken, egg, nest, hats, name tags, looks, perch, laid eggs, effects
  Pets.Behaviour.cs             following, petting, emotes, perch, feeding, gifting, trading, bird watch
  Pets.Transmit.cs              per-player visibility
  Pets.Api.cs                   the API implementation
  Arena.cs                      Coop Arena: challenges, duels, betting
  Menus.cs                      every menu
  Commands.cs                   player and admin commands
  Menus/CoopMenu.cs             menu system (jailbreak style)
  Menus/ChatInput.cs            "type it in chat" prompts
  Config.cs                     all settings
  Logic/PetLogic.cs             rules (engine-free, unit-tested)
  Logic/PetLogic.Care.cs        moods, abilities, collections, map rules, limits, restores (unit-tested)
  Logic/ArenaLogic.cs           arena stats, damage table, bets (unit-tested)
  Logic/PetStore.cs             saving: background writes, daily backups, storage modules
  Model/                        saved data and the asset catalogue
  Runtime/                      messages (per-player language), per-pet entity state, admin and transfer logs
  lang/                         en, es, pt, ru, de, pl
tests/                          102 unit tests (pets, care, arena, menus, saving, languages)
```

---

## Credits

- **ChickenCoop** is created by **Gdaki**.
- **Co-authored with Claude** (Anthropic), using Claude Code: design, code, tests, translations and documentation.
- **The menu system** is ported from the SWBS Jailbreak mod by Gdaki.
- **Chicken models, coats, hats, effects and sounds:** © Valve Corporation, part of Counter-Strike 2. They're loaded
  from the game's own files; nothing is redistributed.
- **Asset paths and engine fields** were found using the public
  [SteamDatabase GameTracking-CS2](https://github.com/SteamDatabase/GameTracking-CS2) file lists and schema dumps.
- **Built on** [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).
