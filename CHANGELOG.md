# Changelog

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
