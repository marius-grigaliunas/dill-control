// A folder the page can write .vox files into, through the File System Access
// API (Edge and Chrome, including from a file:// page). The handle is kept in
// IndexedDB, so the next session only has to confirm permission. Without the
// API, Save downloads the file instead.
(function (VG) {
  "use strict";

  const DB = "dillcontrol-vine-gen";
  const STORE = "handles";
  const KEY = "folder";

  function idb(mode, fn) {
    return new Promise(function (resolve, reject) {
      const open = indexedDB.open(DB, 1);
      open.onupgradeneeded = function () { open.result.createObjectStore(STORE); };
      open.onerror = function () { reject(open.error); };
      open.onsuccess = function () {
        const tx = open.result.transaction(STORE, mode);
        const req = fn(tx.objectStore(STORE));
        tx.oncomplete = function () { resolve(req && req.result); };
        tx.onerror = function () { reject(tx.error); };
      };
    });
  }

  VG.files = {
    handle: null,
    pending: null,
    supported: typeof window.showDirectoryPicker === "function",

    get connected() { return !!this.handle; },
    get name() { return this.handle ? this.handle.name : ""; },

    async connect() {
      const handle = await window.showDirectoryPicker({ id: "dillcontrol-vine-gen", mode: "readwrite" });
      this.handle = handle;
      try { await idb("readwrite", function (store) { return store.put(handle, KEY); }); } catch (e) { /* this session only */ }
      return handle;
    },

    async restore() {
      if (!this.supported) return "none";
      let handle = null;
      try { handle = await idb("readonly", function (store) { return store.get(KEY); }); } catch (e) { return "none"; }
      if (!handle) return "none";
      this.pending = handle;
      const state = await handle.queryPermission({ mode: "readwrite" });
      if (state === "granted") {
        this.handle = handle;
        return "granted";
      }
      return "prompt";
    },

    async reconnect() {
      const handle = this.pending;
      if (!handle) return false;
      if (await handle.requestPermission({ mode: "readwrite" }) === "granted") {
        this.handle = handle;
        return true;
      }
      return false;
    },

    async write(name, data) {
      if (!this.handle) throw new Error("No folder connected.");
      const file = await this.handle.getFileHandle(name, { create: true });
      const stream = await file.createWritable();
      await stream.write(data);
      await stream.close();
    },

    download(name, data, mime) {
      const blob = data instanceof Blob ? data : new Blob([data], { type: mime || "application/octet-stream" });
      const link = document.createElement("a");
      link.href = URL.createObjectURL(blob);
      link.download = name;
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(function () { URL.revokeObjectURL(link.href); }, 1500);
    },
  };
})(window.VG = window.VG || {});
