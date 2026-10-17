# Changelog

## 1.3.0

**New gameplay**
- **Moods:**
  - each pet has a happiness (0–100) and a mood, from Sulking to Over the moon;
  - petting, feeding, tricks, perching and rounds together cheer it up; play time, hunger and dying wear it down;
  - happy pets do tricks on their own, and ecstatic ones grow 10% faster;
  - sulking pets sit down now and then, and refuse tricks and perching;
  - `!pet mood`, hearts in the menus, and an optional mood display in the name tag.
- **Abilities:** pets unlock them as they grow, and the owner picks one with `!pet ability`.
  - On by default: Cheerleader, Forager, Broody and Charmer.
  - Off by default, because they reveal enemies: Sentry and Lookout.
- **Collection:**
  - every coat and shiny you raise is recorded, including past pets;
  - feed bags for each new entry, and a bonus for a full breed;
  - `!pet collection`, plus a new "Biggest collections" leaderboard.

**Admins and servers**
- **Admin tools:** `css_pet_inspect`, `css_pet_restore`, `css_pet_backup`, `css_pet_rename`, `css_pet_mood` and
  `css_pet_log`.
  - The first three work for offline players by SteamID64.
  - The admin menu has matching entries.
- **Logs:** `data/admin.log` (admin actions) and `data/transfers.log` (gifts, wagers, bets).
- **`MapRules`:** switch pets, perching, the arena, name tags, abilities and emotes per map or per game mode, and set a
  per-map pet cap.
- **Limits against alt accounts:**
  - gift cooldown, daily gift cap, and a minimum play time to receive a gift;
  - a minimum play time to wager or bet.
- **Faster saves:**
  - only changed players are re-serialized;
  - files are written on a background thread;
  - pets are also saved at the end of every round.
- **Daily backups:** `data/backups/pets-yyyy-MM-dd.json`, the last 7 kept.
- **Storage modules:** a new `IChickenCoopStorage` contract, and an optional **ChickenCoop.MySql 1.0.0** module (MySQL /
  MariaDB). It uploads `pets.json` on its first run and keeps `pets.json` as a local copy.
- **Performance:**
  - name tags aren't sent to players more than 1500 units away (`Performance.TagDrawDistance`);
  - an optional cap on visible pets per player (`Performance.MaxVisiblePets`).
- **`Looks.HatTweaks`:** hat lift and size per look (Chick, Catalana, Polish, Silkie).

**For developers**
- **API:**
  - new methods: `GetMood`, `GetHappiness`, `AddHappiness`, `GetAbility`, `GetCollection`;
  - new events: `MoodChanged`, `DuelFinished`, `EggGifted`, `CollectionAdded`.
- **Tests:** 102 unit tests, up from 64. They now cover menu paging, saving and the new rules. 4 more tests run the
  MySQL module against a real MariaDB.

**Languages:** 88 new messages in all six languages.

**Status:** not yet tested in game.

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
