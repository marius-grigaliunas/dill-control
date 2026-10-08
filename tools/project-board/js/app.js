// Wiring: the top bar, the keyboard, the theme, and writing the board out to the
// project folder.
//
// The write is debounced rather than immediate because dragging a card fires a
// commit per drop and typing a title fires one per keystroke-batch, and each
// write is a file handle, a stream and a flush. Three quarters of a second after
// the last change is far inside the window where a crash could cost anything:
// localStorage has it synchronously either way.
(function (PB) {
  "use strict";

  const Store = PB.Store;
  const Project = PB.Project;
  const Render = PB.Render;
  const el = PB.el;

  let saveTimer = 0;
  let lastWrite = null;
  let toastTimer = 0;

  const App = {
    // --------------- chrome ---------------

    applyTheme() {
      const theme = Store.data.settings.theme;
      if (theme === "system") document.documentElement.removeAttribute("data-theme");
      else document.documentElement.setAttribute("data-theme", theme);
    },

    toast(message, isError) {
      const host = document.getElementById("toast");
      host.textContent = message;
      host.className = isError ? "toast err" : "toast";
      host.hidden = false;
      clearTimeout(toastTimer);
      toastTimer = setTimeout(() => { host.hidden = true; }, isError ? 6000 : 3000);
    },

    // Where the board is being kept, stated in the bar so it is never a guess.
    status() {
      const node = document.getElementById("fileStatus");
      node.textContent = "";
      node.className = "status";
      if (Project.connected) {
        const when = lastWrite ? lastWrite.toTimeString().slice(0, 5) : "…";
        node.append(el("span", null, `${Project.path} · saved `), el("b", null, when));
      } else if (Project.pending && Project.supported) {
        node.append(el("span", null, "browser storage · folder needs a click"));
      } else {
        node.append(el("span", null, "browser storage"));
      }
      document.getElementById("reconnectBtn").hidden = !(Project.pending && !Project.connected);
      document.getElementById("connectBtn").hidden = Project.connected || !Project.supported;
    },

    // --------------- the project file ---------------

    async saveToProject(immediate) {
      if (!Project.connected) return;
      clearTimeout(saveTimer);
      const write = async () => {
        try {
          await Project.writeBoard(Store.serialise());
          lastWrite = new Date();
          Store.dirty = false;
          this.status();
        } catch (e) {
          this.toast(`Could not write ${Project.path}: ${e.message}`, true);
        }
      };
      if (immediate) await write();
      else saveTimer = setTimeout(write, 750);
    },

    // On startup the file in the repo is the source of truth, but it is only
    // *asked about* when it actually differs from what this browser has — which
    // in the ordinary case it does not, because every change was written to both.
    async reconcile() {
      if (!Project.connected) return;
      if (!await Project.exists(Project.path)) { await this.saveToProject(true); return; }
      let raw = null;
      try { raw = await Project.readBoard(); }
      catch (e) { this.toast(`${Project.path} could not be read: ${e.message}`, true); return; }

      const fromFile = JSON.stringify(Store.migrate(raw));
      if (fromFile === JSON.stringify(Store.data)) { lastWrite = new Date(); this.status(); return; }

      const cards = raw && raw.cards ? Object.keys(raw.cards).length : 0;
      const mine = Object.keys(Store.data.cards).length;
      const takeFile = confirm(
        `${Project.path} (${cards} cards) is not the same as this browser's copy (${mine} cards).\n\n`
        + "OK loads the file from the repo.\nCancel keeps what is on screen and overwrites the file.");
      if (takeFile) {
        Store.replace(raw, "load from folder");
        this.toast(`Loaded ${Project.path}.`);
      } else {
        await this.saveToProject(true);
        this.toast(`Overwrote ${Project.path} with the browser copy.`);
      }
    },

    // --------------- keyboard ---------------

    help() {
      const dlg = document.getElementById("smallDlg");
      dlg.textContent = "";
      const shell = el("div", "dlg");
      const head = el("header", "dlg-head");
      head.append(el("h2", null, "Keyboard"));
      shell.append(head);

      const body = el("div", "dlg-body");
      const keys = el("div", "keys");
      const pairs = [
        ["N", "add a card to the first column"],
        ["/", "jump to search"],
        ["Enter", "open the card the keyboard is on"],
        ["Ctrl Z", "undo — including a drag"],
        ["Ctrl Shift Z", "redo"],
        ["Ctrl S", "write the board to the project folder now"],
        ["Ctrl Enter", "save and close the card dialog"],
        ["Esc", "close a dialog, or clear the search"],
        ["?", "this list"],
      ];
      for (const [key, what] of pairs) {
        const kbd = el("kbd", null, key);
        keys.append(kbd, el("span", null, what));
      }
      body.append(keys);
      body.append(el("div", "hint",
        "Cards drag between and within columns. A column's header drags to reorder it, "
        + "on the ungrouped board."));
      shell.append(body);

      const foot = el("footer", "dlg-foot");
      const close = el("button", "btn primary", "Close");
      close.type = "button";
      close.addEventListener("click", () => dlg.close());
      foot.append(close);
      shell.append(foot);
      dlg.append(shell);
      dlg.showModal();
    },

    keydown(ev) {
      const target = ev.target;
      const typing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA"
        || target.tagName === "SELECT" || target.isContentEditable);
      const inDialog = !!document.querySelector("dialog[open]");

      if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === "s") {
        ev.preventDefault();
        if (Project.connected) this.saveToProject(true).then(() => this.toast(`Wrote ${Project.path}.`));
        else PB.download(`${PB.Exports.slug()}_board.json`, PB.Exports.json(), "application/json");
        return;
      }
      if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === "z") {
        if (typing) return;                       // let the field have its own undo
        ev.preventDefault();
        const label = ev.shiftKey ? Store.redo() : Store.undo();
        this.toast(label ? (ev.shiftKey ? `Redid ${label}.` : `Undid ${label}.`)
          : (ev.shiftKey ? "Nothing to redo." : "Nothing to undo."));
        return;
      }
      if (inDialog) return;

      if (ev.key === "Escape") {
        const search = document.getElementById("search");
        if (PB.Filter.text) { PB.Filter.text = ""; search.value = ""; Render.all(); }
        else if (Render.composerColumn) { Render.composerColumn = null; Render.all(); }
        return;
      }
      if (typing) return;

      if (ev.key === "/") { ev.preventDefault(); document.getElementById("search").focus(); return; }
      if (ev.key === "?") { ev.preventDefault(); this.help(); return; }
      if (ev.key.toLowerCase() === "n") {
        ev.preventDefault();
        Render.composerColumn = Store.data.columns[0].id;
        Render.composerText = "";
        Render.all();
        return;
      }
      if (ev.key === "Enter") {
        const card = document.activeElement && document.activeElement.closest
          ? document.activeElement.closest(".card") : null;
        if (card) { ev.preventDefault(); PB.Editor.openCard(card.dataset.cardId); }
      }
    },

    // --------------- start ---------------

    async init() {
      const origin = Store.load();
      this.applyTheme();

      Store.onChange(() => {
        Render.all();
        this.saveToProject(false);
      });

      // Bar
      document.getElementById("search").addEventListener("input", ev => {
        PB.Filter.text = ev.target.value.trim();
        Render.all();
      });
      document.getElementById("addBtn").addEventListener("click", () => {
        Render.composerColumn = Store.data.columns[0].id;
        Render.composerText = "";
        Render.all();
      });
      document.getElementById("addColBtn").addEventListener("click", () => {
        const col = Store.addColumn("New column");
        PB.Editor.openColumn(col.id);
      });
      document.getElementById("undoBtn").addEventListener("click", () => {
        const label = Store.undo();
        if (label) this.toast(`Undid ${label}.`);
      });
      document.getElementById("redoBtn").addEventListener("click", () => {
        const label = Store.redo();
        if (label) this.toast(`Redid ${label}.`);
      });
      document.getElementById("settingsBtn").addEventListener("click", () => PB.Settings.open());
      document.getElementById("helpBtn").addEventListener("click", () => this.help());
      document.getElementById("connectBtn").addEventListener("click", async () => {
        try {
          await Project.connect();
          await this.reconcile();
          this.status();
        } catch (e) {
          if (e && e.name !== "AbortError") this.toast(e.message || String(e), true);
        }
      });
      document.getElementById("reconnectBtn").addEventListener("click", async () => {
        if (await Project.reconnect()) { await this.reconcile(); this.status(); }
        else this.toast("Permission refused, so the board stays in this browser.", true);
      });

      // Cards open on click, from one delegated listener that survives rebuilds.
      document.getElementById("board").addEventListener("click", ev => {
        const card = ev.target.closest(".card");
        if (card) PB.Editor.openCard(card.dataset.cardId);
      });

      document.addEventListener("keydown", ev => this.keydown(ev));
      window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => this.applyTheme());

      Render.all();
      this.status();

      const state = await Project.restore();
      this.status();
      if (state === "granted") await this.reconcile();
      else if (state === "prompt") this.toast("Click Reconnect to write the board into the project folder again.");
      else if (origin === "seed") {
        this.toast("Started a board from this project's own open work. Settings clears it out.");
      }
    },
  };

  PB.App = App;
  window.addEventListener("DOMContentLoaded", () => App.init());
})(window.PB = window.PB || {});
