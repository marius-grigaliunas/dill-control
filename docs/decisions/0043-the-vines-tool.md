# 0043 The Vines tool: a main screen in the editor that generates a nest, shows it, plays it and logs the verdict

*24 October 2026. Built; the user played with it and called it good.
Builds on 0033, 0036 and 0038. Changes CLAUDE.md rule 7's second plugin
from a dock to a main screen.*

## Context

The user asked from the start to "generate the nest, see it, press one
button to play it". The Vines dock grew one vine. Judging a nest meant
editing arguments to `lvl_nest_lab` by hand, and nothing kept what was
judged.

A GDExtension or a separate app could have held it; both mean native code
or a second program. An `EditorPlugin` in C# has the game's own classes.

## Decision

**`addons/vine_gen` is a C# `EditorPlugin` with its own main screen, a tab
beside 2D, 3D, Script and AssetLib. The dock stays for one vine.**

- **Nest tab:** kit, seed, population, horde. Generate plans the nest with
  the game's own `NestLayout` and stands it on a flat disc with its
  measures. The planet is not drawn: its classes are placeholders in the
  editor. **Play** writes `user://nest_play.json` (`NestPlay`) and plays
  `lvl_nest_lab`, whose `NestBuilder` takes the request and deletes it;
  the lab writes how play went (`NestPlaytest`) for the tab to show. Play
  writes nothing in the project. Good/Bad with a note, Save (Good only, as
  a `NestPreset`), Saved.
- **Range lab:** N seeds of a kind as they roll, before any re-roll (0036),
  each valid or why not; Good/Bad; each setting's spread over its range;
  ranges edited and saved. A card opens that very roll in the Vine tab.
- **Vine tab:** one vine, its preview, report, overrides, Good/Bad, Bake.
- **The logs are training data.** `tools/vine-logs/nest_verdicts.jsonl`
  and `vine_verdicts.jsonl`, apart, in git. JSON Lines, the first line an
  `"about"` record explaining the file. Bad results are kept. Never edited
  or pruned.
- **It survives a rebuild.** The editor does not carry a tool's C# signal
  handlers over a reload of the project's code, so every button went dead.
  The plugin tears down its panel and dock as the old code unloads and
  builds them from the new on the first frame after. Editor code uses
  Godot's JSON, not System.Text.Json, whose type caches block the unload.
- **Checked in a real editor:** `--editor -- nesttab-check=<png>`,
  `rangelab-check=<prefix>`, `reload-check`. A reload once reset every
  `.vox.import` to the importer's defaults; `reload-check` now copies them
  aside and puts back any that change.

## What it costs

- Editor code must survive reloads: no long-lived handlers into the
  project's assembly, no caches of its types.
- The Nest tab's preview is not the planet the nest is played on.
