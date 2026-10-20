// Runs the generator checks without a browser: `node run-tests.js` from this
// folder. The page's scripts stay plain browser scripts, because ES modules
// do not load from a file:// page, so they are evaluated against a stub window.
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const files = ["js/vine.js", "js/kinds.js", "js/nest.js", "js/vox.js", "js/tests.js"];
const sandbox = { console, process, TextEncoder, TextDecoder, DataView, Uint8Array, Float32Array, Map, Set, Date, Math, Number, String, Object, JSON };
sandbox.window = sandbox;
sandbox.globalThis = sandbox;
vm.createContext(sandbox);

for (const file of files) {
  const src = fs.readFileSync(path.join(__dirname, file), "utf8");
  try {
    vm.runInContext(src, sandbox, { filename: file });
  } catch (e) {
    console.error(file + " threw while loading: " + (e && e.stack || e));
    process.exit(1);
  }
}

if (sandbox.VG._testFailed) process.exit(1);
