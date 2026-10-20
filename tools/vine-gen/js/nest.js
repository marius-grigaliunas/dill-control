// The nest: where a colony's vines stand, and whether the grapple can cross it.
//
// Rings round a heart, staggered from ring to ring so the next vine is always
// on a diagonal, with avenues left open from the edge to the heart's court.
// Which kind stands where is the kinds' zones: fruit near the heart, leaves
// at the edge. Every setting is a range, rolled from the archetype's seed.
// The layout is on the plane tangent at the nest's centre, in metres: x east,
// z south, the way Godot lays out a pad (docs/decisions/0020).
(function (VG) {
  "use strict";

  const TAU = Math.PI * 2;

  // GrappleTuning.GrappleRange in resources/player/grapple_tuning_default.tres.
  VG.GRAPPLE_RANGE_M = 140;

  VG.NEST_FIELDS = [
    { group: "Shape", key: "radiusM", label: "Radius", min: 40, max: 320, step: 5, suffix: "m", fallback: [190, 220] },
    { group: "Shape", key: "heartCount", label: "Heart vines", integer: true, min: 1, max: 4, step: 1, fallback: [1, 1] },
    { group: "Shape", key: "courtM", label: "Heart's court", min: 0, max: 80, step: 1, suffix: "m", fallback: [30, 36] },
    { group: "Shape", key: "ringSpacingM", label: "Ring spacing", min: 15, max: 80, step: 1, suffix: "m", fallback: [32, 38] },
    { group: "Shape", key: "vineSpacingM", label: "Vine spacing", min: 12, max: 80, step: 1, suffix: "m", fallback: [30, 36] },
    { group: "Shape", key: "ringFill", label: "Ring fill", min: 0.3, max: 1, step: 0.05, fallback: [0.9, 1] },
    { group: "Shape", key: "jitterM", label: "Jitter", min: 0, max: 15, step: 0.5, suffix: "m", fallback: [1, 2.5] },
    { group: "Shape", key: "faceHeart", label: "Lean to the heart", min: 0, max: 1, step: 0.05, fallback: [0.85, 1] },
    { group: "Avenues", key: "avenues", label: "Avenues", integer: true, min: 0, max: 8, step: 1, fallback: [3, 4] },
    { group: "Avenues", key: "avenueWidthM", label: "Avenue width", min: 0, max: 60, step: 1, suffix: "m", fallback: [26, 32] },
    { group: "Grapple", key: "swingReachM", label: "Swing reach", min: 20, max: 140, step: 5, suffix: "m", fallback: 70, single: true },
    { group: "Grapple", key: "anchorShare", label: "Anchor height", min: 0.3, max: 1, step: 0.05, suffix: "× height", fallback: 0.7, single: true },
  ];

  function num(value, fallback, min, max) {
    let n = Number(value);
    if (!Number.isFinite(n)) n = fallback;
    return Math.min(max, Math.max(min, n));
  }

  VG.clampNest = function (raw) {
    const src = raw || {};
    const out = {};
    for (let i = 0; i < VG.NEST_FIELDS.length; i++) {
      const field = VG.NEST_FIELDS[i];
      if (field.single) {
        out[field.key] = num(src[field.key], field.fallback, field.min, field.max);
        continue;
      }
      const pair = Array.isArray(src[field.key]) ? src[field.key] : field.fallback;
      let lo = num(pair[0], field.fallback[0], field.min, field.max);
      let hi = num(pair[1], lo, field.min, field.max);
      if (field.integer) { lo = Math.round(lo); hi = Math.round(hi); }
      if (hi < lo) { const swap = lo; lo = hi; hi = swap; }
      out[field.key] = [lo, hi];
    }
    return out;
  };

  VG.rollNest = function (nest, seed) {
    const out = {};
    for (let i = 0; i < VG.NEST_FIELDS.length; i++) {
      const field = VG.NEST_FIELDS[i];
      if (field.single) { out[field.key] = nest[field.key]; continue; }
      const t = VG.stream(seed, "nest:" + field.key)();
      const lo = nest[field.key][0];
      const hi = nest[field.key][1];
      out[field.key] = field.integer ? Math.min(hi, lo + Math.floor(t * (hi - lo + 1))) : lo + (hi - lo) * t;
    }
    return out;
  };

  // Kinds whose zone holds this share of the radius, by weight; past every
  // zone, the nearest one.
  function pickKind(kinds, rho, rng) {
    let total = 0;
    const open = [];
    for (let i = 0; i < kinds.length; i++) {
      const k = kinds[i];
      if (rho >= k.zone[0] - 1e-9 && rho <= k.zone[1] + 1e-9 && k.weight > 0) {
        open.push(i);
        total += k.weight;
      }
    }
    const roll = rng();
    if (!open.length) {
      let best = 0;
      let bestD = Infinity;
      for (let i = 0; i < kinds.length; i++) {
        const z = kinds[i].zone;
        const d = rho < z[0] ? z[0] - rho : rho > z[1] ? rho - z[1] : 0;
        if (d < bestD) { bestD = d; best = i; }
      }
      return best;
    }
    let at = roll * total;
    for (let i = 0; i < open.length; i++) {
      at -= kinds[open[i]].weight;
      if (at <= 0) return open[i];
    }
    return open[open.length - 1];
  }

  function angleGap(a, b) {
    let d = (a - b) % TAU;
    if (d > Math.PI) d -= TAU;
    if (d < -Math.PI) d += TAU;
    return d;
  }

  // Where every vine stands and which way it faces. A pure function of the
  // archetype: no vine needs to be grown to lay the nest out.
  //
  // yaw turns the vine's +x (Godot's, the way its lean points) towards the
  // plane's (cos yaw, sin yaw) in x and z. Godot's rotation.y is -yaw.
  // Facing the heart turns each vine's own lean inwards, so the outer rings
  // close over the court like the sides of a basket.
  VG.layoutNest = function (archetype) {
    const seed = archetype.seed;
    const kinds = archetype.kinds;
    const p = VG.rollNest(archetype.nest, seed);
    const rngPick = VG.stream(seed, "nest:pick");
    const rngPool = VG.stream(seed, "nest:pool");
    const rngYaw = VG.stream(seed, "nest:yaw");
    const rngJitter = VG.stream(seed, "nest:jitter");
    const rngFill = VG.stream(seed, "nest:fill");
    const heading = VG.stream(seed, "nest:heading")() * TAU;
    const avenues = [];
    for (let i = 0; i < p.avenues; i++) avenues.push(heading + i * TAU / p.avenues);

    const sites = [];
    function place(x, z, ring, rho) {
      const kind = pickKind(kinds, rho, rngPick);
      const inward = Math.atan2(-z, -x);
      const loose = (rngYaw() - 0.5) * TAU;
      const yaw = ring < 0 ? loose + Math.PI : inward + loose * (1 - p.faceHeart);
      sites.push({
        x: x, z: z, ring: ring, rho: rho,
        kind: kind,
        pool: Math.floor(rngPool() * kinds[kind].pool),
        yaw: ((yaw % TAU) + TAU) % TAU,
      });
    }

    // The heart: one vine at the centre, or a tight cluster round it.
    const cluster = p.heartCount > 1 ? Math.min(14, p.courtM * 0.5) : 0;
    for (let i = 0; i < p.heartCount; i++) {
      const a = heading + (i + 0.5) * TAU / p.heartCount;
      place(Math.cos(a) * cluster, Math.sin(a) * cluster, -1, 0);
    }

    // Rings, staggered half a slot from the one inside.
    const first = Math.max(p.courtM, p.ringSpacingM * 0.75);
    let skipped = 0;
    for (let ring = 0; ; ring++) {
      const r = first + ring * p.ringSpacingM;
      if (r > p.radiusM + 1e-6) break;
      const count = Math.max(3, Math.round(TAU * r / p.vineSpacingM));
      const phase = heading + (ring % 2 === 0 ? 0.5 : 0) * TAU / count;
      for (let s = 0; s < count; s++) {
        const a = phase + s * TAU / count;
        // An avenue is its width across, except near the heart, where that
        // would empty the small inner rings: there it takes at most a third
        // of the angle between avenues, and the inner rings keep a gate.
        let inAvenue = false;
        const cap = avenues.length ? Math.PI / avenues.length * 0.34 : 0;
        for (let k = 0; k < avenues.length; k++) {
          const d = angleGap(a, avenues[k]);
          if (Math.abs(d) < cap && Math.abs(r * Math.sin(d)) < p.avenueWidthM * 0.5 + 2) inAvenue = true;
        }
        // Rolled for every slot, so an avenue or a fill change moves no other vine.
        const fill = rngFill();
        const jr = (rngJitter() - 0.5) * 2 * p.jitterM;
        const ja = (rngJitter() - 0.5) * 2 * p.jitterM / r;
        if (inAvenue || fill > p.ringFill) { skipped++; continue; }
        const rr = r + jr;
        place(Math.cos(a + ja) * rr, Math.sin(a + ja) * rr, ring, Math.min(1, r / p.radiusM));
      }
    }
    return { params: p, heading: heading, avenues: avenues, sites: sites, skipped: skipped };
  };

  // What the nest adds up to, once its pool vines are grown. info(kind, pool)
  // returns that vine's height in metres, pods and voxels, or null if it did not grow.
  VG.nestReport = function (layout, info) {
    const p = layout.params;
    const sites = layout.sites;
    const anchors = [];
    let pods = 0;
    let voxels = 0;
    let missing = 0;
    const used = new Set();
    for (let i = 0; i < sites.length; i++) {
      const site = sites[i];
      const vine = info(site.kind, site.pool);
      if (!vine) { missing++; anchors.push(null); continue; }
      used.add(site.kind + ":" + site.pool);
      pods += vine.pods;
      voxels += vine.voxels;
      anchors.push({ x: site.x, z: site.z, y: vine.height * p.anchorShare, top: vine.height });
    }

    // A link is a swing: from one vine's anchor to the next, no further than
    // the swing reach and never past the grapple's range.
    const reach = Math.min(p.swingReachM, VG.GRAPPLE_RANGE_M);
    const cell = reach;
    const grid = new Map();
    for (let i = 0; i < anchors.length; i++) {
      const a = anchors[i];
      if (!a) continue;
      const key = Math.floor(a.x / cell) + "," + Math.floor(a.z / cell);
      if (!grid.has(key)) grid.set(key, []);
      grid.get(key).push(i);
    }
    const parent = anchors.map(function (_, i) { return i; });
    function find(i) { while (parent[i] !== i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
    const links = [];
    const neighbours = anchors.map(function () { return []; });
    let nearestGap = 0;
    let crowded = Infinity;
    for (let i = 0; i < anchors.length; i++) {
      const a = anchors[i];
      if (!a) continue;
      const cx = Math.floor(a.x / cell);
      const cz = Math.floor(a.z / cell);
      let nearest = Infinity;
      for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) {
        const list = grid.get((cx + dx) + "," + (cz + dz));
        if (!list) continue;
        for (let k = 0; k < list.length; k++) {
          const j = list[k];
          if (j === i) continue;
          const b = anchors[j];
          const d = Math.hypot(a.x - b.x, a.z - b.z);
          if (d < nearest) nearest = d;
          if (d < crowded) crowded = d;
          if (j < i || d > reach) continue;
          links.push([i, j]);
          neighbours[i].push(j);
          neighbours[j].push(i);
          parent[find(i)] = find(j);
        }
      }
      if (Number.isFinite(nearest) && nearest > nearestGap) nearestGap = nearest;
    }

    const sizes = new Map();
    let placed = 0;
    for (let i = 0; i < anchors.length; i++) {
      if (!anchors[i]) continue;
      placed++;
      const root = find(i);
      sizes.set(root, (sizes.get(root) || 0) + 1);
    }
    let largest = 0;
    for (const n of sizes.values()) largest = Math.max(largest, n);
    let isolated = 0;
    for (let i = 0; i < anchors.length; i++) if (anchors[i] && neighbours[i].length === 0) isolated++;

    // Swings from the outer ring to a heart vine, the fewest.
    let outer = -1;
    for (let i = 0; i < sites.length; i++) outer = Math.max(outer, sites[i].ring);
    const hops = anchors.map(function () { return -1; });
    const queue = [];
    for (let i = 0; i < sites.length; i++) {
      if (anchors[i] && sites[i].ring === outer && outer >= 0) { hops[i] = 0; queue.push(i); }
    }
    for (let q = 0; q < queue.length; q++) {
      const i = queue[q];
      for (let k = 0; k < neighbours[i].length; k++) {
        const j = neighbours[i][k];
        if (hops[j] < 0) { hops[j] = hops[i] + 1; queue.push(j); }
      }
    }
    let toHeart = -1;
    for (let i = 0; i < sites.length; i++) {
      if (sites[i].ring === -1 && hops[i] >= 0 && (toHeart < 0 || hops[i] < toHeart)) toHeart = hops[i];
    }

    const warnings = [];
    if (toHeart < 0) warnings.push("The heart cannot be reached by swinging in from the edge. Raise the swing reach or close up the rings.");
    if (isolated > 0) warnings.push(isolated + " vines have no neighbour within " + reach + " m to swing to.");
    if (missing > 0) warnings.push(missing + " sites use a pool vine that did not grow.");
    if (Number.isFinite(crowded) && crowded < 12) warnings.push("Two trunks stand " + crowded.toFixed(1) + " m apart: the arms will tangle. Widen the spacing or lower the jitter.");

    return {
      vines: sites.length,
      placed: placed,
      models: used.size,
      pods: pods,
      voxels: voxels,
      links: links,
      anchors: anchors,
      largestShare: placed ? largest / placed : 0,
      isolated: isolated,
      swingsToHeart: toHeart,
      widestGap: nearestGap,
      nearestTrunks: Number.isFinite(crowded) ? crowded : 0,
      reach: reach,
      warnings: warnings,
    };
  };
})(window.VG = window.VG || {});
