# Changelog

## 1.3.3

**Fixes**
- **World menus are the default again:** every saved menu style is put back to the server default (the world panel) once. Many players had switched to the classic menu while the panel was not drawing. Anyone who picks Classic after this keeps it.
- **Spectators get world menus too:** the panel floats in front of your view while you watch someone in first person or fly the free camera. Chase view and fixed cameras still use the classic menu. `Menu.World.Spectators` turns this off.
- **The panel opens a moment after the menu (0.1 s):** creating its entities in the same tick as a chat command can stall the client's connection. Set it with `Menu.World.OpenDelay`.
- **No flicker after a failure:** when a panel cannot be drawn, the classic menu stays for 5 seconds before the world panel is tried again.
- **Status:** not yet tested in game.

## 1.3.2

**Fixes**
- **World menus draw:** a just-created panel entity reads as invalid until it has spawned, and that made every world menu fall back to the classic one.
- **No chat copy:** while a world panel is shown, the menu is no longer also printed in chat.
- **Status:** not yet tested in game.

## 1.3.1

**Fixes**
- **World menus are visible:** the panel is moved in front of you every tick instead of being attached to your player. On current CS2 builds the attached panel did not show. `Menu.World.Follow` can be set back to `"parent"`.
- **No crash at round end:** the panel's entities are kept as handles. The round restart removes them, and touching a removed entity crashed the server.
- **The panel no longer trails when you run:** it is placed ahead by your speed times your latency (`Menu.World.MoveLead`).
- **Status:** not yet tested in game.

## 1.3.0

**New: world menus**
- **Menus as a panel in front of you**, drawn with point_worldtext while you are alive:
  - coloured text on a translucent backing plate with an accent bar;
  - numbered rows, a pulsing ► cursor, and right-aligned values (ON/OFF in green and red, bars, counts);
  - page dots and key hints.
- **Follows you:** the panel moves and turns with you and re-centres when you look up or down. Only you can see it.
- **Settings:** menu style (world or classic), theme (9 themes, Coop by default), position (left, centre, right) and
  size (small, normal, large), all saved per player.
- **Config:** the new `Menu.World` section holds the server defaults and the fine-tuning.
- **Fallback:** dead players and spectators get the classic centre menu, which no longer needs the flashing fix
  while a world menu is shown.
- **Languages:** all six updated.
- **Status:** not yet tested in game.

## 1.2.0

**New: Coop Arena**
- **Turn-based pet duels:** `!pet duel`, or the new Arena menu.
- **Moves:** Peck, Guard, Flap and a charged Special, each with a counter.
- **Stats come from the pet's life:** stage, rounds together, meals, tricks and breed.
- **In the ring:** the real chickens act out each move, and the special uses the pet's flashiest unlocked effect.
- **Wagers:** optional feed-bag wagers (the winner takes the pot).
- **Spectator betting:** bettors on the winner split the pool; a draw refunds everyone.
- **Records:** wins, losses and draws in the journal, plus a "Most arena wins" leaderboard.
- **Config:** a new `Arena` section.
- **Languages:** all six updated. 64 unit tests.
- **Status:** not yet tested in game.

## 1.1.0

**New**
- **Menus in the SWBS Jailbreak style.**
  - Drawn in the centre of the screen with a title, a subtitle, progress bars and pages.
  - Controls: W/S move, E select, R back, or `!1`–`!9`. Optional chat style.
  - A fix stops the panel flashing while a menu is open.
  - Submenus: Commands, Emotes, Wardrobe (Hats, Looks), Egg basket, Pet info, Journal (and past pets), Leaderboards,
    Settings, Help and Admin.
- **Rename by typing in chat** from the menu.
- **Shoulder perch** for chicks and pullets (`!pet perch`).
- **Hens lay eggs.** Laid eggs go into a basket and can be gifted to a friend without a pet or traded for feed bags.
  Optionally, anyone can pick them up, so eggs can be stolen.
- **Shiny pets** (Golden, Rainbow, Shadow, Celestial), about 1 in 50, revealed at the pullet stage with confetti and a
  server announcement.
- **14 emotes** using the stock pet photo-booth effects, some unlocked through play.
- **Looks:** natural, shiny, and the seasonal Ghost and Frost looks.
- **Seasonal events:** Halloween and Christmas, with themed effects, name-tag colours and keepsake unlocks (Ghost,
  Frost, Snowman Hat).
- **Leaderboards:** age, rounds, revenges, tricks, pets given and eggs laid.
- **Per-player settings:** show other pets, show name tags, bring my pet.
- **Bird watch** (optional): pets panic when enemies are near.
- **Admin menu**, plus `css_pet_eggs`, `css_pet_shiny` and `css_pet_hatmode`.
- **Translations:** English, Spanish, Portuguese, Russian, German and Polish, with each player's own language picked
  automatically.
- **Plugin API** (`ChickenCoop.Shared.dll`, capability `chickencoop:api`): read pets, give feed bags, eggs and hats,
  keep pets out of special rounds, and stage-change and pet-killed events.

**Fixed**
- **Hats:** photo-booth hats are now bone-merged onto the chicken's skeleton by default. Two fallback modes can be
  switched live with `css_pet_hatmode`.
- **Mid-map loading:** admins are now warned when the plugin was loaded mid-map, which stops hats and models from being
  loaded until the next map change.

**Status:** 1.1.0 compiles and passes 49 unit tests, but hasn't been tested in game yet. The 1.0.0 core is confirmed
working in game.

## 1.0.0

- First release: eggs, chicks, pullets and hens; breeds and coats; following with the stock chicken AI; feeding;
  petting; tricks; names; hats; name tags; deaths and revenge; journal; admin commands; crash-safe JSON saving.
- Confirmed working in game (tested by Gdaki).
