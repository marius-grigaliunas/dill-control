// The board itself: one plain object, held in memory, written to localStorage on
// every change and to the project folder when one is connected.
//
// Columns own an ordered array of card ids and cards live in a map, so moving a
// card is a splice rather than a re-sort of anything. Nothing in here touches
// the DOM: render.js reads this and never writes it, every mutation goes through
// a function here, and every one of those snapshots for undo first. That is what
// makes Ctrl+Z reliable for a drag as well as for a typed edit.
(function (PB) {
  "use strict";

  const KEY = "dillcontrol-project-board";
  const VERSION = 1;
  const UNDO_DEPTH = 60;

  // Card kinds. Deliberately gamedev rather than generic: an art task and a
  // perf task are not the same work and do not go to the same person-hours.
  const TYPES = [
    { id: "feature",  name: "Feature",  color: "#3f7fbf" },
    { id: "bug",      name: "Bug",      color: "#c43d31" },
    { id: "perf",     name: "Perf",     color: "#b4522f" },
    { id: "art",      name: "Art",      color: "#9a5bb0" },
    { id: "audio",    name: "Audio",    color: "#2f8f8a" },
    { id: "design",   name: "Design",   color: "#c08a1e" },
    { id: "tech",     name: "Tech",     color: "#6b7b86" },
    { id: "tooling",  name: "Tooling",  color: "#6f8f7a" },
    { id: "polish",   name: "Polish",   color: "#bb7a8f" },
  ];

  // Four levels, and the top one is called Blocker rather than P0 so it reads
  // without a legend.
  const PRIORITIES = [
    { id: 0, name: "Blocker", short: "P0" },
    { id: 1, name: "High",    short: "P1" },
    { id: 2, name: "Normal",  short: "P2" },
    { id: 3, name: "Low",     short: "P3" },
  ];

  let counter = 0;
  function id(prefix) {
    counter += 1;
    return `${prefix}${Date.now().toString(36)}${counter.toString(36)}`;
  }

  function today() {
    return new Date().toISOString().slice(0, 10);
  }

  function nowStamp() {
    return new Date().toISOString();
  }

  const Store = {
    data: null,
    undoStack: [],
    redoStack: [],
    dirty: false,
    listeners: [],

    // --------------- lifecycle ---------------

    load() {
      let raw = null;
      try { raw = localStorage.getItem(KEY); } catch (e) { /* private window: memory only */ }
      if (raw) {
        try {
          this.data = this.migrate(JSON.parse(raw));
          return "storage";
        } catch (e) { /* fall through to the seed rather than losing the session */ }
      }
      // Through migrate rather than used raw, so a fresh board is in exactly the
      // shape a loaded one is. App.reconcile compares the two for real.
      this.data = this.migrate(this.seed());
      this.save();
      return "seed";
    },

    // Accepts anything shaped like a board and fills in what a older or
    // hand-edited file left out, so an import can never half-load.
    migrate(raw) {
      const d = raw && typeof raw === "object" ? raw : {};
      const s = d.settings && typeof d.settings === "object" ? d.settings : {};
      const board = {
        version: VERSION,
        title: typeof d.title === "string" && d.title.trim() ? d.title : "Dill Control",
        nextNumber: Number.isFinite(d.nextNumber) ? d.nextNumber : 1,
        columns: Array.isArray(d.columns) ? d.columns : [],
        cards: d.cards && typeof d.cards === "object" ? d.cards : {},
        labels: Array.isArray(d.labels) ? d.labels : [],
        milestones: Array.isArray(d.milestones) ? d.milestones : [],
        // Built key by key rather than by Object.assign, so an unknown key in a
        // hand-edited file cannot survive into the comparison reconcile() makes.
        settings: {
          theme: ["system", "light", "dark"].includes(s.theme) ? s.theme : "system",
          groupBy: ["none", "milestone", "type"].includes(s.groupBy) ? s.groupBy : "none",
        },
      };

      board.columns = board.columns
        .filter(c => c && typeof c === "object")
        .map(c => ({
          id: c.id || id("c"),
          name: typeof c.name === "string" ? c.name : "Column",
          wip: Number.isFinite(c.wip) && c.wip > 0 ? Math.floor(c.wip) : 0,
          done: !!c.done,
          cards: Array.isArray(c.cards) ? c.cards.filter(x => typeof x === "string") : [],
        }));
      if (!board.columns.length) board.columns = this.seed().columns.map(c => ({ ...c, cards: [] }));

      const types = new Set(TYPES.map(t => t.id));
      const labelIds = new Set(board.labels.map(l => l && l.id));
      const msIds = new Set(board.milestones.map(m => m && m.id));
      const clean = {};
      for (const key of Object.keys(board.cards)) {
        const c = board.cards[key];
        if (!c || typeof c !== "object") continue;
        clean[key] = {
          id: key,
          number: Number.isFinite(c.number) ? c.number : board.nextNumber++,
          title: typeof c.title === "string" ? c.title : "Untitled",
          body: typeof c.body === "string" ? c.body : "",
          type: types.has(c.type) ? c.type : "feature",
          priority: [0, 1, 2, 3].includes(c.priority) ? c.priority : 2,
          points: Number.isFinite(c.points) ? c.points : null,
          labels: Array.isArray(c.labels) ? c.labels.filter(l => labelIds.has(l)) : [],
          milestone: msIds.has(c.milestone) ? c.milestone : null,
          assignee: typeof c.assignee === "string" ? c.assignee : "",
          checklist: Array.isArray(c.checklist)
            ? c.checklist.filter(t => t && typeof t.text === "string")
                .map(t => ({ id: t.id || id("t"), text: t.text, done: !!t.done }))
            : [],
          archived: !!c.archived,
          created: typeof c.created === "string" ? c.created : nowStamp(),
          updated: typeof c.updated === "string" ? c.updated : nowStamp(),
        };
      }
      board.cards = clean;

      // A card in no column is unreachable and a card in two columns renders
      // twice, so the column arrays are the authority and get repaired here.
      const seen = new Set();
      for (const col of board.columns) {
        col.cards = col.cards.filter(cid => {
          if (!board.cards[cid] || seen.has(cid)) return false;
          seen.add(cid);
          return true;
        });
      }
      const orphans = Object.keys(board.cards).filter(cid => !seen.has(cid));
      if (orphans.length) board.columns[0].cards.push(...orphans);

      board.labels = board.labels.filter(l => l && typeof l === "object").map(l => ({
        id: l.id || id("l"),
        name: typeof l.name === "string" ? l.name : "label",
        color: /^#[0-9a-fA-F]{6}$/.test(l.color) ? l.color : "#6f8f7a",
      }));
      board.milestones = board.milestones.filter(m => m && typeof m === "object").map(m => ({
        id: m.id || id("m"),
        name: typeof m.name === "string" ? m.name : "Milestone",
        due: typeof m.due === "string" ? m.due : "",
      }));
      const maxNum = Object.values(board.cards).reduce((n, c) => Math.max(n, c.number), 0);
      board.nextNumber = Math.max(board.nextNumber, maxNum + 1);
      return board;
    },

    // --------------- persistence ---------------

    save() {
      try { localStorage.setItem(KEY, JSON.stringify(this.data)); }
      catch (e) { /* over quota or private window; the project folder is the durable copy */ }
    },

    serialise() {
      return JSON.stringify(this.data, null, 2);
    },

    onChange(fn) { this.listeners.push(fn); },

    // Every mutation ends here. `label` is what the undo button will offer to
    // take back, so it is written from the caller's point of view.
    //
    // Deliberately no board-level modified stamp: App.reconcile decides whether
    // the repo's file differs from this browser's copy by comparing two migrated
    // boards, and a timestamp would make every one of those comparisons differ
    // and put a prompt in front of someone on every single startup.
    commit(label) {
      this.dirty = true;
      this.save();
      for (const fn of this.listeners) fn(label);
    },

    snapshot(label) {
      this.undoStack.push({ label: label || "change", json: JSON.stringify(this.data) });
      if (this.undoStack.length > UNDO_DEPTH) this.undoStack.shift();
      this.redoStack.length = 0;
    },

    canUndo() { return this.undoStack.length > 0; },
    canRedo() { return this.redoStack.length > 0; },
    undoLabel() { const e = this.undoStack[this.undoStack.length - 1]; return e ? e.label : ""; },

    undo() {
      const entry = this.undoStack.pop();
      if (!entry) return null;
      this.redoStack.push({ label: entry.label, json: JSON.stringify(this.data) });
      this.data = JSON.parse(entry.json);
      this.commit(`undo ${entry.label}`);
      return entry.label;
    },

    redo() {
      const entry = this.redoStack.pop();
      if (!entry) return null;
      this.undoStack.push({ label: entry.label, json: JSON.stringify(this.data) });
      this.data = JSON.parse(entry.json);
      this.commit(`redo ${entry.label}`);
      return entry.label;
    },

    // Replaces the whole board, for an import or a load from the project folder.
    replace(raw, label) {
      this.snapshot(label || "load");
      this.data = this.migrate(raw);
      this.commit(label || "load");
    },

    // --------------- reads ---------------

    column(cid) { return this.data.columns.find(c => c.id === cid) || null; },
    card(id) { return this.data.cards[id] || null; },
    columnOf(cardId) { return this.data.columns.find(c => c.cards.includes(cardId)) || null; },
    label(lid) { return this.data.labels.find(l => l.id === lid) || null; },
    milestone(mid) { return this.data.milestones.find(m => m.id === mid) || null; },
    type(tid) { return TYPES.find(t => t.id === tid) || TYPES[0]; },
    priority(p) { return PRIORITIES.find(x => x.id === p) || PRIORITIES[2]; },

    // --------------- cards ---------------

    addCard(columnId, fields, atIndex) {
      const col = this.column(columnId);
      if (!col) return null;
      this.snapshot("add card");
      const cardId = id("k");
      const card = {
        id: cardId,
        number: this.data.nextNumber++,
        title: (fields && fields.title) || "Untitled",
        body: (fields && fields.body) || "",
        type: (fields && fields.type) || "feature",
        priority: fields && [0, 1, 2, 3].includes(fields.priority) ? fields.priority : 2,
        points: fields && Number.isFinite(fields.points) ? fields.points : null,
        labels: (fields && fields.labels) ? fields.labels.slice() : [],
        milestone: (fields && fields.milestone) || null,
        assignee: (fields && fields.assignee) || "",
        checklist: [],
        archived: false,
        created: nowStamp(),
        updated: nowStamp(),
      };
      this.data.cards[cardId] = card;
      const at = Number.isFinite(atIndex) ? Math.max(0, Math.min(atIndex, col.cards.length)) : col.cards.length;
      col.cards.splice(at, 0, cardId);
      this.commit("add card");
      return card;
    },

    updateCard(cardId, fields) {
      const card = this.card(cardId);
      if (!card) return null;
      this.snapshot("edit card");
      Object.assign(card, fields);
      card.updated = nowStamp();
      this.commit("edit card");
      return card;
    },

    deleteCard(cardId) {
      const col = this.columnOf(cardId);
      if (!col) return false;
      this.snapshot("delete card");
      col.cards.splice(col.cards.indexOf(cardId), 1);
      delete this.data.cards[cardId];
      this.commit("delete card");
      return true;
    },

    // One card copied, including its checklist, placed right under the original.
    duplicateCard(cardId) {
      const card = this.card(cardId);
      const col = this.columnOf(cardId);
      if (!card || !col) return null;
      this.snapshot("duplicate card");
      const copyId = id("k");
      const copy = JSON.parse(JSON.stringify(card));
      copy.id = copyId;
      copy.number = this.data.nextNumber++;
      copy.title = `${card.title} (copy)`;
      copy.created = copy.updated = nowStamp();
      copy.checklist = copy.checklist.map(t => ({ ...t, id: id("t") }));
      this.data.cards[copyId] = copy;
      col.cards.splice(col.cards.indexOf(cardId) + 1, 0, copyId);
      this.commit("duplicate card");
      return copy;
    },

    // The one move used by drag and drop and by the keyboard alike. `index` is
    // an index into the destination column *after* the card has been taken out
    // of its source, which is the only reading that makes a same-column nudge
    // land where the drop line was drawn.
    //
    // `assign` is for a drop onto a swimlane: dragging a card into the "M2" lane
    // has to actually put it on M2, or it lands in the column asked for and then
    // vanishes back to its own lane, which reads as the drop having failed. It
    // rides the same snapshot so one drag stays one undo.
    moveCard(cardId, toColumnId, index, assign) {
      const from = this.columnOf(cardId);
      const to = this.column(toColumnId);
      if (!from || !to) return false;
      const at = Math.max(0, Math.min(Number.isFinite(index) ? index : to.cards.length, to.cards.length));
      const stays = from === to && from.cards.indexOf(cardId) === at;
      if (stays && !assign) return false;
      this.snapshot("move card");
      if (assign) Object.assign(this.data.cards[cardId], assign);
      from.cards.splice(from.cards.indexOf(cardId), 1);
      const bounded = Math.max(0, Math.min(at, to.cards.length));
      to.cards.splice(bounded, 0, cardId);
      this.card(cardId).updated = nowStamp();
      this.commit("move card");
      return true;
    },

    setArchived(cardId, archived) {
      const card = this.card(cardId);
      if (!card) return;
      this.snapshot(archived ? "archive card" : "restore card");
      card.archived = !!archived;
      card.updated = nowStamp();
      this.commit(archived ? "archive card" : "restore card");
    },

    archiveDone() {
      const cols = this.data.columns.filter(c => c.done);
      const ids = [];
      for (const col of cols) for (const cid of col.cards) if (!this.data.cards[cid].archived) ids.push(cid);
      if (!ids.length) return 0;
      this.snapshot("archive finished");
      for (const cid of ids) { this.data.cards[cid].archived = true; this.data.cards[cid].updated = nowStamp(); }
      this.commit("archive finished");
      return ids.length;
    },

    // --------------- checklist ---------------

    addTask(cardId, text) {
      const card = this.card(cardId);
      if (!card) return null;
      const task = { id: id("t"), text: text || "", done: false };
      card.checklist.push(task);
      return task;
    },

    // --------------- columns ---------------

    addColumn(name, atIndex) {
      this.snapshot("add column");
      const col = { id: id("c"), name: name || "New column", wip: 0, done: false, cards: [] };
      const at = Number.isFinite(atIndex) ? atIndex : this.data.columns.length;
      this.data.columns.splice(at, 0, col);
      this.commit("add column");
      return col;
    },

    updateColumn(colId, fields) {
      const col = this.column(colId);
      if (!col) return;
      this.snapshot("edit column");
      Object.assign(col, fields);
      this.commit("edit column");
    },

    // Cards are never destroyed with their column: they move to the column on
    // the left, because losing a week of cards to one stray click is the single
    // worst thing a board can do.
    deleteColumn(colId) {
      if (this.data.columns.length < 2) return false;
      const at = this.data.columns.findIndex(c => c.id === colId);
      if (at < 0) return false;
      this.snapshot("delete column");
      const [col] = this.data.columns.splice(at, 1);
      const into = this.data.columns[Math.max(0, at - 1)];
      into.cards.push(...col.cards);
      this.commit("delete column");
      return true;
    },

    moveColumn(colId, index) {
      const at = this.data.columns.findIndex(c => c.id === colId);
      if (at < 0 || at === index) return;
      this.snapshot("move column");
      const [col] = this.data.columns.splice(at, 1);
      this.data.columns.splice(Math.max(0, Math.min(index, this.data.columns.length)), 0, col);
      this.commit("move column");
    },

    // --------------- labels and milestones ---------------

    addLabel(name, color) {
      this.snapshot("add label");
      const label = { id: id("l"), name: name || "label", color: color || "#6f8f7a" };
      this.data.labels.push(label);
      this.commit("add label");
      return label;
    },

    updateLabel(lid, fields) {
      const label = this.label(lid);
      if (!label) return;
      this.snapshot("edit label");
      Object.assign(label, fields);
      this.commit("edit label");
    },

    deleteLabel(lid) {
      this.snapshot("delete label");
      this.data.labels = this.data.labels.filter(l => l.id !== lid);
      for (const card of Object.values(this.data.cards)) {
        card.labels = card.labels.filter(x => x !== lid);
      }
      this.commit("delete label");
    },

    addMilestone(name, due) {
      this.snapshot("add milestone");
      const ms = { id: id("m"), name: name || "Milestone", due: due || "" };
      this.data.milestones.push(ms);
      this.commit("add milestone");
      return ms;
    },

    updateMilestone(mid, fields) {
      const ms = this.milestone(mid);
      if (!ms) return;
      this.snapshot("edit milestone");
      Object.assign(ms, fields);
      this.commit("edit milestone");
    },

    deleteMilestone(mid) {
      this.snapshot("delete milestone");
      this.data.milestones = this.data.milestones.filter(m => m.id !== mid);
      for (const card of Object.values(this.data.cards)) {
        if (card.milestone === mid) card.milestone = null;
      }
      this.commit("delete milestone");
    },

    setSetting(key, value) {
      this.data.settings[key] = value;
      this.save();
      for (const fn of this.listeners) fn("setting");
    },

    // --------------- derived ---------------

    // Points done over points planned, for a milestone or for the whole board.
    progress(milestoneId) {
      let done = 0, total = 0, doneCards = 0, totalCards = 0;
      const doneCols = new Set(this.data.columns.filter(c => c.done).map(c => c.id));
      for (const col of this.data.columns) {
        for (const cid of col.cards) {
          const card = this.data.cards[cid];
          if (!card || card.archived) continue;
          if (milestoneId !== undefined && card.milestone !== milestoneId) continue;
          const pts = Number.isFinite(card.points) ? card.points : 0;
          total += pts; totalCards += 1;
          if (doneCols.has(col.id)) { done += pts; doneCards += 1; }
        }
      }
      return { done, total, doneCards, totalCards };
    },

    clear() {
      this.snapshot("clear board");
      const fresh = this.seed();
      fresh.title = this.data.title;
      fresh.labels = this.data.labels;
      fresh.milestones = this.data.milestones;
      fresh.settings = this.data.settings;
      fresh.cards = {};
      for (const col of fresh.columns) col.cards = [];
      fresh.nextNumber = 1;
      this.data = fresh;
      this.commit("clear board");
    },

    // --------------- the first-run board ---------------

    // Assembled from PB.SEED, which is content rather than code and lives in its
    // own file for that reason. The keys there are readable names ("slice", "m3")
    // and the real ids are generated here, so the data file never carries an id
    // and two cards can never accidentally share one.
    //
    // Only ever called for a browser with no board in it at all.
    seed() {
      const src = PB.SEED;
      if (!src) return this.emptyBoard();

      const msIds = {}, labelIds = {};
      const milestones = src.milestones.map(m => {
        const mid = id("m");
        msIds[m.key] = mid;
        return { id: mid, name: m.name, due: m.due || "" };
      });
      const labels = src.labels.map(l => {
        const lid = id("l");
        labelIds[l.key] = lid;
        return { id: lid, name: l.name, color: l.color };
      });
      const columns = src.columns.map(c =>
        ({ id: id("c"), name: c.name, wip: c.wip || 0, done: !!c.done, cards: [] }));

      const board = {
        version: VERSION,
        title: src.title,
        nextNumber: 1,
        columns,
        cards: {},
        labels,
        milestones,
        settings: { theme: "system", groupBy: "none" },
      };

      // Every card on this board is in the one category, so the milestone is
      // taken from it rather than repeated on all fifty cards.
      const defaultMilestone = milestones.length ? milestones[0].id : null;

      for (const c of src.cards) {
        const at = src.columns.findIndex(x => x.key === c.col);
        if (at < 0) continue;
        const cardId = id("k");
        board.cards[cardId] = {
          id: cardId,
          number: board.nextNumber++,
          title: c.title,
          body: c.body || "",
          type: c.type,
          priority: c.priority,
          points: Number.isFinite(c.points) ? c.points : null,
          labels: (c.labels || []).map(k => labelIds[k]).filter(Boolean),
          milestone: c.milestone ? (msIds[c.milestone] || null) : defaultMilestone,
          assignee: "",
          checklist: (c.checklist || []).map(t => ({ id: id("t"), text: t, done: false })),
          archived: false,
          created: nowStamp(),
          updated: nowStamp(),
        };
        board.columns[at].cards.push(cardId);
      }

      return board;
    },

    // The fallback when seed.js was not loaded: five columns and nothing in them,
    // which is a usable board rather than a crash.
    emptyBoard() {
      const names = [["Backlog", 0, false], ["Next", 6, false], ["In progress", 3, false],
        ["Verify", 0, false], ["Done", 0, true]];
      return {
        version: VERSION,
        title: "Board",
        nextNumber: 1,
        columns: names.map(([name, wip, done]) => ({ id: id("c"), name, wip, done, cards: [] })),
        cards: {},
        labels: [],
        milestones: [],
        settings: { theme: "system", groupBy: "none" },
      };
    },
  };

  PB.TYPES = TYPES;
  PB.PRIORITIES = PRIORITIES;
  PB.Store = Store;
  PB.id = id;
  PB.today = today;
})(window.PB = window.PB || {});
