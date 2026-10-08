// The card dialog and the column dialog.
//
// A card is edited in a working copy and written back on Save, so the whole
// session — title, labels, every checklist tick — is one entry on the undo stack
// rather than thirty. Cancel genuinely discards, which is only true because
// nothing here touches Store until Save is pressed.
(function (PB) {
  "use strict";

  const Store = PB.Store;
  const el = PB.el;

  function field(labelText, control, hint) {
    const wrap = el("div", "field");
    const label = el("label", null, labelText);
    const id = control.id || `f${Math.random().toString(36).slice(2, 8)}`;
    control.id = id;
    label.setAttribute("for", id);
    wrap.append(label, control);
    if (hint) wrap.append(el("div", "hint", hint));
    return wrap;
  }

  function select(options, value) {
    const sel = el("select");
    for (const [v, text] of options) {
      const o = el("option", null, text);
      o.value = String(v);
      if (String(value) === String(v)) o.selected = true;
      sel.append(o);
    }
    return sel;
  }

  function input(type, value, attrs) {
    const node = el("input");
    node.type = type;
    if (value !== null && value !== undefined) node.value = String(value);
    for (const key of Object.keys(attrs || {})) node.setAttribute(key, attrs[key]);
    return node;
  }

  const Editor = {
    // --------------- card ---------------

    openCard(cardId) {
      const original = Store.card(cardId);
      if (!original) return;
      const dlg = document.getElementById("cardDlg");
      const draft = JSON.parse(JSON.stringify(original));
      const startColumn = Store.columnOf(cardId);
      let column = startColumn ? startColumn.id : Store.data.columns[0].id;

      dlg.textContent = "";
      const shell = el("form", "dlg");
      shell.method = "dialog";

      // ---- head
      const head = el("header", "dlg-head");
      head.append(el("h2", null, "Card"), el("span", "card-id", `#${draft.number}`), el("span", "grow"));
      const archBtn = el("button", "btn", draft.archived ? "Unarchive" : "Archive");
      archBtn.type = "button";
      archBtn.addEventListener("click", () => {
        draft.archived = !draft.archived;
        archBtn.textContent = draft.archived ? "Unarchive" : "Archive";
      });
      head.append(archBtn);
      shell.append(head);

      // ---- body
      const body = el("div", "dlg-body");

      const titleIn = input("text", draft.title, { spellcheck: "true", required: "required" });
      titleIn.style.fontSize = "15px";
      body.append(field("Title", titleIn));

      const bodyIn = el("textarea");
      bodyIn.rows = 7;
      bodyIn.value = draft.body;
      bodyIn.spellcheck = true;
      const preview = el("div", "md");
      preview.hidden = true;
      const bodyField = field("Notes", bodyIn, "Markdown: **bold**, `code`, - lists, > quotes, [links](https://example.com)");
      const previewBtn = el("button", "btn", "Preview");
      previewBtn.type = "button";
      previewBtn.addEventListener("click", () => {
        const showing = preview.hidden;
        preview.innerHTML = showing ? PB.markdown(bodyIn.value) : "";
        preview.hidden = !showing;
        bodyIn.hidden = showing;
        previewBtn.textContent = showing ? "Edit" : "Preview";
      });
      const bodyHead = bodyField.querySelector("label");
      const bodyRow = el("div", "pickers");
      bodyRow.append(previewBtn);
      bodyHead.after(bodyRow);
      bodyField.append(preview);
      body.append(bodyField);

      const grid = el("div", "grid");
      const colSel = select(Store.data.columns.map(c => [c.id, c.name]), column);
      const typeSel = select(PB.TYPES.map(t => [t.id, t.name]), draft.type);
      const priSel = select(PB.PRIORITIES.map(p => [p.id, `${p.short} ${p.name}`]), draft.priority);
      const ptsIn = input("number", Number.isFinite(draft.points) ? draft.points : "", { min: "0", step: "1", placeholder: "—" });
      const msSel = select([["", "none"], ...Store.data.milestones.map(m => [m.id, m.name])], draft.milestone || "");
      const whoIn = input("text", draft.assignee, { placeholder: "unassigned", spellcheck: "false" });
      grid.append(
        field("Column", colSel),
        field("Type", typeSel),
        field("Priority", priSel),
        field("Points", ptsIn),
        field("Milestone", msSel),
        field("Owner", whoIn),
      );
      body.append(grid);

      // ---- labels
      const labelSet = new Set(draft.labels);
      const labelBox = el("fieldset", "fieldset");
      labelBox.append(el("legend", null, "Labels"));
      const pickers = el("div", "pickers");
      if (!Store.data.labels.length) {
        pickers.append(el("span", "hint", "No labels yet — add some in Settings."));
      }
      for (const label of Store.data.labels) {
        const chip = el("button", "chip dot", label.name);
        chip.type = "button";
        chip.style.setProperty("--chip-color", label.color);
        chip.setAttribute("aria-pressed", labelSet.has(label.id) ? "true" : "false");
        chip.addEventListener("click", () => {
          if (labelSet.has(label.id)) labelSet.delete(label.id); else labelSet.add(label.id);
          chip.setAttribute("aria-pressed", labelSet.has(label.id) ? "true" : "false");
        });
        pickers.append(chip);
      }
      labelBox.append(pickers);
      body.append(labelBox);

      // ---- checklist
      const checkBox = el("fieldset", "fieldset");
      const legend = el("legend");
      const legendText = el("span", null, "Checklist");
      legend.append(legendText);
      checkBox.append(legend);
      const list = el("div", "list");
      let dragTaskId = null;

      const renderList = () => {
        list.textContent = "";
        const done = draft.checklist.filter(t => t.done).length;
        legendText.textContent = draft.checklist.length
          ? `Checklist — ${done}/${draft.checklist.length}`
          : "Checklist";

        draft.checklist.forEach((task, index) => {
          const row = el("div", `list-row${task.done ? " done" : ""}`);
          row.draggable = true;
          row.dataset.taskId = task.id;

          const grip = el("span", "drag", "≡");
          grip.title = "Drag to reorder";
          const tick = input("checkbox");
          tick.checked = task.done;
          tick.setAttribute("aria-label", `Done: ${task.text || "task"}`);
          tick.addEventListener("change", () => { task.done = tick.checked; renderList(); });

          const holder = el("div", "grow");
          const text = input("text", task.text, { placeholder: "Step" });
          text.addEventListener("input", () => { task.text = text.value; });
          text.addEventListener("keydown", ev => {
            if (ev.key === "Enter") {
              ev.preventDefault();
              if (index === draft.checklist.length - 1 && task.text.trim()) {
                draft.checklist.push({ id: PB.id("t"), text: "", done: false });
                renderList();
                const boxes = list.querySelectorAll('input[type="text"]');
                boxes[boxes.length - 1].focus();
              }
            }
          });
          holder.append(text);

          const del = el("button", "btn icon", "✕");
          del.type = "button";
          del.title = "Remove this step";
          del.addEventListener("click", () => {
            draft.checklist = draft.checklist.filter(t => t.id !== task.id);
            renderList();
          });

          row.append(grip, tick, holder, del);

          row.addEventListener("dragstart", ev => {
            dragTaskId = task.id;
            ev.dataTransfer.effectAllowed = "move";
            ev.dataTransfer.setData("text/plain", task.id);
          });
          row.addEventListener("dragover", ev => {
            if (!dragTaskId || dragTaskId === task.id) return;
            ev.preventDefault();
            row.classList.add("over");
          });
          row.addEventListener("dragleave", () => row.classList.remove("over"));
          row.addEventListener("drop", ev => {
            ev.preventDefault();
            row.classList.remove("over");
            if (!dragTaskId || dragTaskId === task.id) return;
            const from = draft.checklist.findIndex(t => t.id === dragTaskId);
            const [moved] = draft.checklist.splice(from, 1);
            const to = draft.checklist.findIndex(t => t.id === task.id);
            draft.checklist.splice(to, 0, moved);
            dragTaskId = null;
            renderList();
          });
          row.addEventListener("dragend", () => { dragTaskId = null; row.classList.remove("over"); });

          list.append(row);
        });

        const add = el("button", "btn", "+ Add step");
        add.type = "button";
        add.addEventListener("click", () => {
          draft.checklist.push({ id: PB.id("t"), text: "", done: false });
          renderList();
          const boxes = list.querySelectorAll('input[type="text"]');
          if (boxes.length) boxes[boxes.length - 1].focus();
        });
        list.append(add);
      };
      renderList();
      checkBox.append(list);
      body.append(checkBox);

      body.append(el("div", "hint",
        `Created ${draft.created.slice(0, 16).replace("T", " ")} · last touched ${draft.updated.slice(0, 16).replace("T", " ")}`));
      shell.append(body);

      // ---- foot
      const foot = el("footer", "dlg-foot");
      const save = el("button", "btn primary", "Save");
      save.type = "submit";
      const cancel = el("button", "btn", "Cancel");
      cancel.type = "button";
      cancel.addEventListener("click", () => dlg.close());
      const dupe = el("button", "btn", "Duplicate");
      dupe.type = "button";
      dupe.addEventListener("click", () => {
        const copy = Store.duplicateCard(cardId);
        dlg.close();
        if (copy) this.openCard(copy.id);
      });
      const del = el("button", "btn danger", "Delete");
      del.type = "button";
      del.addEventListener("click", () => {
        if (!confirm(`Delete card #${draft.number} "${draft.title}"? This can be undone with Ctrl Z.`)) return;
        Store.deleteCard(cardId);
        dlg.close();
      });
      foot.append(save, cancel, el("span", "grow"), dupe, del);
      shell.append(foot);

      const commit = () => {
        const points = ptsIn.value.trim() === "" ? null : Math.max(0, Number(ptsIn.value));
        Store.updateCard(cardId, {
          title: titleIn.value.trim() || "Untitled",
          body: bodyIn.value,
          type: typeSel.value,
          priority: Number(priSel.value),
          points: Number.isFinite(points) ? points : null,
          milestone: msSel.value || null,
          assignee: whoIn.value.trim(),
          labels: [...labelSet],
          checklist: draft.checklist.filter(t => t.text.trim()).map(t => ({ ...t, text: t.text.trim() })),
          archived: draft.archived,
        });
        // The column move is a second entry on purpose: "I moved that card" and
        // "I retyped that card" are different things to want back.
        if (colSel.value !== (startColumn ? startColumn.id : null)) {
          Store.moveCard(cardId, colSel.value, Infinity);
        }
      };

      shell.addEventListener("submit", ev => {
        ev.preventDefault();
        commit();
        dlg.close();
      });
      // Ctrl Enter saves from inside the notes box, where Enter has to stay a newline.
      shell.addEventListener("keydown", ev => {
        if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) { ev.preventDefault(); commit(); dlg.close(); }
      });

      dlg.append(shell);
      dlg.showModal();
      titleIn.focus();
      titleIn.select();
    },

    // --------------- column ---------------

    openColumn(colId) {
      const column = Store.column(colId);
      if (!column) return;
      const dlg = document.getElementById("smallDlg");
      dlg.textContent = "";

      const shell = el("form", "dlg");
      shell.method = "dialog";
      const head = el("header", "dlg-head");
      head.append(el("h2", null, "Column"));
      shell.append(head);

      const body = el("div", "dlg-body");
      const nameIn = input("text", column.name, { required: "required" });
      const wipIn = input("number", column.wip || "", { min: "0", step: "1", placeholder: "no limit" });
      body.append(
        field("Name", nameIn),
        field("Work-in-progress limit", wipIn, "The count turns red past this. It warns; it never refuses a card."),
      );

      const doneWrap = el("label", "check");
      const doneIn = input("checkbox");
      doneIn.checked = column.done;
      const doneText = el("span");
      doneText.append(document.createTextNode("Cards here count as finished"));
      doneText.append(el("small", null, "Feeds milestone progress and is what Archive finished collects."));
      doneWrap.append(doneIn, doneText);
      body.append(doneWrap);
      shell.append(body);

      const foot = el("footer", "dlg-foot");
      const save = el("button", "btn primary", "Save");
      save.type = "submit";
      const cancel = el("button", "btn", "Cancel");
      cancel.type = "button";
      cancel.addEventListener("click", () => dlg.close());
      const addLeft = el("button", "btn", "+ Column after");
      addLeft.type = "button";
      addLeft.addEventListener("click", () => {
        const at = Store.data.columns.findIndex(c => c.id === colId) + 1;
        Store.addColumn("New column", at);
        dlg.close();
      });
      const del = el("button", "btn danger", "Delete");
      del.type = "button";
      del.disabled = Store.data.columns.length < 2;
      del.title = del.disabled ? "A board needs at least one column" : "Delete this column";
      del.addEventListener("click", () => {
        const live = column.cards.length;
        const msg = live
          ? `Delete "${column.name}"? Its ${live} card${live === 1 ? "" : "s"} move to the column on its left.`
          : `Delete "${column.name}"?`;
        if (!confirm(msg)) return;
        Store.deleteColumn(colId);
        dlg.close();
      });
      foot.append(save, cancel, el("span", "grow"), addLeft, del);
      shell.append(foot);

      shell.addEventListener("submit", ev => {
        ev.preventDefault();
        const wip = wipIn.value.trim() === "" ? 0 : Math.max(0, Math.floor(Number(wipIn.value)));
        Store.updateColumn(colId, {
          name: nameIn.value.trim() || "Column",
          wip: Number.isFinite(wip) ? wip : 0,
          done: doneIn.checked,
        });
        dlg.close();
      });

      dlg.append(shell);
      dlg.showModal();
      nameIn.focus();
      nameIn.select();
    },
  };

  PB.field = field;
  PB.select = select;
  PB.input = input;
  PB.Editor = Editor;
})(window.PB = window.PB || {});
