# Against The Storm Camps Upgrade Mod

In the normal game only the Woodcutters Camp can be upgraded, and only after you get the camp upgrades effect. This mod lets you upgrade the Woodcutters Camp right away, and gives the other gatherer camps and the fishing huts the same upgrades. Each building can be turned on or off in the config file.

The compiled mod is meant to be loaded into the game with [BepInEx](https://github.com/BepInEx/BepInEx). I tested with version 5.4.23.

## Which buildings get upgrades

By default all of these can be upgraded:

- Woodcutters Camp
- Forager's Camp (and Primitive Forager's Camp)
- Herbalist's Camp (and Primitive Herbalist's Camp)
- Trapper's Camp (and Primitive Trapper's Camp)
- Harvester Camp
- Stonecutter's Camp
- Fishing Hut (and Primitive Fishing Hut)

## The upgrades

Every building has the same upgrades as the Woodcutters Camp, and they cost the same as the Woodcutters Camp's upgrades.

| Level | Cost | Pick one |
| --- | --- | --- |
| 1 | 2 Packs of Building Materials | **Worker Speed** (+25%) or **Worker Capacity** (+3) |
| 2 | 3 Packs of Building Materials, 10 Coats, 10 Boots | **Extra Production Chance** (+33%) or **Storage Automaton** (1) |

Worker Capacity makes the workers of that building carry more goods per trip from the camp to the main storage, so they make fewer trips. It does not make them produce more.

Not everything has been tested on every building. I confirmed Worker Capacity on the Forager's Camp. The Storage Automaton comes from the Woodcutters Camp, so it may not behave the way you expect on the other buildings. Worker Capacity probably does nothing for fishing huts.

## Settings

The game creates a settings file the first time you run it with the mod: `BepInEx/config/CampsUpgrade.cfg`.

- **`[Buildings]`**: one entry per building. Set it to `false` to leave that building as in the normal game, with no upgrades from this mod. Default is `true`.

Changes only take effect the next time you start the game.

**Note:** changing a setting on an existing save can cause problems for camps you already built. If you change settings, it is safest to start a new settlement.

## Background
This mod started because I played the Tree Grazer event and thought those upgrades were really nice under certain circumstances and if the resources allow it. I have this enabled for the other camps just....for funsies. Probably not practical lol.

## Credits

- Thanks to https://github.com/ats-mods/ModTemplate for the mod template
- Thanks to https://github.com/JoyfulShush/AtS-my-first-mod/ with the really helpful guide
- Thanks to https://github.com/JamesVeug/Josiwe.ATS.Cheats/ for all the really good examples for all the difference things you can do.
- Thanks to Eremite Games for making Against the Storm.
