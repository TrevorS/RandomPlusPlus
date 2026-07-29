# Smoke test

Everything in this repository is checked without RimWorld. One thing cannot be: the two transpilers
in `Source/Game/HarmonyPatches.cs` match opcode patterns inside RimWorld method bodies, and
reference assemblies carry no IL. When a pattern stops matching, the injection is skipped **and
nothing is logged** — the Filter button simply is not there.

So this runs before every release, on **both 1.6 and 1.5**, because the patterns are matched against
each game version separately and passing on one says nothing about the other.

## Setup

```sh
make install
```

That builds every supported version and copies the mod into RimWorld's `Mods` folder. Enable
Harmony, then RandomPlusPlus, in that order. Remove the original RandomPlus first — they are
declared incompatible and both replace the same button.

`make uninstall` takes it back out. To test the actual download instead, `make package` and unzip
`dist/RandomPlusPlus-*.zip` into `Mods/` by hand.

For 1.5, switch RimWorld to the `1.5` beta branch in Steam (Properties → Betas).

## The checks

Start a new colony and stop at **Create characters**.

| # | Check | Why it is here |
| --- | --- | --- |
| 1 | A **Filter** button appears next to Randomize | The `DrawCharacterCard` transpiler matched. This is the one that fails silently. |
| 2 | A **Rerolls: 0/1000** label appears | Same transpiler, second injection. |
| 3 | Filter opens the editor, and all three panels draw | `Page_RandomEditor` and the EdB panels. |
| 4 | **Reset All** and **Save/Load** sit at the top right of the editor | Their positions were wrong at non-default UI scale. Repeat at UI scale 2.0 — this is the point of the check. |
| 5 | Set a **gender** filter, close, hit Randomize | Must converge to that gender. This was unsatisfiable before. |
| 6 | Set **age 18–25**, Randomize | Must converge, and the reroll counter must stop below the limit. |
| 7 | Set a **skill minimum** of 8 and a required **trait**, Randomize | Both satisfied on the pawn handed back. |
| 8 | The kept pawn has **apparel and a weapon** | Gear is generated once, after the search, for the pawn kept. |
| 9 | The kept pawn's **work tab** is populated | Work priorities are initialised only for the kept pawn. |
| 10 | Ask for something impossible (all skills ≥ 20), Randomize | Spends the reroll limit, returns a fully finished pawn, does not hang or throw. |
| 11 | **Save** a filter, restart the game, **Load** it | Presets round-trip through `Scribe`. |
| 12 | Check the log | No red errors. `[RandomPlusPlus]` warnings are readable and not repeated per reroll. |

## Migration

Presets live in `Config/RandomPlus.xml` — the same file the original mod uses. That is deliberate:
a player switching from RandomPlus keeps their saved filters. If you have both mods' presets to test,
back that file up first.

## If the Filter button is missing

The transpiler did not match. Compare the IL of `CharacterCardUtility.DrawCharacterCard` in that
game version against the pattern in `Patch_RandomEditButton.Transpiler` — it looks for `Ldloc_1`
followed by `Brfalse`. Nothing off the game can tell you this; only this test can.
