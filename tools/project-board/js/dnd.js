// Drag and drop for cards and for columns, on the browser's own HTML5 drag
// events. Listeners are delegated onto #board once and survive every rebuild,
// which is why attach() is cheap to call after each render.
//
// Two details are load-bearing. The drop index is worked out from an *anchor
// card* rather than from a count of the elements on screen, because with a
// filter active the cards drawn in a column are a subset of the cards in it and
// a positional index would file the card somewhere nobody pointed at. And the
// drag kind is kept in a module variable rather than read back from
// dataTransfer, since browsers deliberately withhold the payload during dragover
// and the drop line has to be drawn before the drop.
(function (PB) {
  "use strict";

  const Store = PB.Store;
  let dragging = null;    // { kind: "card" | "column", id }
  let line = null;        // the insertion marker, while one is shown

  function clearLine() {
    if (line && line.parentNode) line.parentNode.removeChild(line);
    line = null;
    for (const node of document.querySelectorAll(".col.over")) node.classList.remove("over");
  }

  function showLine(list, before) {
    if (!line) {
      line = document.createElement("div");
      line.className = "drop-line";
    }
    if (before) list.insertBefore(line, before);
    else list.append(line);
  }

  // The visible card the pointer is above the top half of. Null means "past the
  // last one", which is an append.
  function anchorAt(list, y) {
    const cards = [...list.querySelectorAll(".card")].filter(c => !c.classList.contains("dragging"));
    for (const node of cards) {
      const box = node.getBoundingClientRect();
      if (y < box.top + box.height / 2) return node;
    }
    return null;
  }

  // What dropping into this lane means for the card, when the board is grouped.
  // Null on an ungrouped board, and null when the card is already in that lane.
  function laneAssign(card, laneKey) {
    if (!laneKey) return null;
    const group = Store.data.settings.groupBy;
    if (group === "milestone") {
      const value = laneKey === "none" ? null : laneKey;
      return card.milestone === value ? null : { milestone: value };
    }
    if (group === "type") return card.type === laneKey ? null : { type: laneKey };
    return null;
  }

  // Where the dragged card lands in the destination column's own array, counted
  // after it has been taken out of wherever it came from. That is the index
  // Store.moveCard wants.
  function indexForAnchor(column, cardId, beforeId) {
    const after = column.cards.filter(x => x !== cardId);
    if (!beforeId) return after.length;
    const at = after.indexOf(beforeId);
    return at < 0 ? after.length : at;
  }

  const Dnd = {
    attach(host) {
      if (host._pbBound) return;
      host._pbBound = true;

      host.addEventListener("dragstart", ev => {
        const card = ev.target.closest(".card");
        if (card) {
          dragging = { kind: "card", id: card.dataset.cardId };
          card.classList.add("dragging");
          ev.dataTransfer.effectAllowed = "move";
          ev.dataTransfer.setData("text/plain", card.dataset.cardId);
          return;
        }
        const head = ev.target.closest(".col-head");
        if (head && head.draggable) {
          const col = head.closest(".col");
          dragging = { kind: "column", id: col.dataset.columnId };
          col.classList.add("dragging");
          ev.dataTransfer.effectAllowed = "move";
          ev.dataTransfer.setData("text/plain", col.dataset.columnId);
          return;
        }
        ev.preventDefault();
      });

      host.addEventListener("dragend", () => {
        dragging = null;
        clearLine();
        for (const node of document.querySelectorAll(".dragging")) node.classList.remove("dragging");
      });

      host.addEventListener("dragover", ev => {
        if (!dragging) return;

        if (dragging.kind === "card") {
          const col = ev.target.closest(".col");
          if (!col) return;
          ev.preventDefault();
          ev.dataTransfer.dropEffect = "move";
          const list = col.querySelector(".col-cards");
          const empty = list.querySelector(".col-empty");
          if (empty) empty.remove();
          clearLine();
          col.classList.add("over");
          showLine(list, anchorAt(list, ev.clientY));
          return;
        }

        if (dragging.kind === "column") {
          const col = ev.target.closest(".col");
          if (!col || col.dataset.columnId === dragging.id) return;
          ev.preventDefault();
          ev.dataTransfer.dropEffect = "move";
          clearLine();
          col.classList.add("over");
        }
      });

      host.addEventListener("dragleave", ev => {
        const col = ev.target.closest(".col");
        if (col && !col.contains(ev.relatedTarget)) col.classList.remove("over");
      });

      host.addEventListener("drop", ev => {
        if (!dragging) return;
        ev.preventDefault();

        if (dragging.kind === "card") {
          const col = ev.target.closest(".col");
          if (!col) { clearLine(); return; }
          const column = Store.column(col.dataset.columnId);
          const list = col.querySelector(".col-cards");
          // The marker is already sitting exactly where the card should go, so
          // the card it precedes is the anchor.
          let beforeId = null;
          if (line && line.parentNode === list) {
            let next = line.nextElementSibling;
            while (next && !next.classList.contains("card")) next = next.nextElementSibling;
            if (next) beforeId = next.dataset.cardId;
          }
          clearLine();
          if (column) {
            const card = Store.card(dragging.id);
            Store.moveCard(dragging.id, column.id,
              indexForAnchor(column, dragging.id, beforeId),
              card ? laneAssign(card, col.dataset.lane) : null);
          }
          dragging = null;
          return;
        }

        if (dragging.kind === "column") {
          const col = ev.target.closest(".col");
          clearLine();
          if (col && col.dataset.columnId !== dragging.id) {
            const target = col.dataset.columnId;
            const order = Store.data.columns.map(c => c.id).filter(x => x !== dragging.id);
            const box = col.getBoundingClientRect();
            const after = ev.clientX > box.left + box.width / 2;
            let at = order.indexOf(target);
            if (after) at += 1;
            Store.moveColumn(dragging.id, at);
          }
          dragging = null;
        }
      });
    },
  };

  // Exposed for tests.html: the filter-proof drop index is the one piece of this
  // file that is arithmetic rather than event plumbing.
  Dnd._indexForAnchor = indexForAnchor;
  Dnd._laneAssign = laneAssign;

  PB.Dnd = Dnd;
})(window.PB = window.PB || {});
