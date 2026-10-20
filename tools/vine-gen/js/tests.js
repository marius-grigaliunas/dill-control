// Checks that fail quietly: a missing pod, a core that is only paint, a model
// Godot cannot store, a seed that does not come back as the same file.
(function (VG) {
  "use strict";

  const results = [];

  function check(name, cond) {
    results.push({ name: name, ok: !!cond });
    if (!cond) console.error("FAIL  " + name);
  }

  function sameBytes(a, b) {
    if (!a || !b || a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
    return true;
  }

  function readChunks(bytes) {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    function read(at) {
      const id = String.fromCharCode(bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]);
      const content = view.getInt32(at + 4, true);
      const children = view.getInt32(at + 8, true);
      return { id: id, at: at, content: content, start: at + 12, next: at + 12 + content + children };
    }
    const main = read(8);
    const list = [];
    let at = main.start;
    while (at + 12 <= main.next) {
      const chunk = read(at);
      list.push(chunk);
      at = chunk.next;
    }
    return { view: view, list: list };
  }

  const defaults = VG.defaults();
  const t0 = Date.now();
  const vine = VG.generate(defaults);
  const elapsed = Date.now() - t0;
  console.log("generate " + elapsed + " ms, " + vine.counts.total + " voxels, bounds "
    + vine.bounds.x + "x" + vine.bounds.y + "x" + vine.bounds.z
    + " shell " + vine.counts.shell + " core " + vine.counts.core
    + " leaf " + vine.counts.leaf + " tendril " + vine.counts.tendril);

  const podCount = defaults.ringCount * defaults.podsPerRing;
  const lastPod = "@Pod" + podCount;
  check("default vine builds", vine.ok === true && vine.error == null);
  check("default vine has no warnings", vine.warnings.length === 0);

  // The game holds a part up only by faces, and checks the whole vine at its
  // first hit: anything not joined to the ground then falls, shot or not.
  function grounded(v) {
    const at = new Set();
    for (const c of v.voxels) at.add(c.x + "," + c.y + "," + c.z);
    const held = new Set();
    let q = v.voxels.filter(function (c) { return c.z === 0; });
    for (const c of q) held.add(c.x + "," + c.y + "," + c.z);
    while (q.length) {
      const next = [];
      for (const c of q) {
        for (const d of [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]]) {
          const k = (c.x + d[0]) + "," + (c.y + d[1]) + "," + (c.z + d[2]);
          if (at.has(k) && !held.has(k)) {
            held.add(k);
            next.push({ x: c.x + d[0], y: c.y + d[1], z: c.z + d[2] });
          }
        }
      }
      q = next;
    }
    return held.size === v.voxels.length;
  }
  check("every voxel of the default vine reaches the ground face to face", grounded(vine));
  check(podCount + " pod markers", vine.markers.length === podCount && vine.counts.markers === podCount);

  const names = new Set();
  let namesOk = vine.markers.length === podCount;
  for (let i = 0; i < vine.markers.length; i++) {
    const name = vine.markers[i].name;
    if (names.has(name) || name.charAt(0) !== "@") namesOk = false;
    names.add(name);
  }
  check("marker names are unique @ names", namesOk);
  check("first and last pod", names.has("@Pod01") && names.has(lastPod));

  const occupied = new Set();
  let colour255 = false;
  for (let i = 0; i < vine.voxels.length; i++) {
    const voxel = vine.voxels[i];
    occupied.add(voxel.x + "," + voxel.y + "," + voxel.z);
    if (voxel.c === 255 || voxel.c === 0) colour255 = true;
  }
  let markerInside = false;
  for (let i = 0; i < vine.markers.length; i++) {
    const marker = vine.markers[i];
    if (occupied.has(marker.x + "," + marker.y + "," + marker.z)) markerInside = true;
  }
  check("markers are not stalk voxels", !markerInside);
  const podCells = new Set(vine.markers.map(function (m) { return m.x + "," + m.y + "," + m.z; }));
  check("no two pods share a cell", podCells.size === vine.markers.length);
  check("every default pod has a pickle's drop below it",
    vine.counts.podsBlocked === 0 && vine.counts.podsGrounded === 0);
  let lowest = Infinity;
  for (let i = 0; i < vine.markers.length; i++) lowest = Math.min(lowest, vine.markers[i].z * vine.voxelSize);
  check("pods hang above the bare stalk", lowest > defaults.bareStalkM * 0.5);
  check("palette index 255 is unused", !colour255);
  check("a core sits inside the shell", vine.counts.core > 0);

  let coreNearFoot = false;
  let best = Infinity;
  let bestColour = 0;
  for (let i = 0; i < vine.voxels.length; i++) {
    const voxel = vine.voxels[i];
    if (voxel.z < 2 || voxel.z > 10) continue;
    const d = (voxel.x + 0.5 - vine.foot.x) ** 2 + (voxel.y + 0.5 - vine.foot.y) ** 2 + (voxel.z - 4) ** 2;
    if (d < best) {
      best = d;
      bestColour = voxel.c;
    }
  }
  coreNearFoot = bestColour === VG.PALETTE.CORE;
  check("the base interior is core", coreNearFoot);

  let knuckles = vine.rings.length === defaults.ringCount;
  for (let i = 0; i < vine.rings.length; i++) {
    if (!(vine.rings[i].radius > vine.rings[i].midRadius)) knuckles = false;
  }
  check("every knuckle is fatter than the span beside it", knuckles);
  check("pods spiral from ring to ring", vine.rings[0].angle0 !== vine.rings[1].angle0);

  check("fits one MagicaVoxel model",
    vine.bounds.x <= 256 && vine.bounds.y <= 256 && vine.bounds.z <= 256 && vine.bounds.z > 100);

  const again = VG.generate(defaults);
  const bytes = VG.encodeVox(vine);
  const bytesAgain = VG.encodeVox(again);
  check("same seed, same bytes", sameBytes(bytes, bytesAgain));
  check("VOX header and version 150",
    bytes && String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) === "VOX "
    && new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getInt32(4, true) === 150);

  const parsed = readChunks(bytes);
  let xyzi = 0;
  let sizeOk = false;
  for (let i = 0; i < parsed.list.length; i++) {
    const chunk = parsed.list[i];
    if (chunk.id === "SIZE" && !sizeOk) {
      const sx = parsed.view.getInt32(chunk.start, true);
      const sy = parsed.view.getInt32(chunk.start + 4, true);
      const sz = parsed.view.getInt32(chunk.start + 8, true);
      sizeOk = sx === vine.bounds.x && sy === vine.bounds.y && sz === vine.bounds.z;
    }
    if (chunk.id === "XYZI") xyzi += parsed.view.getInt32(chunk.start, true);
  }
  check("stalk SIZE matches the bounds", sizeOk);
  check("XYZI count is the stalk plus one shared marker model", xyzi === vine.counts.total + 1);

  const text = new TextDecoder().decode(bytes);
  check("the stalk is one named object", text.indexOf("Stalk") !== -1);
  check("pod names are in the file", text.indexOf("@Pod01") !== -1 && text.indexOf(lastPod) !== -1);
  check("shell and core are different materials", text.indexOf("_diffuse") !== -1 && text.indexOf("_glass") !== -1);

  const rgbaAt = parsed.list.filter(function (chunk) { return chunk.id === "RGBA"; })[0];
  let pivotEmpty = false;
  if (rgbaAt) {
    const o = rgbaAt.start + 254 * 4;
    pivotEmpty = bytes[o] === 0 && bytes[o + 1] === 0 && bytes[o + 2] === 0 && bytes[o + 3] === 0;
  }
  check("palette index 255 is empty", pivotEmpty);

  const tooFine = VG.generate(Object.assign({}, defaults, { voxelSize: 0.1 }));
  check("0.1 m refuses an 80 m vine", tooFine.ok === false && tooFine.voxels.length === 0);

  const over = VG.generate(Object.assign({}, defaults, { voxelSize: 0.28, heightM: 80, leanDeg: 0, wobbleM: 0, leavesPerRing: 0, tendrils: false }));
  check("a model past 256 is refused", over.ok === false && over.bounds.z > 256);

  // A preset saved before arms existed grows the vine it grew then, give or
  // take the foot, which no longer kinks: 16,360 voxels before that fix.
  const v1 = { seed: 1, heightM: 80, voxelSize: 0.5, ringCount: 20, podsPerRing: 5, podTwistDeg: 36, wobbleM: 4,
    wobbleFrequency: 1.6, leanDeg: 7, helixTurns: 0, trellisRadiusM: 4, baseRadiusM: 3.5, tipRadiusM: 0.8,
    shellVoxels: 2, knuckleBulge: 0.4, knuckleWidth: 3, leavesPerRing: 1, leafSizeM: 5, leafTiltDeg: 28,
    leafDroopDeg: 34, tendrils: true, tendrilCount: 14, tendrilLengthM: 6, tendrilCurl: 2.5 };
  const old = VG.generate(VG.readPreset({ version: 1, params: v1 }));
  check("a version 1 preset grows the same vine",
    old.ok && Math.abs(old.counts.total - 16360) < 16360 * 0.02 && old.markers.length === 100);

  const bare = VG.generate(Object.assign({}, defaults, { armsPerRing: 0, pedicelM: 0 }));
  check("no arms puts the pods on the stalk", bare.ok && bare.markers.length === podCount);

  // Ranges: every roll inside its range, the same seed the same vine, and
  // one range moved leaves every other setting where it was.
  const arch = VG.defaultArchetype();
  let inside = true;
  for (let k = 0; k < arch.kinds.length; k++) {
    const kind = arch.kinds[k];
    const rolls = VG.poolParams(arch, k);
    for (let r = 0; r < rolls.length; r++) {
      for (let f = 0; f < VG.RANGE_FIELDS.length; f++) {
        const field = VG.RANGE_FIELDS[f];
        if (field.type === "bool") continue;
        const v = rolls[r][field.key];
        if (v < kind.ranges[field.key][0] - 1e-9 || v > kind.ranges[field.key][1] + 1e-9) inside = false;
      }
    }
  }
  check("every roll is inside its range", inside);
  check("the same seed rolls the same vine",
    JSON.stringify(VG.poolParams(arch, 1)) === JSON.stringify(VG.poolParams(VG.defaultArchetype(), 1)));
  const wider = VG.clampArchetype(JSON.parse(JSON.stringify(arch)));
  wider.kinds[1].ranges.heightM = [60, 120];
  const before = VG.poolParams(arch, 1)[0];
  const after = VG.poolParams(wider, 1)[0];
  let othersKept = true;
  for (const key in before) if (key !== "heightM" && before[key] !== after[key]) othersKept = false;
  check("widening one range moves no other setting", othersKept && before.heightM !== after.heightM);
  const pool = VG.poolParams(arch, 1);
  check("a pool's vines differ", new Set(pool.map(function (p) { return JSON.stringify(p); })).size === pool.length);

  let leafy = null;
  let fruiting = null;
  for (let k = 0; k < arch.kinds.length; k++) {
    if (arch.kinds[k].name === "Leafy") leafy = VG.generate(VG.poolParams(arch, k)[0]);
    if (arch.kinds[k].name === "Fruiting") fruiting = VG.generate(VG.poolParams(arch, k)[0]);
  }
  check("a leafy vine is mostly leaf, a fruiting one mostly pods",
    leafy && fruiting && leafy.counts.leaf > fruiting.counts.leaf * 2 && fruiting.counts.markers > leafy.counts.markers * 4);

  const v2 = VG.readArchetype({ version: 2, params: Object.assign({}, defaults) });
  check("a single-vine preset loads as one kind that rolls that vine",
    v2.kinds.length === 1 && VG.generate(VG.poolParams(v2, 0)[0]).counts.total === vine.counts.total);
  const round = VG.readArchetype(JSON.parse(VG.archetypeJson(arch)));
  check("an archetype survives saving", JSON.stringify(round) === JSON.stringify(arch));

  // The nest: the heart at the centre, avenues clear, the same seed the same
  // layout, and every vine reachable by swinging once the pools are grown.
  const layout = VG.layoutNest(arch);
  const again2 = VG.layoutNest(VG.defaultArchetype());
  check("the same seed lays out the same nest", JSON.stringify(layout) === JSON.stringify(again2));
  const hearts = layout.sites.filter(function (site) { return site.ring === -1; });
  check("the heart stands at the centre", hearts.length === layout.params.heartCount
    && hearts.every(function (site) { return Math.hypot(site.x, site.z) < 15 && arch.kinds[site.kind].name === "Heart"; }));
  let avenueClear = true;
  for (let i = 0; i < layout.sites.length; i++) {
    const site = layout.sites[i];
    if (site.ring < 0) continue;
    const r = Math.hypot(site.x, site.z);
    for (let a = 0; a < layout.avenues.length; a++) {
      const along = site.x * Math.cos(layout.avenues[a]) + site.z * Math.sin(layout.avenues[a]);
      const across = Math.abs(-site.x * Math.sin(layout.avenues[a]) + site.z * Math.cos(layout.avenues[a]));
      // Near the heart an avenue narrows to a gate; past that it is its full width.
      const open = Math.min(layout.params.avenueWidthM * 0.5, r * Math.sin(Math.PI / layout.avenues.length * 0.34));
      if (along > 0 && across < open - layout.params.jitterM - 0.5 && r > 1) avenueClear = false;
    }
  }
  check("no trunk stands in an avenue", avenueClear && layout.avenues.length === layout.params.avenues);
  let zoned = true;
  for (let i = 0; i < layout.sites.length; i++) {
    const site = layout.sites[i];
    const z = arch.kinds[site.kind].zone;
    if (site.rho < z[0] - 1e-9 || site.rho > z[1] + 1e-9) zoned = false;
  }
  check("every vine stands inside its kind's zone", zoned);
  const fakeHeights = arch.kinds.map(function (kind) { return (kind.ranges.heightM[0] + kind.ranges.heightM[1]) / 2; });
  const report = VG.nestReport(layout, function (k) { return { height: fakeHeights[k], pods: 100, voxels: 10000 }; });
  check("the heart can be reached by swinging in from the edge", report.swingsToHeart > 0 && report.isolated === 0);
  check("the whole nest is one swinging network", report.largestShare === 1);
  check("nest pods add up", report.pods === layout.sites.length * 100);
  const sparse = VG.clampArchetype(JSON.parse(JSON.stringify(arch)));
  sparse.nest.swingReachM = 20;
  check("a reach shorter than the spacing is reported",
    VG.nestReport(VG.layoutNest(sparse), function (k) { return { height: fakeHeights[k], pods: 1, voxels: 1 }; }).warnings.length > 0);

  VG._testResults = results;
  const failed = results.filter(function (item) { return !item.ok; });
  console.log(results.length - failed.length + "/" + results.length + " passed");
  if (failed.length) {
    VG._testFailed = true;
    for (let i = 0; i < failed.length; i++) console.error("  " + failed[i].name);
  }
})(window.VG = window.VG || {});
