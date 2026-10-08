// Runs tests.html's suite without a browser: `node run-tests.js` from this
// folder. Exits non-zero on a failure, so it is usable from a hook or a script.
//
// The board's scripts are plain browser scripts on purpose — ES modules cannot
// load from a file:// page, which is how this tool is meant to be opened — so
// there is nothing to require. They are read and evaluated against a stub window
// instead, which is also a useful check in itself: anything that reaches for the
// DOM at load time rather than inside a handler fails here.
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const files = [
  "js/seed.js", "js/store.js", "js/md.js", "js/project.js", "js/exports.js",
  "js/dnd.js", "js/render.js", "js/tests.js",
];

const sandbox = {
  console,
  process,
  setTimeout,
  clearTimeout,
  indexedDB: undefined,
  document: undefined,       // no DOM: tests.js prints to the console instead
  matchMedia: undefined,
};
sandbox.window = sandbox;
sandbox.globalThis = sandbox;
vm.createContext(sandbox);

for (const file of files) {
  const src = fs.readFileSync(path.join(__dirname, file), "utf8");
  try {
    vm.runInContext(src, sandbox, { filename: file });
  } catch (e) {
    console.error(`${file} threw while loading: ${e.message}`);
    process.exit(1);
  }
}

const report = sandbox.PB && sandbox.PB.testReport;
if (!report) {
  console.error("tests.js produced no report");
  process.exit(1);
}
process.exit(report.failed ? 1 : 0);
