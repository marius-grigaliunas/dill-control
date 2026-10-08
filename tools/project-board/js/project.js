// The project folder, reached through the File System Access API (Edge and
// Chrome, including from a file:// page). Same arrangement as
// tools/voxel-bench/js/project.js, and for the same reason: the folder handle is
// kept in IndexedDB, so after the first pick a new session only has to confirm
// permission with one click.
//
// Why this exists at all when localStorage already works: a board held only in a
// browser profile is not in the repo, is not in git and is gone with the
// profile. Connecting the folder makes docs/project_board.json the real board
// and localStorage the working copy.
(function (PB) {
  "use strict";

  const DB = "dillcontrol-project-board", STORE = "handles", KEY = "project";
  const BOARD_PATH = "docs/project_board.json";

  function idb(mode, fn) {
    return new Promise((resolve, reject) => {
      const open = indexedDB.open(DB, 1);
      open.onupgradeneeded = () => open.result.createObjectStore(STORE);
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const tx = open.result.transaction(STORE, mode);
        const req = fn(tx.objectStore(STORE));
        tx.oncomplete = () => resolve(req && req.result);
        tx.onerror = () => reject(tx.error);
      };
    });
  }

  const Project = {
    handle: null,
    pending: null,
    path: BOARD_PATH,
    supported: typeof window.showDirectoryPicker === "function",

    get connected() { return !!this.handle; },
    get name() { return this.handle ? this.handle.name : ""; },

    // Asks for the folder and checks it is a Godot project, so a mis-picked
    // folder fails here rather than by writing a board into someone's Downloads.
    async connect() {
      const handle = await window.showDirectoryPicker({ id: "dillcontrol-project", mode: "readwrite" });
      try { await handle.getFileHandle("project.godot"); }
      catch (e) { throw new Error(`"${handle.name}" has no project.godot. Pick the folder that contains it.`); }
      this.handle = handle;
      try { await idb("readwrite", s => s.put(handle, KEY)); } catch (e) { /* this session only */ }
      return handle;
    },

    // Returns "granted", "prompt" (needs a click on Reconnect) or "none".
    async restore() {
      if (!this.supported) return "none";
      let handle = null;
      try { handle = await idb("readonly", s => s.get(KEY)); } catch (e) { return "none"; }
      if (!handle) return "none";
      this.pending = handle;
      const state = await handle.queryPermission({ mode: "readwrite" });
      if (state === "granted") { this.handle = handle; return "granted"; }
      return "prompt";
    },

    // Must run inside a click handler: the browser will not prompt otherwise.
    async reconnect() {
      const handle = this.pending;
      if (!handle) return false;
      if (await handle.requestPermission({ mode: "readwrite" }) === "granted") { this.handle = handle; return true; }
      return false;
    },

    async forget() {
      this.handle = null;
      this.pending = null;
      try { await idb("readwrite", s => s.delete(KEY)); } catch (e) { /* nothing to forget */ }
    },

    split(path) {
      const clean = path.replace(/^res:\/\//, "");
      const i = clean.lastIndexOf("/");
      return { dir: i >= 0 ? clean.slice(0, i) : "", file: clean.slice(i + 1) };
    },

    async dirFor(path, create) {
      let dir = this.handle;
      const parts = path.replace(/^res:\/\//, "").split("/").filter(Boolean);
      for (const part of parts) dir = await dir.getDirectoryHandle(part, { create: !!create });
      return dir;
    },

    async exists(path) {
      if (!this.handle) return false;
      try { const { dir, file } = this.split(path); await (await this.dirFor(dir)).getFileHandle(file); return true; }
      catch (e) { return false; }
    },

    async readText(path) {
      const { dir, file } = this.split(path);
      const fh = await (await this.dirFor(dir)).getFileHandle(file);
      return (await fh.getFile()).text();
    },

    async writeText(path, text) {
      const { dir, file } = this.split(path);
      const fh = await (await this.dirFor(dir, true)).getFileHandle(file, { create: true });
      const w = await fh.createWritable();
      await w.write(text);
      await w.close();
    },

    async readBoard() { return JSON.parse(await this.readText(this.path)); },
    async writeBoard(json) { await this.writeText(this.path, json); },
  };

  // Offers a file for download when there is no folder to write into. This is
  // the whole fallback for Firefox and Safari, which have no directory picker.
  PB.download = function (name, text, type) {
    const blob = new Blob([text], { type: type || "application/json" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  };

  // Reads one picked file as text, through the plain <input type=file> that
  // works everywhere.
  PB.pickFile = function (accept) {
    return new Promise(resolve => {
      const input = document.createElement("input");
      input.type = "file";
      if (accept) input.accept = accept;
      input.addEventListener("change", () => {
        const file = input.files && input.files[0];
        if (!file) { resolve(null); return; }
        const reader = new FileReader();
        reader.onload = () => resolve({ name: file.name, text: String(reader.result) });
        reader.onerror = () => resolve(null);
        reader.readAsText(file);
      });
      input.click();
    });
  };

  PB.Project = Project;
})(window.PB = window.PB || {});
