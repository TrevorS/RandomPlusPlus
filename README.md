# RandomPlusPlus

[![Build](https://github.com/TrevorS/RandomPlusPlus/actions/workflows/build.yml/badge.svg)](https://github.com/TrevorS/RandomPlusPlus/actions/workflows/build.yml)

Set a specification for your starting colonists, then hit randomize. It keeps rerolling until a
pawn matches, or until it reaches your reroll limit — without freezing the game while it searches.

Filter on skill levels and passions, total skill points, traits (required, excluded, or "any N of
these"), age, gender, health conditions and work capability.

![The Random Editor open over the Create characters page, with a 50,000-reroll search running behind it](./docs/random-editor.webp)

RimWorld 1.6 and 1.5 &middot;
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3774235680) &middot;
requires [Harmony](https://github.com/pardeike/HarmonyRimWorld)

A fork of [mastertea/RandomPlus](https://github.com/mastertea/RandomPlus)
([Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=1434137894)) that fixes several
filter bugs (gender, skills) and rewrites the reroll search so it runs in small time slices instead
of hanging the game. `changelog.txt` has the details. Saved filter presets from RandomPlus carry
over.

## Related mods

Other forks of RandomPlus you may want to compare:

- [RandomPlusPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=3742532944) by hsariaslan
  ([GitHub](https://github.com/hsariaslan/RandomPlusPlus))
- [FasterRandomPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=3510981573) by ppuya13
  ([GitHub](https://github.com/ppuya13/FasterRandomPlus))

## Building

```sh
make build     # needs the .NET SDK 8.0+, no RimWorld install required
make install   # copy into RimWorld's Mods folder
```

Developer documentation — project layout, how the search works, tests, releasing — lives in
[CLAUDE.md](./CLAUDE.md).

## Credits

Forked from [RandomPlus](https://github.com/mastertea/RandomPlus) by __mastertea__. UI code and
assets originally forked from
[EdBPrepareCarefully](https://github.com/edbmods/EdBPrepareCarefully) by __edbmods__.
