// Ways off the board: the JSON that round-trips, a Markdown digest for pasting
// into a commit message or a doc, and a CSV for a spreadsheet.
//
// Only the JSON is the board. The other two are reports and deliberately lossy —
// re-importing one is not supported and is not meant to be.
(function (PB) {
  "use strict";

  const Store = PB.Store;

  function csvCell(value) {
    const s = value === null || value === undefined ? "" : String(value);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  }

  const Exports = {
    json() { return Store.serialise(); },

    // Columns as headings, cards as checkboxes. Done columns tick their boxes, so
    // the digest reads as a status report rather than as a dump of the board.
    markdown() {
      const d = Store.data;
      const out = [`# ${d.title} — board`, "", `_${new Date().toISOString().slice(0, 10)}_`, ""];

      const overall = Store.progress();
      out.push(`**${overall.doneCards}/${overall.totalCards} cards**`
        + (overall.total ? ` · **${overall.done}/${overall.total} points**` : ""), "");

      if (d.milestones.length) {
        out.push("## Milestones", "");
        for (const ms of d.milestones) {
          const p = Store.progress(ms.id);
          const pct = p.total ? Math.round((p.done / p.total) * 100) : 0;
          out.push(`- **${ms.name}**${ms.due ? ` (due ${ms.due})` : ""} — `
            + `${p.doneCards}/${p.totalCards} cards, ${p.done}/${p.total} pts (${pct}%)`);
        }
        out.push("");
      }

      for (const col of d.columns) {
        const cards = col.cards.map(id => Store.card(id)).filter(c => c && !c.archived);
        out.push(`## ${col.name}${col.wip ? ` (WIP ${cards.length}/${col.wip})` : ""}`, "");
        if (!cards.length) { out.push("_empty_", ""); continue; }
        for (const card of cards) {
          const bits = [Store.type(card.type).name];
          if (card.priority <= 1) bits.push(Store.priority(card.priority).short);
          if (Number.isFinite(card.points)) bits.push(`${card.points} pts`);
          const ms = card.milestone ? Store.milestone(card.milestone) : null;
          if (ms) bits.push(ms.name);
          for (const lid of card.labels) {
            const label = Store.label(lid);
            if (label) bits.push(label.name);
          }
          if (card.assignee) bits.push(card.assignee);
          out.push(`- [${col.done ? "x" : " "}] **#${card.number} ${card.title}** — _${bits.join(", ")}_`);
          for (const task of card.checklist) out.push(`  - [${task.done ? "x" : " "}] ${task.text}`);
        }
        out.push("");
      }

      const archived = Object.values(d.cards).filter(c => c.archived);
      if (archived.length) {
        out.push(`## Archived (${archived.length})`, "");
        for (const card of archived) out.push(`- #${card.number} ${card.title}`);
        out.push("");
      }

      return out.join("\n");
    },

    csv() {
      const head = ["number", "title", "column", "type", "priority", "points", "milestone",
        "labels", "owner", "checklist done", "checklist total", "archived", "created", "updated"];
      const rows = [head.join(",")];
      for (const col of Store.data.columns) {
        for (const cid of col.cards) {
          const card = Store.card(cid);
          if (!card) continue;
          const ms = card.milestone ? Store.milestone(card.milestone) : null;
          rows.push([
            card.number,
            card.title,
            col.name,
            Store.type(card.type).name,
            Store.priority(card.priority).short,
            Number.isFinite(card.points) ? card.points : "",
            ms ? ms.name : "",
            card.labels.map(l => (Store.label(l) || {}).name || "").filter(Boolean).join(" "),
            card.assignee,
            card.checklist.filter(t => t.done).length,
            card.checklist.length,
            card.archived ? "yes" : "no",
            card.created.slice(0, 10),
            card.updated.slice(0, 10),
          ].map(csvCell).join(","));
        }
      }
      return rows.join("\n");
    },

    slug() {
      return Store.data.title.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_|_$/g, "") || "board";
    },
  };

  PB.Exports = Exports;
})(window.PB = window.PB || {});
