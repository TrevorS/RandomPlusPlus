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
| 1 | A **Filter** button appears left of the Randomize panel, not overlapping its highlight | The `DrawCharacterCard` transpiler matched. This is the one that fails silently. |
| 2 | With no filter set there is **no Rerolls label**; set any filter and **Rerolls: 0/N** appears and the Filter button turns **green** | The label only means something while a filter is active, and the green button is the there-is-a-filter indicator. Clear the filter and both revert. |
| 3 | Filter opens the editor, and all three panels draw | `Page_RandomEditor` and the EdB panels. |
| 4 | **Reset All** and **Save/Load** sit at the top right of the editor | Their positions were wrong at non-default UI scale. Repeat at UI scale 2.0 — this is the point of the check. |
| 5 | Set a **gender** filter, close, hit Randomize | Must converge to that gender. This was unsatisfiable before. |
| 6 | Set **age 18–25**, Randomize | Must converge, and the reroll counter must stop below the limit. |
| 7 | Set a **skill minimum** of 8 and a required **trait**, Randomize | Both satisfied on the pawn handed back. |
| 8 | The kept pawn has **apparel and a weapon** | Gear is generated once, after the search, for the pawn kept. |
| 9 | The kept pawn's **work tab** is populated | Work priorities are initialised only for the kept pawn. |
| 10 | Ask for something impossible (all skills ≥ 20), Randomize | Spends the reroll limit, returns a fully finished pawn, does not hang or throw. |
| 11 | **Save** a filter, restart the game, **Load** it | Presets round-trip through `Scribe`. |
| 12 | Set a rare filter (a 2–3% trait, required) with a big reroll limit, Randomize | No beachball, no frozen window. The portrait area shows the search panel: a live reroll count, candidate names ticking at a readable pace, and a **Stop searching** button — not a flickering half-generated pawn. |
| 13 | While that search runs, watch the rest of the page | The pawn's tile in the left list and the **Team skills** panel tick together with the overlay, at the same readable pace — name, title, portrait and skill numbers all showing the sampled candidate, never flickering per frame. Everything comes back live the moment the search ends. |
| 13a | While a search runs: click **Stop searching** | The search stops on the spot and the pawn it leaves behind is finished — gear, work tab — not half-rerolled. |
| 13b | Start another search: click Randomize again, then close the page mid-search | The second click does nothing. Closing the page stops the search and also leaves a finished pawn. When a search ends normally, the card comes back showing the kept pawn. |
| 14 | Check the log | No red errors. `[RandomPlusPlus]` warnings are readable and not repeated per reroll. |

## Migration

Presets live in `Config/RandomPlus.xml` — the same file the original mod uses. That is deliberate:
a player switching from RandomPlus keeps their saved filters. If you have both mods' presets to test,
back that file up first.

## If the Filter button is missing

The transpiler did not match. Compare the IL of `CharacterCardUtility.DrawCharacterCard` in that
game version against the pattern in `Patch_RandomEditButton.Transpiler` — it looks for `Ldloc_1`
followed by `Brfalse`. Nothing off the game can tell you this; only this test can.
