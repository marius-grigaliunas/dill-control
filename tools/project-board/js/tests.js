// Self-tests for the parts of the board that are logic rather than plumbing:
// the schema repair, the move arithmetic, undo, the filter, the Markdown
// renderer and the exports. Runs in the browser from tests.html, and under node
// with `node run-tests.js`.
//
// Deliberately not a test of the DOM. What is worth holding still here is the
// index a drop resolves to and the repair a hand-edited file goes through,
// because both are silent when wrong: a card files itself one row from where you
// pointed, or a board loads with a card in no column at all and simply never
// appears again.
(function (root) {
  "use strict";

  const PB = root.PB;
  const results = [];

  function check(name, fn) {
    try {
      fn();
      results.push({ name, ok: true });
    } catch (e) {
      results.push({ name, ok: false, message: e && e.message ? e.message : String(e) });
    }
  }

  function eq(actual, expected, what) {
    const a = JSON.stringify(actual), b = JSON.stringify(expected);
    if (a !== b) throw new Error(`${what || "value"}: got ${a}, wanted ${b}`);
  }

  function ok(cond, what) {
    if (!cond) throw new Error(what || "expected true");
  }

  // A tiny board with known ids, so the arithmetic below reads plainly.
  function board() {
    return {
      version: 1,
      title: "T",
      nextNumber: 1,
      columns: [
        { id: "A", name: "A", wip: 0, done: false, cards: ["k1", "k2", "k3"] },
        { id: "B", name: "B", wip: 2, done: true, cards: ["k4"] },
      ],
      cards: {
        k1: { id: "k1", number: 1, title: "one", type: "bug", priority: 0, points: 3, labels: ["l1"], milestone: "m1", checklist: [{ id: "t1", text: "x", done: true }] },
        k2: { id: "k2", number: 2, title: "two", type: "art", priority: 2, points: 5, labels: [], milestone: "m1" },
        k3: { id: "k3", number: 3, title: "three", type: "feature", priority: 3, points: null, labels: [] },
        k4: { id: "k4", number: 4, title: "four", type: "perf", priority: 1, points: 2, labels: ["l1"], milestone: "m1" },
      },
      labels: [{ id: "l1", name: "perf", color: "#b4522f" }],
      milestones: [{ id: "m1", name: "M1", due: "" }],
      settings: { theme: "system", groupBy: "none", showArchived: false },
    };
  }

  function load(raw) {
    PB.Store.data = PB.Store.migrate(raw || board());
    PB.Store.undoStack.length = 0;
    PB.Store.redoStack.length = 0;
    PB.Store.listeners.length = 0;
    return PB.Store;
  }

  // ---------------- migrate ----------------

  check("migrate fills in every missing card field", () => {
    const S = load({ columns: [{ id: "A", name: "A", cards: ["k1"] }], cards: { k1: { title: "bare" } } });
    const card = S.card("k1");
    eq(card.type, "feature", "type");
    eq(card.priority, 2, "priority");
    eq(card.points, null, "points");
    eq(card.labels, [], "labels");
    eq(card.milestone, null, "milestone");
    eq(card.checklist, [], "checklist");
    eq(card.archived, false, "archived");
    ok(typeof card.created === "string" && card.created.length > 10, "created stamp");
    ok(Number.isFinite(card.number), "number assigned");
  });

  check("migrate rejects a type, priority or label that does not exist", () => {
    const S = load({
      columns: [{ id: "A", name: "A", cards: ["k1"] }],
      cards: { k1: { title: "x", type: "nonsense", priority: 99, labels: ["ghost"], milestone: "ghost" } },
      labels: [], milestones: [],
    });
    const card = S.card("k1");
    eq(card.type, "feature", "unknown type falls back");
    eq(card.priority, 2, "unknown priority falls back");
    eq(card.labels, [], "unknown label dropped");
    eq(card.milestone, null, "unknown milestone dropped");
  });

  check("a card in no column is recovered rather than lost", () => {
    const raw = board();
    raw.columns[0].cards = ["k1"];        // k2 and k3 are now orphans
    const S = load(raw);
    const all = S.data.columns.flatMap(c => c.cards);
    ok(all.includes("k2") && all.includes("k3"), "orphans put back on the board");
    eq(all.length, 4, "every card reachable exactly once");
  });

  check("a card listed in two columns is only kept once", () => {
    const raw = board();
    raw.columns[1].cards = ["k4", "k1"];  // k1 is in A as well
    const S = load(raw);
    const all = S.data.columns.flatMap(c => c.cards);
    eq(all.filter(x => x === "k1").length, 1, "k1 appears once");
    eq(S.column("A").cards.includes("k1"), true, "the first column keeps it");
  });

  check("a card id with no card behind it is dropped", () => {
    const raw = board();
    raw.columns[0].cards.push("ghost");
    const S = load(raw);
    eq(S.column("A").cards.includes("ghost"), false, "dangling id gone");
  });

  check("migrate never returns a board with no columns", () => {
    const S = load({ columns: [], cards: {} });
    ok(S.data.columns.length > 0, "columns seeded");
  });

  check("nextNumber clears the highest number already used", () => {
    const S = load(board());
    ok(S.data.nextNumber > 4, `nextNumber ${S.data.nextNumber} is past 4`);
  });

  // ---------------- moving ----------------

  check("moveCard to a lower index in the same column", () => {
    const S = load();
    S.moveCard("k3", "A", 0);
    eq(S.column("A").cards, ["k3", "k1", "k2"], "k3 to the top");
  });

  check("moveCard to the end of its own column", () => {
    const S = load();
    S.moveCard("k1", "A", 2);
    eq(S.column("A").cards, ["k2", "k3", "k1"], "k1 to the bottom");
  });

  check("moveCard across columns", () => {
    const S = load();
    S.moveCard("k1", "B", 0);
    eq(S.column("A").cards, ["k2", "k3"], "left the source");
    eq(S.column("B").cards, ["k1", "k4"], "arrived at the index asked for");
  });

  check("an out-of-range index clamps instead of leaving a hole", () => {
    const S = load();
    S.moveCard("k1", "B", 999);
    eq(S.column("B").cards, ["k4", "k1"], "appended");
    eq(S.column("B").cards.indexOf(undefined), -1, "no hole");
  });

  // The whole reason indexForAnchor exists: with a filter on, the cards drawn are
  // a subset, so a positional index would be wrong.
  check("the drop index is right when a filter hides cards above the drop", () => {
    const S = load();
    const column = S.column("A");           // ["k1","k2","k3"]
    // Only k3 is on screen and the card is dropped above it. Counting elements
    // would say index 0; the real array wants index 2.
    eq(PB.Dnd._indexForAnchor(column, "k4", "k3"), 2, "index against the full array");
  });

  check("dropping past the last card appends", () => {
    const S = load();
    eq(PB.Dnd._indexForAnchor(S.column("A"), "k4", null), 3, "append to a foreign column");
  });

  check("a same-column drop discounts the card being moved", () => {
    const S = load();
    const column = S.column("A");
    // k1 dragged to just above k3: after k1 comes out the array is [k2,k3], so
    // k3 sits at 1 and that is where k1 goes.
    eq(PB.Dnd._indexForAnchor(column, "k1", "k3"), 1, "source removal accounted for");
    eq(PB.Dnd._indexForAnchor(column, "k1", null), 2, "append discounts itself");
  });

  check("a drop where nothing moves is refused rather than committed", () => {
    const S = load();
    eq(S.moveCard("k1", "A", 0), false, "no-op move returns false");
    eq(S.undoStack.length, 0, "and leaves nothing on the undo stack");
  });

  check("a drop into a swimlane sets the field that lane groups by", () => {
    const S = load();
    S.data.settings.groupBy = "milestone";
    eq(PB.Dnd._laneAssign(S.card("k3"), "m1"), { milestone: "m1" }, "gains the lane's milestone");
    eq(PB.Dnd._laneAssign(S.card("k1"), "none"), { milestone: null }, "the no-milestone lane clears it");
    eq(PB.Dnd._laneAssign(S.card("k1"), "m1"), null, "already in that lane: nothing to assign");

    S.data.settings.groupBy = "type";
    eq(PB.Dnd._laneAssign(S.card("k1"), "art"), { type: "art" }, "gains the lane's type");
    eq(PB.Dnd._laneAssign(S.card("k2"), "art"), null, "already that type");

    S.data.settings.groupBy = "none";
    eq(PB.Dnd._laneAssign(S.card("k1"), ""), null, "ungrouped board assigns nothing");
  });

  check("a lane assignment rides the same undo entry as the move", () => {
    const S = load();
    S.moveCard("k3", "B", 0, { milestone: "m1" });
    eq(S.card("k3").milestone, "m1", "assigned");
    eq(S.column("B").cards, ["k3", "k4"], "and moved");
    eq(S.undoStack.length, 1, "one entry for the whole drag");
    S.undo();
    eq(S.card("k3").milestone, null, "milestone back");
    eq(S.column("A").cards, ["k1", "k2", "k3"], "position back");
  });

  check("a drop that only changes the lane still commits", () => {
    const S = load();
    // Same column, same index, but the lane says the type changes. Without the
    // assign this returns false and the drag looks like it did nothing.
    eq(S.moveCard("k1", "A", 0, { type: "art" }), true, "committed");
    eq(S.card("k1").type, "art", "type changed");
    eq(S.column("A").cards, ["k1", "k2", "k3"], "order untouched");
  });

  // ---------------- the file on disk ----------------

  // App.reconcile decides whether to prompt by comparing two migrated boards, so
  // anything that makes an unchanged board serialise differently puts a dialog in
  // front of someone on every startup.
  check("a board survives a save and reload byte for byte", () => {
    const S = load();
    const once = JSON.stringify(S.migrate(JSON.parse(JSON.stringify(S.data))));
    const twice = JSON.stringify(S.migrate(JSON.parse(once)));
    eq(once === twice, true, "migrate is idempotent");
    eq(once === JSON.stringify(S.data), true, "and a migrated board is already in its final shape");
  });

  check("an edit leaves the board still in its final shape", () => {
    const S = load();
    S.updateCard("k1", { title: "changed" });
    S.addCard("B", { title: "fresh" });
    S.moveCard("k2", "B", 0);
    const mine = JSON.stringify(S.data);
    eq(JSON.stringify(S.migrate(JSON.parse(mine))), mine, "no drift after ordinary edits");
  });

  check("an unknown settings key does not survive a load", () => {
    const raw = board();
    raw.settings.mystery = 1;
    raw.settings.groupBy = "nonsense";
    const S = load(raw);
    eq(Object.keys(S.data.settings), ["theme", "groupBy"], "only known keys");
    eq(S.data.settings.groupBy, "none", "an unknown grouping falls back");
  });

  // ---------------- columns ----------------

  check("deleting a column moves its cards left rather than deleting them", () => {
    const S = load();
    S.deleteColumn("B");
    eq(S.data.columns.map(c => c.id), ["A"], "column gone");
    eq(S.column("A").cards, ["k1", "k2", "k3", "k4"], "cards kept");
  });

  check("the last column cannot be deleted", () => {
    const S = load({ columns: [{ id: "A", name: "A", cards: [] }], cards: {} });
    eq(S.deleteColumn("A"), false, "refused");
    eq(S.data.columns.length, 1, "still there");
  });

  check("moveColumn reorders", () => {
    const S = load();
    S.moveColumn("B", 0);
    eq(S.data.columns.map(c => c.id), ["B", "A"], "B first");
  });

  // ---------------- undo ----------------

  check("undo takes back a move, and redo puts it forward again", () => {
    const S = load();
    S.moveCard("k1", "B", 0);
    eq(S.column("B").cards, ["k1", "k4"], "moved");
    S.undo();
    eq(S.column("A").cards, ["k1", "k2", "k3"], "undone");
    eq(S.column("B").cards, ["k4"], "destination restored");
    S.redo();
    eq(S.column("B").cards, ["k1", "k4"], "redone");
  });

  check("undo restores a deleted card in its old place", () => {
    const S = load();
    S.deleteCard("k2");
    eq(S.card("k2"), null, "gone");
    S.undo();
    eq(S.column("A").cards, ["k1", "k2", "k3"], "back where it was");
    ok(S.card("k2"), "card object back");
  });

  check("a new action clears the redo stack", () => {
    const S = load();
    S.moveCard("k1", "B", 0);
    S.undo();
    ok(S.canRedo(), "redo available");
    S.addCard("A", { title: "new" });
    ok(!S.canRedo(), "redo dropped after a fresh change");
  });

  check("deleting a label takes it off every card", () => {
    const S = load();
    ok(S.card("k1").labels.includes("l1"), "on the card first");
    S.deleteLabel("l1");
    eq(S.card("k1").labels, [], "off the card");
    eq(S.card("k4").labels, [], "off the other card too");
  });

  check("deleting a milestone clears it from its cards", () => {
    const S = load();
    S.deleteMilestone("m1");
    eq(S.card("k1").milestone, null, "cleared");
  });

  // ---------------- derived ----------------

  check("progress counts a done column, for the board and per milestone", () => {
    const S = load();
    const all = S.progress();
    eq(all.total, 10, "3 + 5 + 2 points planned");
    eq(all.done, 2, "only column B counts as done");
    eq(all.doneCards, 1, "one card finished");
    const m1 = S.progress("m1");
    eq(m1.total, 10, "k3 has no milestone and no points");
    eq(m1.totalCards, 3, "three cards on m1");
  });

  check("an archived card is out of progress entirely", () => {
    const S = load();
    S.setArchived("k4", true);
    eq(S.progress().done, 0, "archived finished card stops counting");
  });

  check("archiveDone archives every card in a done column, once", () => {
    const S = load();
    eq(S.archiveDone(), 1, "one card archived");
    eq(S.archiveDone(), 0, "nothing left to archive");
  });

  check("duplicate gets its own id, number and checklist rows", () => {
    const S = load();
    const copy = S.duplicateCard("k1");
    ok(copy.id !== "k1", "new id");
    ok(copy.number !== 1, "new number");
    eq(S.column("A").cards[1], copy.id, "placed under the original");
    ok(copy.checklist[0].id !== "t1", "checklist rows are fresh");
    eq(copy.checklist[0].text, "x", "and carry the text");
  });

  check("clear keeps the columns, labels and milestones", () => {
    const S = load();
    S.clear();
    eq(Object.keys(S.data.cards).length, 0, "no cards");
    eq(S.data.labels.length, 1, "labels kept");
    eq(S.data.milestones.length, 1, "milestones kept");
    ok(S.canUndo(), "and it is undoable");
  });

  // ---------------- filter ----------------

  check("the filter matches on type, label, priority and milestone", () => {
    const S = load();
    const F = PB.Filter;
    F.clear();
    F.types.add("bug");
    ok(F.matches(S.card("k1")), "bug matches");
    ok(!F.matches(S.card("k2")), "art does not");

    F.clear();
    F.priority = 1;
    ok(F.matches(S.card("k1")), "P0 passes a P1 cut");
    ok(!F.matches(S.card("k2")), "P2 does not");

    F.clear();
    F.labels.add("l1");
    ok(F.matches(S.card("k4")), "labelled card matches");
    ok(!F.matches(S.card("k3")), "unlabelled does not");

    F.clear();
    F.milestone = "none";
    ok(F.matches(S.card("k3")), "k3 has no milestone");
    ok(!F.matches(S.card("k1")), "k1 has one");
    F.clear();
  });

  check("search reaches the body, the labels and the card number", () => {
    const S = load();
    const F = PB.Filter;
    S.updateCard("k3", { body: "mentions Jolt" });
    F.clear();
    F.text = "jolt";
    ok(F.matches(S.card("k3")), "body hit, case-insensitively");
    F.text = "perf";
    ok(F.matches(S.card("k1")), "label name hit");
    F.text = "#4";
    ok(F.matches(S.card("k4")), "card number hit");
    F.clear();
  });

  check("archived cards are hidden until the archive filter is on", () => {
    const S = load();
    const F = PB.Filter;
    F.clear();
    S.setArchived("k1", true);
    ok(!F.matches(S.card("k1")), "hidden by default");
    F.archived = true;
    ok(F.matches(S.card("k1")), "shown in archive view");
    ok(!F.matches(S.card("k2")), "and live cards are hidden there");
    F.clear();
  });

  // ---------------- markdown ----------------

  check("markdown escapes HTML before anything else", () => {
    const html = PB.markdown('<script>alert("x")</script>');
    ok(!/<script/i.test(html), `no live script: ${html}`);
    ok(html.includes("&lt;script&gt;"), "escaped instead");
  });

  check("a javascript: link is left as text", () => {
    const html = PB.markdown("[click](javascript:alert(1))");
    ok(!/href/.test(html), `no href: ${html}`);
  });

  check("an http link becomes an anchor with noopener", () => {
    const html = PB.markdown("[docs](https://example.com/a)");
    ok(html.includes('href="https://example.com/a"'), "href set");
    ok(html.includes('rel="noopener noreferrer"'), "rel set");
  });

  check("inline code is not touched by the emphasis rules", () => {
    const html = PB.markdown("`a*b*c` and *real*");
    ok(html.includes("<code>a*b*c</code>"), `code left alone: ${html}`);
    ok(html.includes("<em>real</em>"), "emphasis outside still applies");
  });

  check("bullets, numbers and quotes become lists and a blockquote", () => {
    const html = PB.markdown("- one\n- two\n\n1. first\n\n> quoted");
    ok(html.includes("<ul>") && html.match(/<li>/g).length === 3, `lists: ${html}`);
    ok(html.includes("<ol>"), "ordered list");
    ok(html.includes("<blockquote>quoted</blockquote>"), "quote");
  });

  check("a fenced block keeps its newlines and its angle brackets", () => {
    const html = PB.markdown("```\nif (a < b) {\n  go();\n}\n```");
    ok(html.includes("<pre><code>"), "pre block");
    ok(html.includes("a &lt; b"), "escaped inside code");
    ok(html.includes("\n  go();"), "newlines kept");
  });

  check("an empty body renders as nothing at all", () => {
    eq(PB.markdown(""), "", "empty string");
  });

  // ---------------- exports ----------------

  check("the Markdown export ticks the done column and lists checklists", () => {
    const S = load();
    const md = PB.Exports.markdown();
    ok(md.includes("- [x] **#4 four**"), `done column ticked:\n${md}`);
    ok(md.includes("- [ ] **#1 one**"), "open column not ticked");
    ok(md.includes("  - [x] x"), "checklist row carried");
    ok(md.includes("## A") && md.includes("## B"), "a heading per column");
  });

  check("the CSV quotes a title containing a comma or a quote", () => {
    const S = load();
    S.updateCard("k1", { title: 'a, b "c"' });
    const csv = PB.Exports.csv();
    ok(csv.includes('"a, b ""c"""'), `escaped: ${csv.split("\n")[1]}`);
    eq(csv.split("\n").length, 5, "header plus four cards");
  });

  check("the JSON export reloads to the same board", () => {
    const S = load();
    const json = PB.Exports.json();
    const again = S.migrate(JSON.parse(json));
    eq(again.columns.map(c => c.cards), S.data.columns.map(c => c.cards), "same layout");
    eq(Object.keys(again.cards).length, 4, "same cards");
  });

  // ---------------- the seeded board ----------------

  check("the first-run board is internally consistent", () => {
    const seeded = PB.Store.seed();
    const migrated = PB.Store.migrate(JSON.parse(JSON.stringify(seeded)));
    const before = seeded.columns.flatMap(c => c.cards).length;
    const after = migrated.columns.flatMap(c => c.cards).length;
    eq(after, before, "no card repaired away, so every id and label in the seed is real");
    ok(before > 0, "it has cards");
    eq(migrated.columns.filter(c => c.done).length, 1, "exactly one done column");
  });

  // The data file addresses columns, labels and milestones by readable key. A
  // mistyped key is not an error anywhere — the card silently loses the label, or
  // the whole card is silently dropped — so it is checked here instead.
  check("every key in seed.js resolves", () => {
    const src = PB.SEED;
    ok(src && Array.isArray(src.cards) && src.cards.length > 0, "seed.js is loaded and has cards");
    const cols = new Set(src.columns.map(c => c.key));
    const labels = new Set(src.labels.map(l => l.key));
    const stones = new Set(src.milestones.map(m => m.key));
    const types = new Set(PB.TYPES.map(t => t.id));
    for (const card of src.cards) {
      ok(cols.has(card.col), `card "${card.title}" names column "${card.col}"`);
      ok(types.has(card.type), `card "${card.title}" has type "${card.type}"`);
      ok([0, 1, 2, 3].includes(card.priority), `card "${card.title}" has priority ${card.priority}`);
      for (const l of card.labels || []) ok(labels.has(l), `card "${card.title}" names label "${l}"`);
      if (card.milestone) ok(stones.has(card.milestone), `card "${card.title}" names milestone`);
    }
  });

  check("no two seed cards share a title", () => {
    const seen = new Set();
    for (const card of PB.SEED.cards) {
      ok(!seen.has(card.title), `duplicated: "${card.title}"`);
      seen.add(card.title);
    }
  });

  check("every seeded card is in the vertical slice category", () => {
    const board = PB.Store.seed();
    const slice = board.milestones.find(m => m.name === "Vertical slice");
    ok(slice, "there is a Vertical slice milestone");
    const cards = Object.values(board.cards);
    const off = cards.filter(c => c.milestone !== slice.id);
    eq(off.map(c => c.title), [], "nothing is outside it");
    eq(cards.length, PB.SEED.cards.length, "every card in the data file made it onto the board");
  });

  check("the seeded board fills every column and every label is used", () => {
    const board = PB.Store.seed();
    for (const col of board.columns) {
      ok(col.cards.length > 0, `"${col.name}" has cards`);
    }
    const used = new Set(Object.values(board.cards).flatMap(c => c.labels));
    const unused = board.labels.filter(l => !used.has(l.id)).map(l => l.name);
    eq(unused, [], "no label is dead weight");
    const kinds = new Set(Object.values(board.cards).map(c => c.type));
    ok(kinds.size >= 6, `${kinds.size} of the nine types are in use`);
  });

  check("the seeded board carries real estimates and progress", () => {
    PB.Store.data = PB.Store.migrate(PB.Store.seed());
    const p = PB.Store.progress();
    ok(p.total > 0, "points are estimated");
    ok(p.done > 0 && p.done < p.total, `${p.done} of ${p.total} points done`);
    ok(p.doneCards > 0 && p.doneCards < p.totalCards, `${p.doneCards} of ${p.totalCards} cards done`);
  });

  check("seed.js has no placeholder text left in it", () => {
    const json = JSON.stringify(PB.SEED);
    for (const bad of ["Lorem", "TODO:", "FIXME", "XXX", "Untitled", "tbd"]) {
      ok(!json.includes(bad), `found "${bad}"`);
    }
  });

  // ---------------- report ----------------

  const failed = results.filter(r => !r.ok);
  const report = { results, passed: results.length - failed.length, failed: failed.length };

  if (root.document) {
    const host = root.document.getElementById("out");
    const head = root.document.createElement("p");
    head.className = failed.length ? "fail" : "pass";
    head.textContent = failed.length
      ? `${failed.length} of ${results.length} failed`
      : `all ${results.length} passed`;
    host.append(head);
    for (const r of results) {
      const line = root.document.createElement("div");
      line.className = r.ok ? "row pass" : "row fail";
      line.textContent = `${r.ok ? "ok  " : "FAIL"} ${r.name}${r.ok ? "" : ` — ${r.message}`}`;
      host.append(line);
    }
  } else {
    for (const r of results) {
      if (!r.ok) console.log(`FAIL ${r.name} — ${r.message}`);
    }
    console.log(failed.length ? `${failed.length} of ${results.length} failed` : `all ${results.length} passed`);
    if (typeof process !== "undefined" && failed.length) process.exitCode = 1;
  }

  root.PB.testReport = report;
})(typeof window !== "undefined" ? window : globalThis);
