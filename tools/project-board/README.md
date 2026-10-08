# Project Board

Dill Control's kanban board. It runs in any browser from `index.html`, with no Godot,
no server, no internet and no dependency of any kind — the whole tool is ten
small scripts and one stylesheet.

Open `index.html`. It comes populated with the vertical slice from
`docs/plan.md`, milestone by milestone, all in one `Vertical slice` category.
`Settings → Delete every card` clears it out if you would rather start empty.

## What it does

**Columns** are yours to define: add, rename, reorder by dragging a header, and
delete. A column can carry a **work-in-progress limit** (its count turns red past
it) and can be flagged as **counting as finished**, which is what feeds milestone
progress and what `Archive finished cards` collects.

**Cards** drag between and within columns. Each one carries a title, Markdown
notes, a type, a priority, a point estimate, any number of labels, a milestone,
an owner and a checklist whose steps drag to reorder. `+ Add card` opens a quick
box where Enter files one and leaves the box open for the next, which is the
mode filling a backlog actually wants; anything needing more than a title is one
click further into the card dialog.

**Nine card types**, because an art task and a perf task are not the same work:
feature, bug, perf, art, audio, design, tech, tooling, polish. Each one colours
its card's left edge, so a column's make-up reads without reading it.

**Filtering** is by text, type, label, milestone and priority floor, all at once,
with the search reaching titles, notes, labels, checklist steps and `#numbers`.

**Swimlanes** group the whole board by milestone or by type. Dropping a card into
a lane genuinely puts it on that milestone or changes its type — a lane you can
drop into but that does not mean anything is worse than no lane at all.

**Undo covers everything**, drags and deletions included, sixty steps deep, with
redo behind Ctrl Shift Z. Deleting a column never deletes its cards; they move
to the column on its left.

## Where the board lives

Two places, and the tool says which in its top bar rather than leaving it to be
guessed.

Always in **this browser's storage**, written synchronously on every change.
That survives a reload and a crash and is not in the repo.

Also, once you press **Connect project folder**, in
**`docs/project_board.json`** — written three quarters of a second after each
change through the File System Access API, exactly as `tools/voxel-bench` writes
`.tres` files. That copy is in the repo and in git, which is the point: a board
held only in a browser profile is gone with the profile. The folder handle is
remembered in IndexedDB, so later sessions need one click on **Reconnect**
rather than another trip through the picker.

On startup, when the two differ — someone hand-edited the file, or a `git pull`
brought a newer one — it says so and asks which to keep. When they agree, which
is the ordinary case, it says nothing.

`docs/project_board.json` does not exist until the folder is first connected;
the board writes it then, and from that point it is the copy git tracks.

The directory picker is Edge and Chrome only. In Firefox and Safari the board
lives in browser storage and moves by **Export JSON** / **Import JSON**, which
work everywhere.

## Exports

| Format | What it is |
|---|---|
| JSON | the board. The only one that loads back in |
| Markdown | a digest: milestone progress, then a heading per column and a checkbox per card, ticked in the finished columns. For pasting into a commit message or a doc |
| CSV | one row per card, for a spreadsheet |

Markdown and CSV are reports and are deliberately lossy. Only the JSON
round-trips.

## Keyboard

| Key | Action |
|---|---|
| `N` | add a card to the first column |
| `/` | jump to search |
| `Enter` | open the card the keyboard is on |
| `Ctrl Z` / `Ctrl Shift Z` | undo / redo |
| `Ctrl S` | write the board to the project folder now, or download it when no folder is connected |
| `Ctrl Enter` | save and close the card dialog |
| `Esc` | close a dialog, or clear the search |
| `?` | the same list, on screen |

Unlike the game's own controls, these are ordinary browser conventions — the
no-function-keys rule in `CLAUDE.md` is about reaching a key one-handed while
moving, which does not apply to a board.

## What it starts with

`js/seed.js` holds the starting content, as data rather than code, on the same
split between data and the machinery that reads it. It is read once, for a
browser with no board in it, and never consulted again.

The milestones M0 to M7 are labels (`M0 scaffold` to `M7 mission`), so the
swimlanes group by them; two more labels mark cards that `port from the lab` and
cards that `need a bench` before they count as done. Every card is traceable to
a milestone in `docs/plan.md`.

## Tests

`tests.html` in a browser, or `node run-tests.js` without one. Fifty-four
checks over the parts that are logic rather than plumbing: the schema repair,
the drop-index arithmetic, undo, the filter, the Markdown renderer, the exports,
and the seed data itself.

Those are there because each one fails *silently*. A wrong drop index files a
card one row from where you pointed. A board file that loads with a card in no
column simply never shows that card again. A stray key in `settings` makes the
startup comparison differ every time and puts a dialog in front of you on every
launch. None of the three announces itself.

The seed checks are the same argument: `seed.js` addresses columns, labels and
milestones by readable key, and a mistyped key is not an error anywhere — the
card quietly loses the label, or the card is quietly dropped. So the suite
proves every key resolves, every card lands in the Vertical slice category,
every column has cards in it, no label is dead weight and no two cards share a
title.

## Shape of the code

Plain browser scripts on a `window.PB` namespace, not ES modules — modules
cannot load from a `file://` page, and opening the file directly is how this is
meant to be used. Same reason `tools/voxel-bench` is built the same way.

| File | Holds |
|---|---|
| `js/seed.js` | the starting board as data: the vertical slice's work |
| `js/store.js` | the board object, the schema repair, persistence, undo |
| `js/project.js` | the project folder, through the File System Access API |
| `js/md.js` | the Markdown subset used in card notes |
| `js/render.js` | everything that turns the board into DOM, plus the filter |
| `js/dnd.js` | dragging cards and columns |
| `js/editor.js` | the card dialog and the column dialog |
| `js/settings.js` | the settings dialog: name, theme, labels, milestones, files |
| `js/exports.js` | JSON, Markdown and CSV |
| `js/app.js` | the top bar, the keyboard, the theme, writing to the folder |

`render.js` reads the store and never writes it. Every mutation goes through a
function in `store.js`, each of which snapshots for undo first and then notifies
— which is why one drag is one undo step, and why no view state can drift out of
step with the data.

The board is re-rendered whole on every change rather than patched. A few
hundred cards is well under a millisecond, and the two things that genuinely
cannot survive a rebuild — the open quick-add box and the scroll position — are
restored explicitly.

## Not built, on purpose

No accounts, no sync, no server, no burndown chart, no time tracking, no
dependencies between cards, no recurring cards. This is one person's board for
one project. `tools/.gdignore` keeps Godot from importing any of it.
