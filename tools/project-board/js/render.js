// Everything that turns the board object into DOM. It reads Store and never
// writes it: a click that changes something calls a Store method, Store notifies,
// and the board is rebuilt from scratch.
//
// Rebuilding whole rather than patching is the right trade at this size — a few
// hundred cards is well under a millisecond and it means no view state can drift
// out of step with the data. The two things that genuinely cannot survive a
// rebuild, the open quick-add composer and the scroll position, are restored
// explicitly below.
(function (PB) {
  "use strict";

  const Store = PB.Store;

  function el(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined && text !== null) node.textContent = String(text);
    return node;
  }

  // Filter state. Empty sets mean "everything", which is why a fresh board shows
  // every card without any chip being pressed.
  const Filter = {
    text: "",
    labels: new Set(),
    types: new Set(),
    milestone: "",      // "" all, or a milestone id, or "none"
    priority: 3,        // show this priority and anything more urgent
    archived: false,

    active() {
      return !!this.text || this.labels.size > 0 || this.types.size > 0
        || this.milestone !== "" || this.priority < 3 || this.archived;
    },

    clear() {
      this.text = "";
      this.labels.clear();
      this.types.clear();
      this.milestone = "";
      this.priority = 3;
      this.archived = false;
    },

    matches(card) {
      if (card.archived !== this.archived) return false;
      if (this.types.size && !this.types.has(card.type)) return false;
      if (this.labels.size && !card.labels.some(l => this.labels.has(l))) return false;
      if (this.milestone === "none" && card.milestone) return false;
      if (this.milestone && this.milestone !== "none" && card.milestone !== this.milestone) return false;
      if (card.priority > this.priority) return false;
      if (this.text) {
        const needle = this.text.toLowerCase();
        const hay = [
          card.title, card.body, card.assignee, `#${card.number}`,
          Store.type(card.type).name,
          ...card.labels.map(l => (Store.label(l) || {}).name || ""),
          ...card.checklist.map(t => t.text),
        ].join(" ").toLowerCase();
        if (!hay.includes(needle)) return false;
      }
      return true;
    },
  };

  const Render = {
    composerColumn: null,   // the column whose quick-add box is open
    composerText: "",

    // --------------- cards ---------------

    cardEl(card, column) {
      const node = el("div", "card");
      node.dataset.cardId = card.id;
      node.draggable = true;
      node.tabIndex = 0;
      node.style.setProperty("--type-color", Store.type(card.type).color);
      if (column && column.done) node.classList.add("done");

      const top = el("div", "card-top");
      top.append(el("div", "card-title", card.title), el("div", "card-id", `#${card.number}`));
      node.append(top);

      const type = Store.type(card.type);
      const tags = el("div", "card-labels");
      const typeTag = el("span", "tag type", type.name);
      typeTag.style.setProperty("--tag-bg", type.color);
      typeTag.style.setProperty("--tag-ink", "#fff");
      tags.append(typeTag);
      for (const lid of card.labels) {
        const label = Store.label(lid);
        if (!label) continue;
        const tag = el("span", "tag", label.name);
        tag.style.setProperty("--tag-bg", label.color);
        tag.style.setProperty("--tag-ink", "#fff");
        tags.append(tag);
      }
      if (card.archived) tags.append(el("span", "tag", "archived"));
      node.append(tags);

      const done = card.checklist.filter(t => t.done).length;
      if (card.checklist.length) {
        const bar = el("div", "card-bar");
        const fill = el("i");
        fill.style.width = `${Math.round((done / card.checklist.length) * 100)}%`;
        bar.append(fill);
        node.append(bar);
      }

      const foot = el("div", "card-foot");
      const pri = Store.priority(card.priority);
      if (card.priority <= 1) {
        foot.append(el("span", `pri p${card.priority}`, pri.short));
      }
      if (card.checklist.length) foot.append(el("span", null, `${done}/${card.checklist.length}`));
      if (card.assignee) foot.append(el("span", null, card.assignee));
      const ms = card.milestone ? Store.milestone(card.milestone) : null;
      if (ms) foot.append(el("span", "ms", ms.name));
      foot.append(el("span", "grow"));
      if (Number.isFinite(card.points)) foot.append(el("span", "points", `${card.points}`));
      if (foot.children.length > 1) node.append(foot);

      return node;
    },

    // --------------- columns ---------------

    columnEl(column, cards, laneKey) {
      const node = el("section", "col");
      node.dataset.columnId = column.id;
      node.dataset.lane = laneKey || "";

      const head = el("header", "col-head");
      head.draggable = !laneKey;               // columns only reorder on the ungrouped board
      head.append(el("h3", null, column.name));

      const count = el("span", "count", column.wip ? `${cards.length}/${column.wip}` : `${cards.length}`);
      if (column.wip && cards.length > column.wip) {
        count.classList.add("over");
        count.title = `Over the ${column.wip}-card limit for this column`;
      }
      head.append(count, el("span", "grow"));

      const edit = el("button", "btn icon", "⋯");
      edit.type = "button";
      edit.title = "Column settings";
      edit.setAttribute("aria-label", `Settings for ${column.name}`);
      edit.addEventListener("click", ev => { ev.stopPropagation(); PB.Editor.openColumn(column.id); });
      head.append(edit);
      node.append(head);

      const list = el("div", "col-cards");
      list.dataset.columnId = column.id;
      if (!cards.length) list.append(el("div", "col-empty", Filter.active() ? "nothing matches" : "empty"));
      for (const card of cards) list.append(this.cardEl(card, column));
      node.append(list);

      const foot = el("div", "col-foot");
      if (this.composerColumn === column.id) {
        foot.append(this.composerEl(column));
      } else {
        const add = el("button", "btn", "+ Add card");
        add.type = "button";
        add.addEventListener("click", () => {
          this.composerColumn = column.id;
          this.composerText = "";
          this.all();
        });
        foot.append(add);
      }
      node.append(foot);
      return node;
    },

    // Quick add: type a title, Enter files it and leaves the box open for the
    // next one. Filling a backlog is the case this exists for; anything that
    // needs a body, points or a label is one click further into the card dialog.
    composerEl(column) {
      const wrap = el("div", "field");
      const box = el("textarea");
      box.rows = 2;
      box.placeholder = "Card title. Enter files it, Shift Enter for a new line, Esc closes.";
      box.value = this.composerText;

      const commit = openEditor => {
        const title = box.value.trim();
        if (!title) { this.composerColumn = null; this.all(); return; }
        const card = Store.addCard(column.id, { title });
        this.composerText = "";
        if (openEditor && card) { this.composerColumn = null; this.all(); PB.Editor.openCard(card.id); }
      };

      box.addEventListener("keydown", ev => {
        if (ev.key === "Enter" && !ev.shiftKey) { ev.preventDefault(); commit(false); }
        else if (ev.key === "Escape") { ev.preventDefault(); this.composerColumn = null; this.all(); }
      });
      box.addEventListener("input", () => { this.composerText = box.value; });

      const row = el("div", "pickers");
      const file = el("button", "btn primary", "Add");
      file.type = "button";
      file.addEventListener("click", () => commit(false));
      const detail = el("button", "btn", "Add and open");
      detail.type = "button";
      detail.addEventListener("click", () => commit(true));
      const close = el("button", "btn", "Close");
      close.type = "button";
      close.addEventListener("click", () => { this.composerColumn = null; this.all(); });
      row.append(file, detail, close);

      wrap.append(box, row);
      return wrap;
    },

    // --------------- lanes ---------------

    // Which lanes there are, and which cards belong to each. Grouping by
    // milestone is the view a month-based plan actually wants; grouping by type
    // answers "how much art is left" in one glance.
    lanes() {
      const group = Store.data.settings.groupBy;
      if (group === "milestone") {
        const lanes = Store.data.milestones.map(m => ({ key: m.id, name: m.name, milestone: m }));
        lanes.push({ key: "none", name: "No milestone", milestone: null });
        return lanes.map(l => ({ ...l, test: c => (c.milestone || "none") === l.key }));
      }
      if (group === "type") {
        return PB.TYPES.map(t => ({ key: t.id, name: t.name, test: c => c.type === t.id }));
      }
      return [{ key: "", name: "", test: () => true }];
    },

    boardEl() {
      const board = el("div");
      const lanes = this.lanes();
      const grouped = lanes.length > 1 || !!lanes[0].key;
      let shown = 0;

      for (const lane of lanes) {
        const perColumn = Store.data.columns.map(column => {
          const cards = column.cards
            .map(cid => Store.card(cid))
            .filter(card => card && Filter.matches(card) && lane.test(card));
          return { column, cards };
        });
        const laneTotal = perColumn.reduce((n, x) => n + x.cards.length, 0);
        shown += laneTotal;
        // An empty lane is noise on a grouped board unless it is the only one.
        if (grouped && !laneTotal && lanes.length > 1) continue;

        const laneNode = el("section", "lane");
        if (grouped) {
          const head = el("div", "lane-head");
          head.append(el("h2", null, lane.name));
          if (lane.milestone !== undefined && lane.milestone) {
            const p = Store.progress(lane.milestone.id);
            const meter = el("div", "meter");
            const fill = el("i");
            fill.style.width = `${p.total ? Math.round((p.done / p.total) * 100) : 0}%`;
            meter.append(fill);
            head.append(meter, el("span", "meter-text",
              `${p.doneCards}/${p.totalCards} cards · ${p.done}/${p.total} pts`));
            if (lane.milestone.due) head.append(el("span", "meter-text", `due ${lane.milestone.due}`));
          } else {
            head.append(el("span", "meter-text", `${laneTotal} card${laneTotal === 1 ? "" : "s"}`));
          }
          laneNode.append(head);
        }

        const cols = el("div", "cols");
        for (const { column, cards } of perColumn) cols.append(this.columnEl(column, cards, lane.key));
        laneNode.append(cols);
        board.append(laneNode);
      }

      if (!shown) {
        const empty = el("div", "empty-board");
        if (Filter.active()) {
          empty.append(el("p", null, "No card matches the current filter."));
          const btn = el("button", "btn primary", "Clear the filter");
          btn.type = "button";
          btn.addEventListener("click", () => { Filter.clear(); this.all(); });
          empty.append(btn);
        } else {
          empty.append(el("p", null, "The board is empty. Add a card to a column to start."));
        }
        board.append(empty);
      }
      return { node: board, shown };
    },

    // --------------- the filter strip ---------------

    filtersEl() {
      const strip = document.getElementById("filters");
      strip.textContent = "";

      strip.append(el("span", "label", "Type"));
      for (const type of PB.TYPES) {
        const chip = el("button", "chip dot", type.name);
        chip.type = "button";
        chip.style.setProperty("--chip-color", type.color);
        chip.setAttribute("aria-pressed", Filter.types.has(type.id) ? "true" : "false");
        chip.addEventListener("click", () => {
          if (Filter.types.has(type.id)) Filter.types.delete(type.id); else Filter.types.add(type.id);
          this.all();
        });
        strip.append(chip);
      }

      if (Store.data.labels.length) {
        strip.append(el("span", "sep"), el("span", "label", "Label"));
        for (const label of Store.data.labels) {
          const chip = el("button", "chip dot", label.name);
          chip.type = "button";
          chip.style.setProperty("--chip-color", label.color);
          chip.setAttribute("aria-pressed", Filter.labels.has(label.id) ? "true" : "false");
          chip.addEventListener("click", () => {
            if (Filter.labels.has(label.id)) Filter.labels.delete(label.id); else Filter.labels.add(label.id);
            this.all();
          });
          strip.append(chip);
        }
      }

      strip.append(el("span", "sep"), el("span", "label", "Milestone"));
      const msSel = el("select");
      msSel.setAttribute("aria-label", "Filter by milestone");
      const opts = [["", "any"], ...Store.data.milestones.map(m => [m.id, m.name]), ["none", "none set"]];
      for (const [value, text] of opts) {
        const o = el("option", null, text);
        o.value = value;
        if (Filter.milestone === value) o.selected = true;
        msSel.append(o);
      }
      msSel.addEventListener("change", () => { Filter.milestone = msSel.value; this.all(); });
      strip.append(msSel);

      strip.append(el("span", "label", "Priority"));
      const priSel = el("select");
      priSel.setAttribute("aria-label", "Filter by priority");
      for (const p of PB.PRIORITIES) {
        const o = el("option", null, p.id === 3 ? "any" : `${p.short} ${p.name} and up`);
        o.value = String(p.id);
        if (Filter.priority === p.id) o.selected = true;
        priSel.append(o);
      }
      priSel.addEventListener("change", () => { Filter.priority = Number(priSel.value); this.all(); });
      strip.append(priSel);

      strip.append(el("span", "sep"));
      const arch = el("button", "chip", "Archive");
      arch.type = "button";
      arch.title = "Show archived cards instead of live ones";
      arch.setAttribute("aria-pressed", Filter.archived ? "true" : "false");
      arch.addEventListener("click", () => { Filter.archived = !Filter.archived; this.all(); });
      strip.append(arch);

      strip.append(el("span", "grow"));
      strip.append(el("span", "label", "Lanes"));
      const grp = el("select");
      grp.setAttribute("aria-label", "Group cards into lanes");
      for (const [value, text] of [["none", "one board"], ["milestone", "by milestone"], ["type", "by type"]]) {
        const o = el("option", null, text);
        o.value = value;
        if (Store.data.settings.groupBy === value) o.selected = true;
        grp.append(o);
      }
      grp.addEventListener("change", () => Store.setSetting("groupBy", grp.value));
      strip.append(grp);

      if (Filter.active()) {
        const clear = el("button", "btn", "Clear filter");
        clear.type = "button";
        clear.addEventListener("click", () => { Filter.clear(); this.all(); });
        strip.append(clear);
      }
    },

    // --------------- status ---------------

    statusEl(shown) {
      const status = document.getElementById("status");
      status.textContent = "";
      const total = Object.values(Store.data.cards).filter(c => !c.archived).length;
      const p = Store.progress();
      const breach = Store.data.columns.filter(c => {
        if (!c.wip) return false;
        const live = c.cards.filter(cid => { const card = Store.card(cid); return card && !card.archived; });
        return live.length > c.wip;
      });

      const bits = [`${shown} shown`, `${total} live`];
      if (p.total) bits.push(`${p.done}/${p.total} pts`);
      status.append(el("span", null, bits.join(" · ")));
      if (breach.length) {
        const warn = el("span", "status warn", ` · over WIP: ${breach.map(c => c.name).join(", ")}`);
        status.append(warn);
      }

      document.getElementById("undoBtn").disabled = !Store.canUndo();
      document.getElementById("redoBtn").disabled = !Store.canRedo();
      const undoTitle = Store.canUndo() ? `Undo ${Store.undoLabel()}` : "Nothing to undo";
      document.getElementById("undoBtn").title = undoTitle;
      document.title = `${Store.data.title} board`;
      const heading = document.getElementById("boardTitle");
      if (heading.textContent !== Store.data.title) heading.textContent = Store.data.title;
    },

    // --------------- the whole view ---------------

    all() {
      const host = document.getElementById("board");
      const scrollTop = host.scrollTop, scrollLeft = host.scrollLeft;

      this.filtersEl();
      const { node, shown } = this.boardEl();
      host.textContent = "";
      host.append(node);
      host.scrollTop = scrollTop;
      host.scrollLeft = scrollLeft;
      this.statusEl(shown);

      if (this.composerColumn) {
        const box = host.querySelector(`.col[data-column-id="${this.composerColumn}"] textarea`);
        if (box) {
          box.focus();
          box.setSelectionRange(box.value.length, box.value.length);
        } else {
          this.composerColumn = null;   // its column was deleted under us
        }
      }
      PB.Dnd.attach(host);
    },
  };

  PB.el = el;
  PB.Filter = Filter;
  PB.Render = Render;
})(window.PB = window.PB || {});
