// Writes golden/golden.json: what the browser generator grows for a set of
// vines, for the C# port to be checked against (scripts/debug/VineCheck.cs).
// `node make-golden.js` from this folder. Run it again only when vine.js or
// kinds.js changes on purpose.
//
// A vine is kept small: its rolled settings, counts, bounds, foot, pods, a
// hash of its sorted voxels and the voxels per layer, so a mismatch says how
// much and where. `node make-golden.js dump <name>` prints one vine's voxels
// in full, to diff against the port's dump.
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const files = ["js/vine.js", "js/kinds.js", "js/nest.js", "js/vox.js"];
const sandbox = { console, TextEncoder, TextDecoder, DataView, Uint8Array, Float32Array, Map, Set, Math, Number, String, Object, JSON };
sandbox.window = sandbox;
vm.createContext(sandbox);
for (const file of files) vm.runInContext(fs.readFileSync(path.join(__dirname, file), "utf8"), sandbox, { filename: file });
const VG = sandbox.VG;

// FNV-1a over each voxel as x, y, z (two bytes each, little end) and colour.
function voxelHash(voxels) {
  let h = 2166136261;
  function byte(b) { h ^= b & 255; h = Math.imul(h, 16777619); }
  for (const v of voxels) {
    byte(v.x); byte(v.x >> 8); byte(v.y); byte(v.y >> 8); byte(v.z); byte(v.z >> 8); byte(v.c);
  }
  return h >>> 0;
}

function entry(name, params) {
  const vine = VG.generate(params);
  const layers = new Array(vine.bounds.z).fill(0);
  for (const v of vine.voxels) layers[v.z]++;
  return {
    name: name,
    params: vine.params,
    ok: vine.ok,
    counts: vine.counts,
    bounds: vine.bounds,
    foot: vine.foot,
    hash: voxelHash(vine.voxels),
    layers: layers,
    markers: vine.markers.map(function (m) { return [m.name, m.x, m.y, m.z]; }),
    warnings: vine.warnings.length,
    vine: vine,
  };
}

const list = [];
list.push(entry("default", VG.defaults()));
for (const file of ["vine_laden", "vine_leaning", "vine_weeping"]) {
  list.push(entry(file, VG.readPreset(JSON.parse(fs.readFileSync(path.join(__dirname, "presets", file + ".json"), "utf8")))));
}
const arch = VG.defaultArchetype();
for (const kind of arch.kinds) {
  for (let i = 0; i < 3; i++) {
    const seed = VG.poolSeed(arch.seed, kind.name, i);
    list.push(entry(kind.name.toLowerCase() + "_" + seed, VG.rollVine(kind, seed, arch.voxelSize)));
  }
}

if (process.argv[2] === "dump") {
  const one = list.find(function (e) { return e.name === process.argv[3]; });
  if (!one) { console.error("no vine " + process.argv[3]); process.exit(1); }
  for (const v of one.vine.voxels) console.log(v.x + " " + v.y + " " + v.z + " " + v.c);
  process.exit(0);
}

const out = {
  tool: "dill-control-vine-golden",
  version: 1,
  // The pool seeds come from this archetype seed, as kinds.js poolSeed makes them.
  archetypeSeed: arch.seed,
  vines: list.map(function (e) { const copy = Object.assign({}, e); delete copy.vine; return copy; }),
};
fs.mkdirSync(path.join(__dirname, "golden"), { recursive: true });
fs.writeFileSync(path.join(__dirname, "golden", "golden.json"), JSON.stringify(out, null, 1) + "\n");
for (const e of list) {
  console.log(e.name.padEnd(22) + String(e.counts.total).padStart(7) + " voxels "
    + String(e.markers.length).padStart(4) + " pods  " + e.bounds.x + "x" + e.bounds.y + "x" + e.bounds.z
    + "  hash " + e.hash.toString(16));
}
