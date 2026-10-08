// The settings dialog: the board's name and theme, the label and milestone
// tables, where the board file lives, and the ways in and out of it.
//
// Labels and milestones are edited live rather than on a Save, because both are
// lists whose rows are independent and each row's own edit is the unit anyone
// would want back.
(function (PB) {
  "use strict";

  const Store = PB.Store;
  const Project = PB.Project;
  const el = PB.el;
  const field = PB.field;
  const input = PB.input;

  const Settings = {
    open(tab) {
      const dlg = document.getElementById("bigDlg");
      dlg.textContent = "";

      const shell = el("div", "dlg");
      const head = el("header", "dlg-head");
      head.append(el("h2", null, "Board settings"));
      shell.append(head);

      const body = el("div", "dlg-body");
      body.append(this.boardSection(), this.labelSection(), this.milestoneSection(),
        this.fileSection(dlg), this.dangerSection(dlg));
      shell.append(body);

      const foot = el("footer", "dlg-foot");
      const close = el("button", "btn primary", "Done");
      close.type = "button";
      close.addEventListener("click", () => dlg.close());
      foot.append(close, el("span", "grow"));
      const help = el("button", "btn", "Keyboard shortcuts");
      help.type = "button";
      help.addEventListener("click", () => { dlg.close(); PB.App.help(); });
      foot.append(help);
      shell.append(foot);

      dlg.append(shell);
      dlg.showModal();
      if (tab === "labels") body.querySelector("[data-section=labels]").scrollIntoView({ block: "start" });
    },

    // --------------- board ---------------

    boardSection() {
      const box = el("fieldset", "fieldset");
      box.append(el("legend", null, "Board"));

      const grid = el("div", "grid");
      const nameIn = input("text", Store.data.title);
      nameIn.addEventListener("change", () => {
        const title = nameIn.value.trim() || "Board";
        Store.snapshot("rename board");
        Store.data.title = title;
        Store.commit("rename board");
      });
      grid.append(field("Name", nameIn));

      const themeSel = PB.select(
        [["system", "follow the system"], ["light", "light"], ["dark", "dark"]],
        Store.data.settings.theme);
      themeSel.addEventListener("change", () => {
        Store.setSetting("theme", themeSel.value);
        PB.App.applyTheme();
      });
      grid.append(field("Theme", themeSel));
      box.append(grid);

      const stats = el("div", "stats");
      const p = Store.progress();
      const live = Object.values(Store.data.cards).filter(c => !c.archived).length;
      const archived = Object.values(Store.data.cards).filter(c => c.archived).length;
      const add = (label, value) => {
        const stat = el("div", "stat");
        stat.append(el("span", null, label), el("b", null, value));
        stats.append(stat);
      };
      add("Live cards", live);
      add("Finished", `${p.doneCards}`);
      add("Points", p.total ? `${p.done}/${p.total}` : "—");
      add("Archived", archived);
      add("Columns", Store.data.columns.length);
      box.append(stats);
      return box;
    },

    // --------------- labels ---------------

    labelSection() {
      const box = el("fieldset", "fieldset");
      box.dataset.section = "labels";
      box.append(el("legend", null, "Labels"));
      const list = el("div", "list");

      for (const label of Store.data.labels) {
        const row = el("div", "list-row");
        const colour = el("input", "swatch-in");
        colour.type = "color";
        colour.value = label.color;
        colour.setAttribute("aria-label", `Colour for ${label.name}`);
        colour.addEventListener("change", () => Store.updateLabel(label.id, { color: colour.value }));

        const holder = el("div", "grow");
        const name = input("text", label.name);
        name.setAttribute("aria-label", "Label name");
        name.addEventListener("change", () => Store.updateLabel(label.id, { name: name.value.trim() || "label" }));
        holder.append(name);

        const used = Object.values(Store.data.cards).filter(c => c.labels.includes(label.id)).length;
        const count = el("span", "meter-text", `${used}`);
        count.title = `On ${used} card${used === 1 ? "" : "s"}`;

        const del = el("button", "btn icon", "✕");
        del.type = "button";
        del.title = "Delete this label";
        del.addEventListener("click", () => {
          if (used && !confirm(`Delete "${label.name}"? It comes off ${used} card${used === 1 ? "" : "s"}.`)) return;
          Store.deleteLabel(label.id);
          this.reopen();
        });

        row.append(colour, holder, count, del);
        list.append(row);
      }

      const add = el("button", "btn", "+ Add label");
      add.type = "button";
      add.addEventListener("click", () => {
        Store.addLabel("new label", "#6f8f7a");
        this.reopen();
      });
      list.append(add);
      box.append(list);
      return box;
    },

    // --------------- milestones ---------------

    milestoneSection() {
      const box = el("fieldset", "fieldset");
      box.append(el("legend", null, "Milestones"));
      const list = el("div", "list");

      for (const ms of Store.data.milestones) {
        const row = el("div", "list-row");
        const holder = el("div", "grow");
        const name = input("text", ms.name);
        name.setAttribute("aria-label", "Milestone name");
        name.addEventListener("change", () => Store.updateMilestone(ms.id, { name: name.value.trim() || "Milestone" }));
        holder.append(name);

        const due = input("date", ms.due);
        due.setAttribute("aria-label", `Due date for ${ms.name}`);
        due.addEventListener("change", () => Store.updateMilestone(ms.id, { due: due.value }));

        const p = Store.progress(ms.id);
        const count = el("span", "meter-text", `${p.doneCards}/${p.totalCards}`);
        count.title = "Cards finished out of cards on this milestone";

        const del = el("button", "btn icon", "✕");
        del.type = "button";
        del.title = "Delete this milestone";
        del.addEventListener("click", () => {
          if (p.totalCards && !confirm(`Delete "${ms.name}"? ${p.totalCards} card${p.totalCards === 1 ? "" : "s"} lose it.`)) return;
          Store.deleteMilestone(ms.id);
          this.reopen();
        });

        row.append(holder, due, count, del);
        list.append(row);
      }

      const add = el("button", "btn", "+ Add milestone");
      add.type = "button";
      add.addEventListener("click", () => {
        Store.addMilestone("New milestone", "");
        this.reopen();
      });
      list.append(add);
      box.append(list);
      return box;
    },

    // --------------- the board file ---------------

    fileSection(dlg) {
      const box = el("fieldset", "fieldset");
      box.append(el("legend", null, "Where the board lives"));

      const state = el("div", "hint");
      const refresh = () => {
        if (!Project.supported) {
          state.innerHTML = "This browser has no directory picker, so the board is kept in "
            + "<b>this browser's storage</b>. Use Export and Import to move it, or open this page in "
            + "Edge or Chrome to write straight into the repo.";
        } else if (Project.connected) {
          state.innerHTML = `Connected to <b>${PB.escapeHtml(Project.name)}</b>. Every change is written to `
            + `<code>${Project.path}</code>, so the board is in the repo and in git.`;
        } else {
          state.innerHTML = "Kept in <b>this browser's storage</b> only. Connect the project folder to "
            + `write the board to <code>${Project.path}</code> instead, where it can be committed.`;
        }
      };
      refresh();
      box.append(state);

      const row = el("div", "pickers");
      if (Project.supported) {
        const connect = el("button", "btn primary", Project.connected ? "Change folder" : "Connect project folder");
        connect.type = "button";
        connect.addEventListener("click", async () => {
          try {
            await Project.connect();
            await PB.App.saveToProject(true);
            refresh();
            PB.App.status();
            PB.App.toast(`Connected. Board written to ${Project.path}.`);
          } catch (e) {
            if (e && e.name !== "AbortError") PB.App.toast(e.message || String(e), true);
          }
        });
        row.append(connect);

        if (Project.connected) {
          const load = el("button", "btn", "Load from folder");
          load.type = "button";
          load.title = "Discard what is in this browser and take the file in the repo";
          load.addEventListener("click", async () => {
            try {
              if (!await Project.exists(Project.path)) { PB.App.toast(`No ${Project.path} yet.`, true); return; }
              const raw = await Project.readBoard();
              const count = raw && raw.cards ? Object.keys(raw.cards).length : 0;
              if (!confirm(`Replace the board on screen with ${Project.path} (${count} cards)?`)) return;
              Store.replace(raw, "load from folder");
              dlg.close();
              PB.App.toast(`Loaded ${Project.path}.`);
            } catch (e) { PB.App.toast(`Could not read ${Project.path}: ${e.message}`, true); }
          });
          const forget = el("button", "btn", "Disconnect");
          forget.type = "button";
          forget.addEventListener("click", async () => {
            await Project.forget();
            refresh();
            PB.App.status();
            this.reopen();
          });
          row.append(load, forget);
        }
      }
      box.append(row);

      const io = el("div", "pickers");
      const expJson = el("button", "btn", "Export JSON");
      expJson.type = "button";
      expJson.addEventListener("click", () =>
        PB.download(`${PB.Exports.slug()}_board.json`, PB.Exports.json(), "application/json"));
      const expMd = el("button", "btn", "Export Markdown");
      expMd.type = "button";
      expMd.addEventListener("click", () =>
        PB.download(`${PB.Exports.slug()}_board.md`, PB.Exports.markdown(), "text/markdown"));
      const expCsv = el("button", "btn", "Export CSV");
      expCsv.type = "button";
      expCsv.addEventListener("click", () =>
        PB.download(`${PB.Exports.slug()}_board.csv`, PB.Exports.csv(), "text/csv"));
      const imp = el("button", "btn", "Import JSON");
      imp.type = "button";
      imp.addEventListener("click", async () => {
        const picked = await PB.pickFile(".json,application/json");
        if (!picked) return;
        try {
          const raw = JSON.parse(picked.text);
          const count = raw && raw.cards ? Object.keys(raw.cards).length : 0;
          if (!confirm(`Replace the board on screen with ${picked.name} (${count} cards)?`)) return;
          Store.replace(raw, "import");
          dlg.close();
          PB.App.toast(`Imported ${picked.name}.`);
        } catch (e) { PB.App.toast(`${picked.name} is not a board file: ${e.message}`, true); }
      });
      io.append(expJson, expMd, expCsv, imp);
      box.append(io);
      box.append(el("div", "hint",
        "Markdown and CSV are reports, not backups — only the JSON loads back in."));
      return box;
    },

    // --------------- destructive ---------------

    dangerSection(dlg) {
      const box = el("fieldset", "fieldset");
      box.append(el("legend", null, "Clearing out"));
      const row = el("div", "pickers");

      const archive = el("button", "btn", "Archive finished cards");
      archive.type = "button";
      archive.addEventListener("click", () => {
        const n = Store.archiveDone();
        PB.App.toast(n ? `Archived ${n} card${n === 1 ? "" : "s"}.` : "Nothing in a finished column.");
        this.reopen();
      });

      const wipe = el("button", "btn danger", "Delete every card");
      wipe.type = "button";
      wipe.addEventListener("click", () => {
        const n = Object.keys(Store.data.cards).length;
        if (!n) { PB.App.toast("The board has no cards."); return; }
        if (!confirm(`Delete all ${n} cards? Columns, labels and milestones stay. Ctrl Z undoes it.`)) return;
        Store.clear();
        dlg.close();
        PB.App.toast(`Deleted ${n} cards. Ctrl Z brings them back.`);
      });

      row.append(archive, wipe);
      box.append(row);
      return box;
    },

    // Redraws the dialog in place, for the rows that add or remove themselves.
    reopen() {
      const dlg = document.getElementById("bigDlg");
      if (dlg.open) dlg.close();
      this.open();
    },
  };

  PB.Settings = Settings;
})(window.PB = window.PB || {});
