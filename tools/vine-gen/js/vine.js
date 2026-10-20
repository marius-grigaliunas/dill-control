// A giant pickle vine as voxels, in MagicaVoxel space: X and Y across, Z up.
//
// The whole shape is one object. Godot turns each MagicaVoxel object into its
// own destructible part, and a static part is held by the bottom layer of its
// own grid, so a stalk split between rings would leave the top standing after
// the base was cut. Pods are markers, not voxels: the fruit is instanced in
// the game. See docs/art-to-game.md and docs/decisions/0024, 0029.
//
// Pure data in, pure data out. Nothing here touches the DOM, so node can run
// the checks in run-tests.js.
(function (VG) {
  "use strict";

  const TAU = Math.PI * 2;
  // prp_tree_tall_a, the vine's budget to be judged against (docs/decisions/0024).
  VG.TREE_VOXELS = 30352;
  // XYZI stores each coordinate in a byte.
  VG.AXIS_LIMIT = 256;
  VG.PLANET_RADIUS = 1200;
  // HordeStats' agent height: a hanging pickle needs this much clear air below its pod.
  VG.PICKLE_HEIGHT_M = 1.8;
  // Closer than this and two hanging pickles overlap.
  VG.POD_SPACING_M = 0.9;

  VG.PALETTE = {
    SHELL: 1,
    KNUCKLE: 2,
    CORE: 3,
    LEAF: 4,
    PETIOLE: 5,
    TENDRIL: 6,
  };

  // sRGB, the bytes written into the .vox RGBA chunk. The preview uses the same.
  VG.PALETTE_RGB = {
    1: [142, 184, 58],
    2: [78, 122, 38],
    3: [232, 214, 150],
    4: [52, 118, 42],
    5: [40, 86, 32],
    6: [198, 210, 112],
  };

  // Shell and core must differ: a chunk graph never crosses a material type
  // (VoxChunker.KindOf). Trees do not use that graph today; the colours are
  // what a carve reveals. Leaves and tendrils stay with the shell so they
  // remain connected to it.
  VG.MATERIAL = {
    1: "_diffuse",
    2: "_diffuse",
    3: "_glass",
    4: "_diffuse",
    5: "_diffuse",
    6: "_diffuse",
  };

  VG.FIELDS = [
    { group: "Vine", key: "seed", label: "Seed", integer: true, min: 0, max: 9999999, step: 1, fallback: 1 },
    { group: "Vine", key: "heightM", label: "Height", min: 16, max: 120, step: 1, suffix: "m", fallback: 80 },
    { group: "Vine", key: "voxelSize", label: "Voxel size", min: 0.1, max: 1, step: 0.05, suffix: "m", fallback: 0.5, global: true },
    { group: "Vine", key: "ringCount", label: "Growth rings", integer: true, min: 1, max: 40, step: 1, fallback: 10 },
    { group: "Vine", key: "bareStalkM", label: "Bare stalk", min: 0, max: 60, step: 0.5, suffix: "m", fallback: 24 },
    { group: "Vine", key: "wobbleM", label: "Wobble", min: 0, max: 24, step: 0.5, suffix: "m", fallback: 2 },
    { group: "Vine", key: "wobbleFrequency", label: "Wobble waves", min: 0.5, max: 8, step: 0.1, fallback: 1.6 },
    { group: "Vine", key: "leanDeg", label: "Lean", min: -45, max: 45, step: 1, suffix: "°", fallback: 7 },
    { group: "Vine", key: "helixTurns", label: "Helix turns", min: 0, max: 3, step: 0.05, fallback: 0 },
    { group: "Vine", key: "trellisRadiusM", label: "Trellis radius", min: 0, max: 16, step: 0.5, suffix: "m", fallback: 4 },
    { group: "Vine", key: "tipNodDeg", label: "Tip nod", min: 0, max: 150, step: 1, suffix: "°", fallback: 30 },
    { group: "Vine", key: "irregularity", label: "Irregularity", min: 0, max: 1, step: 0.05, fallback: 0.6 },

    { group: "Stalk", key: "baseRadiusM", label: "Base radius", min: 0.5, max: 8, step: 0.1, suffix: "m", fallback: 2.8 },
    { group: "Stalk", key: "tipRadiusM", label: "Tip radius", min: 0.3, max: 4, step: 0.1, suffix: "m", fallback: 1 },
    { group: "Stalk", key: "shellVoxels", label: "Shell thickness", integer: true, min: 1, max: 6, step: 1, suffix: "voxels", fallback: 2 },
    { group: "Stalk", key: "knuckleBulge", label: "Knuckle bulge", min: 0, max: 1, step: 0.05, fallback: 0.4 },
    { group: "Stalk", key: "knuckleWidth", label: "Knuckle width", integer: true, min: 1, max: 12, step: 1, suffix: "voxels", fallback: 3 },

    { group: "Arms", key: "armsPerRing", label: "Arms per ring", integer: true, min: 0, max: 4, step: 1, fallback: 4 },
    { group: "Arms", key: "armLengthM", label: "Arm length", min: 2, max: 24, step: 0.5, suffix: "m", fallback: 18 },
    { group: "Arms", key: "armRiseDeg", label: "Arm rise", min: -30, max: 70, step: 1, suffix: "°", fallback: 40 },
    { group: "Arms", key: "armSag", label: "Arm sag", min: 0, max: 1.5, step: 0.05, fallback: 1.4 },
    { group: "Arms", key: "armThickness", label: "Arm thickness", min: 0.15, max: 0.8, step: 0.05, fallback: 0.4 },
    { group: "Arms", key: "armTipLeaf", label: "Leaf at arm tips", type: "bool", fallback: true },

    { group: "Pods", key: "podsPerRing", label: "Pods per ring", integer: true, min: 1, max: 30, step: 1, fallback: 16 },
    { group: "Pods", key: "podTwistDeg", label: "Pod twist", min: 0, max: 180, step: 1, suffix: "°", fallback: 40 },
    { group: "Pods", key: "pedicelM", label: "Pod stem", min: 0, max: 4, step: 0.25, suffix: "m", fallback: 2 },

    { group: "Leaves", key: "leavesPerRing", label: "Leaves per ring", integer: true, min: 0, max: 2, step: 1, fallback: 0 },
    { group: "Leaves", key: "leavesPerArm", label: "Leaves per arm", integer: true, min: 0, max: 6, step: 1, fallback: 0 },
    { group: "Leaves", key: "leafSizeM", label: "Leaf length", min: 1, max: 24, step: 0.5, suffix: "m", fallback: 5 },
    { group: "Leaves", key: "leafTiltDeg", label: "Leaf tilt", min: 0, max: 80, step: 1, suffix: "°", fallback: 28 },
    { group: "Leaves", key: "leafDroopDeg", label: "Leaf droop", min: 0, max: 80, step: 1, suffix: "°", fallback: 34 },

    { group: "Tendrils", key: "tendrils", label: "Tendrils", type: "bool", fallback: true },
    { group: "Tendrils", key: "tendrilCount", label: "Tendril count", integer: true, min: 0, max: 40, step: 1, fallback: 14 },
    { group: "Tendrils", key: "tendrilLengthM", label: "Tendril length", min: 1, max: 16, step: 0.5, suffix: "m", fallback: 6 },
    { group: "Tendrils", key: "tendrilCurl", label: "Tendril curl", min: 0, max: 6, step: 0.1, suffix: "turns", fallback: 2.5 },

  ];

  function num(value, fallback, min, max, integer) {
    let n = Number(value);
    if (!Number.isFinite(n)) n = fallback;
    if (integer) n = Math.round(n);
    if (n < min) n = min;
    if (n > max) n = max;
    return n;
  }

  // Version 1 presets were saved before arms, stems, the bare stalk, the nod
  // and irregularity. Missing, they mean off, so an old preset grows the same vine.
  VG.PRESET_VERSION = 2;
  const V1_OFF = { bareStalkM: 0, tipNodDeg: 0, irregularity: 0, armsPerRing: 0, pedicelM: 0, leavesPerRing: 1, ringCount: 20, wobbleM: 4, baseRadiusM: 3.5, tipRadiusM: 0.8, podsPerRing: 5, podTwistDeg: 36 };

  VG.readPreset = function (data) {
    const params = Object.assign({}, data.params);
    if (!(data.version >= 2)) {
      for (const key in V1_OFF) if (params[key] == null) params[key] = V1_OFF[key];
    }
    return VG.clampParams(params);
  };

  VG.defaults = function () {
    return VG.clampParams({});
  };

  VG.clampParams = function (raw) {
    const src = raw || {};
    const out = {};
    for (let i = 0; i < VG.FIELDS.length; i++) {
      const field = VG.FIELDS[i];
      if (field.type === "bool") {
        out[field.key] = src[field.key] == null ? field.fallback : !!src[field.key];
      } else {
        out[field.key] = num(src[field.key], field.fallback, field.min, field.max, field.integer);
      }
    }
    return out;
  };

  // mulberry32. Named streams so a leaf tweak cannot reshuffle the stalk.
  function hashName(name) {
    let h = 2166136261;
    for (let i = 0; i < name.length; i++) {
      h ^= name.charCodeAt(i);
      h = Math.imul(h, 16777619);
    }
    return h >>> 0;
  }

  function mulberry32(seed) {
    let a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function stream(seed, name) {
    return mulberry32((seed ^ hashName(name)) >>> 0);
  }

  VG.stream = stream;
  VG.hashName = hashName;

  function noise1D(rng) {
    const table = new Float32Array(256);
    for (let i = 0; i < 256; i++) table[i] = rng() * 2 - 1;
    return function (x) {
      const i = Math.floor(x);
      const f = x - i;
      const u = f * f * (3 - 2 * f);
      const a = table[i & 255];
      const b = table[(i + 1) & 255];
      return a + (b - a) * u;
    };
  }

  function add(a, b) { return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z }; }
  function sub(a, b) { return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }; }
  function scale(a, s) { return { x: a.x * s, y: a.y * s, z: a.z * s }; }
  function dot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
  function cross(a, b) {
    return {
      x: a.y * b.z - a.z * b.y,
      y: a.z * b.x - a.x * b.z,
      z: a.x * b.y - a.y * b.x,
    };
  }
  function len(a) { return Math.hypot(a.x, a.y, a.z); }
  function norm(a) {
    const l = len(a);
    return l < 1e-8 ? { x: 0, y: 0, z: 1 } : scale(a, 1 / l);
  }

  function rotateAround(v, axis, ang) {
    const c = Math.cos(ang);
    const s = Math.sin(ang);
    const k = dot(axis, v);
    const cr = cross(axis, v);
    return {
      x: v.x * c + cr.x * s + axis.x * k * (1 - c),
      y: v.y * c + cr.y * s + axis.y * k * (1 - c),
      z: v.z * c + cr.z * s + axis.z * k * (1 - c),
    };
  }

  // The wobble comes in over at least three times its own size, so the foot
  // leans into it instead of kinking sideways.
  function fadeIn(t, span) {
    const f = Math.min(1, t / span);
    return f * f * (3 - 2 * f);
  }

  // A packed key. Coordinates outside ±4096 are rejected before this is used.
  const OFF = 4096;
  const SPAN = 8192;
  function keyOf(x, y, z) {
    return (x + OFF) + (y + OFF) * SPAN + (z + OFF) * SPAN * SPAN;
  }

  function emptyResult(params, error) {
    return {
      ok: false,
      error: error,
      warnings: [],
      params: params,
      voxelSize: params.voxelSize,
      voxels: [],
      markers: [],
      rings: [],
      counts: { shell: 0, core: 0, leaf: 0, tendril: 0, knuckle: 0, total: 0, markers: 0 },
      bounds: { x: 0, y: 0, z: 0 },
      metres: { x: 0, y: 0, z: 0, height: params.heightM },
      foot: { x: 0, y: 0, z: 0 },
    };
  }

  VG.generate = function (raw) {
    const p = VG.clampParams(raw);
    const vs = p.voxelSize;
    const heightV = p.heightM / vs;
    const baseV = p.baseRadiusM / vs;
    const tipV = p.tipRadiusM / vs;
    // Past this the fill is a multi-second scan. 256 is checked after the
    // shape exists, so a vine a little over the limit can still be looked at.
    if (heightV > 420 || baseV > 32) {
      return emptyResult(p,
        "Too many voxels to build at this size. Raise voxel size or lower the height. An 80 m vine fits one model at 0.5 m per voxel; MagicaVoxel stops at 256 on each axis.");
    }

    const samples = buildSamples(p, heightV, baseV, tipV);
    const cells = fillStalk(samples, p.shellVoxels, p.knuckleWidth);
    if (cells.overflow) {
      return emptyResult(p, "The vine left the range the generator can place. Lower the wobble, lean, or helix.");
    }
    const arms = addArms(cells, samples, p);
    if (cells.overflow) {
      return emptyResult(p, "The vine left the range the generator can place. Lower the arm length, wobble, lean, or helix.");
    }
    const pods = hangPods(cells, samples, arms, p);
    addLeaves(cells, samples, arms, p);
    if (p.tendrils && p.tendrilCount > 0) addTendrils(cells, samples, arms, p);

    const packed = pack(cells, samples, pods, p);
    if (packed.overflow) {
      return emptyResult(p, "The vine left the range the generator can place. Lower the wobble, lean, or helix.");
    }
    return packed;
  };

  function buildSamples(p, heightV, baseV, tipV) {
    const rng = stream(p.seed, "centerline");
    const noiseX = noise1D(rng);
    const noiseY = noise1D(rng);
    const lean = Math.tan(p.leanDeg * Math.PI / 180);
    const radius = p.helixTurns === 0 ? 0 : p.trellisRadiusM / p.voxelSize;
    const wobble = p.wobbleM / p.voxelSize;
    const steps = Math.max(16, Math.ceil(heightV * 4));
    const fadeSpan = Math.min(0.6, Math.max(0.07, wobble * 3 / heightV));
    const raw = new Array(steps + 1);
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const z = t * heightV;
      const fade = fadeIn(t, fadeSpan);
      const theta = p.helixTurns * TAU * t;
      let x = radius * (Math.cos(theta) - 1);
      let y = radius * Math.sin(theta);
      x += fade * wobble * noiseX(t * p.wobbleFrequency);
      y += fade * wobble * noiseY(t * p.wobbleFrequency + 19);
      x += z * lean;
      raw[i] = { x: x, y: y, z: z };
    }
    nod(raw, p);

    const resampled = resample(raw, 1);
    const pts = resampled.points;
    const total = resampled.length;
    const tangents = new Array(pts.length);
    for (let i = 0; i < pts.length; i++) {
      const a = pts[Math.max(0, i - 1)];
      const b = pts[Math.min(pts.length - 1, i + 1)];
      tangents[i] = norm(sub(b, a));
    }

    let normal = { x: 1, y: 0, z: 0 };
    normal = sub(normal, scale(tangents[0], dot(normal, tangents[0])));
    if (len(normal) < 1e-6) normal = { x: 0, y: 1, z: 0 };
    normal = norm(normal);

    const bare = Math.min(total * 0.8, p.bareStalkM / p.voxelSize);
    const rings = placeRings(p.ringCount, bare, total, stream(p.seed, "rings"), p.irregularity);
    const samples = new Array(pts.length);
    for (let i = 0; i < pts.length; i++) {
      if (i > 0) {
        const axis = cross(tangents[i - 1], tangents[i]);
        const s = len(axis);
        const c = Math.max(-1, Math.min(1, dot(tangents[i - 1], tangents[i])));
        if (s > 1e-6) normal = rotateAround(normal, scale(axis, 1 / s), Math.atan2(s, c));
        normal = sub(normal, scale(tangents[i], dot(normal, tangents[i])));
        normal = len(normal) < 1e-6 ? { x: 1, y: 0, z: 0 } : norm(normal);
      }
      const T = tangents[i];
      const N = normal;
      const B = norm(cross(T, N));
      const arc = pts[i].arc;
      samples[i] = {
        x: pts[i].x, y: pts[i].y, z: pts[i].z,
        arc: arc,
        radius: radiusAt(arc, total, baseV, tipV, rings, p.knuckleBulge, p.knuckleWidth),
        T: T, N: N, B: B,
      };
    }

    for (let i = 0; i < rings.length; i++) {
      rings[i].sample = nearestSample(samples, rings[i].arc);
      rings[i].radius = radiusAt(rings[i].arc, total, baseV, tipV, rings, p.knuckleBulge, p.knuckleWidth);
      const midArc = i < rings.length - 1
        ? (rings[i].arc + rings[i + 1].arc) * 0.5
        : (rings.length === 1 ? total * 0.5 : (rings[i].arc + rings[i - 1].arc) * 0.5);
      rings[i].midRadius = radiusAt(midArc, total, baseV, tipV, rings, p.knuckleBulge, p.knuckleWidth);
      rings[i].angle0 = (i * p.podTwistDeg) % 360;
    }

    samples.total = total;
    samples.rings = rings;
    samples.baseV = baseV;
    samples.tipV = tipV;
    return samples;
  }

  function resample(raw, spacing) {
    const seg = new Array(raw.length - 1);
    let total = 0;
    for (let i = 1; i < raw.length; i++) {
      seg[i - 1] = len(sub(raw[i], raw[i - 1]));
      total += seg[i - 1];
    }
    if (total < 1) {
      return { points: [{ x: raw[0].x, y: raw[0].y, z: raw[0].z, arc: 0 }], length: total };
    }
    const points = [{ x: raw[0].x, y: raw[0].y, z: raw[0].z, arc: 0 }];
    let base = 0;
    let si = 0;
    for (let target = spacing; target < total - spacing * 0.25; target += spacing) {
      while (si < seg.length - 1 && base + seg[si] < target) {
        base += seg[si];
        si++;
      }
      const span = seg[si] || 1;
      const t = Math.max(0, Math.min(1, (target - base) / span));
      const a = raw[si];
      const b = raw[si + 1];
      points.push({
        x: a.x + (b.x - a.x) * t,
        y: a.y + (b.y - a.y) * t,
        z: a.z + (b.z - a.z) * t,
        arc: target,
      });
    }
    const last = raw[raw.length - 1];
    points.push({ x: last.x, y: last.y, z: last.z, arc: total });
    return { points: points, length: total };
  }

  // The top quarter bends over, a little more each step, like a shoot heavy
  // with its own growth. Each step keeps its length, so the height is arc.
  function nod(raw, p) {
    if (p.tipNodDeg <= 0) return;
    const rng = stream(p.seed, "nod");
    const heading = rng() * TAU;
    const axis = { x: -Math.sin(heading), y: Math.cos(heading), z: 0 };
    const max = p.tipNodDeg * Math.PI / 180;
    const from = 0.72;
    let prevOld = raw[0];
    for (let i = 1; i < raw.length; i++) {
      const old = raw[i];
      const t = i / (raw.length - 1);
      const u = Math.max(0, (t - from) / (1 - from));
      const step = sub(old, prevOld);
      const bent = u > 0 ? rotateAround(step, axis, max * u * u * (3 - 2 * u)) : step;
      raw[i] = add(raw[i - 1], bent);
      prevOld = old;
    }
  }

  // Rings share the stalk above the bare foot. Irregularity moves each by up
  // to a third of the spacing, so rings never cross and pods keep their order.
  function placeRings(count, bare, total, rng, irregularity) {
    const rings = new Array(count);
    for (let i = 0; i < count; i++) {
      const jitter = (rng() - 0.5) * 0.66 * irregularity;
      rings[i] = { index: i, arc: bare + ((i + 0.5 + jitter) / count) * (total - bare) };
    }
    return rings;
  }

  function radiusAt(arc, total, baseV, tipV, rings, bulge, width) {
    const s = total > 0 ? arc / total : 0;
    const taper = baseV + (tipV - baseV) * s;
    let extra = 0;
    for (let i = 0; i < rings.length; i++) {
      const d = Math.abs(arc - rings[i].arc);
      if (d >= width) continue;
      const u = 1 - d / width;
      const smooth = u * u * (3 - 2 * u);
      const bump = taper * bulge * smooth;
      if (bump > extra) extra = bump;
    }
    return taper + extra;
  }

  function nearestSample(samples, arc) {
    let lo = 0;
    let hi = samples.length - 1;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (samples[mid].arc < arc) lo = mid + 1;
      else hi = mid;
    }
    const b = samples[lo];
    const a = samples[Math.max(0, lo - 1)];
    return Math.abs(a.arc - arc) <= Math.abs(b.arc - arc) ? a : b;
  }

  function fillStalk(samples, shell, knuckleWidth) {
    const rings = samples.rings;
    const cells = sweep(samples);
    if (cells.overflow) return cells;

    for (const cell of cells.values()) {
      const dist = Math.sqrt(cell.d2);
      const inner = cell.radius - shell;
      if (inner >= 0.5 && dist <= inner) {
        cell.c = VG.PALETTE.CORE;
      } else if (nearRing(rings, cell.arc, knuckleWidth * 0.5)) {
        // The band is the fat middle of the bulge, not its whole fade.
        cell.c = VG.PALETTE.KNUCKLE;
      } else {
        cell.c = VG.PALETTE.SHELL;
      }
    }
    return cells;
  }

  // Every cell within a sample's radius of the centreline, nearest sample wins.
  function sweep(samples) {
    const cells = new Map();
    for (let i = 0; i < samples.length; i++) {
      const s = samples[i];
      const pad = Math.ceil(s.radius) + 1;
      const x0 = Math.floor(s.x - pad);
      const x1 = Math.floor(s.x + pad);
      const y0 = Math.floor(s.y - pad);
      const y1 = Math.floor(s.y + pad);
      const z0 = Math.floor(s.z - pad);
      const z1 = Math.floor(s.z + pad);
      const cap = i === 0 || i === samples.length - 1;
      const r2 = s.radius * s.radius;
      for (let z = z0; z <= z1; z++) {
        if (z < 0) continue;
        const dz = (z + 0.5) - s.z;
        for (let y = y0; y <= y1; y++) {
          const dy = (y + 0.5) - s.y;
          for (let x = x0; x <= x1; x++) {
            const dx = (x + 0.5) - s.x;
            const along = dx * s.T.x + dy * s.T.y + dz * s.T.z;
            if (!cap && along * along > 0.64) continue;
            const px = dx - along * s.T.x;
            const py = dy - along * s.T.y;
            const pz = dz - along * s.T.z;
            const d2 = px * px + py * py + pz * pz;
            if (d2 > r2) continue;
            if (x <= -OFF || x >= OFF || y <= -OFF || y >= OFF || z >= OFF) {
              cells.overflow = true;
              return cells;
            }
            const key = keyOf(x, y, z);
            const prev = cells.get(key);
            if (prev && prev.d2 <= d2) continue;
            cells.set(key, { x: x, y: y, z: z, d2: d2, radius: s.radius, arc: s.arc });
          }
        }
      }
    }
    return cells;
  }

  // A tube along points, with the frames and arc the sweep needs.
  function tubeSamples(raw, radiusOf) {
    const resampled = resample(raw, 1);
    const pts = resampled.points;
    const total = resampled.length;
    const out = new Array(pts.length);
    for (let i = 0; i < pts.length; i++) {
      const a = pts[Math.max(0, i - 1)];
      const b = pts[Math.min(pts.length - 1, i + 1)];
      out[i] = {
        x: pts[i].x, y: pts[i].y, z: pts[i].z,
        arc: pts[i].arc,
        u: total > 0 ? pts[i].arc / total : 0,
        T: norm(sub(b, a)),
        radius: radiusOf(total > 0 ? pts[i].arc / total : 0),
      };
    }
    out.total = total;
    return out;
  }

  // Arms grow out of the knuckles between the pod slots' spiral, rise, then
  // bow under their own fruit. Each is solid like the stalk, rooted at the
  // stalk's axis so it can never come loose from it.
  function addArms(cells, samples, p) {
    const arms = [];
    if (p.armsPerRing <= 0) return arms;
    const rng = stream(p.seed, "arms");
    const vs = p.voxelSize;
    const twist = p.podTwistDeg * Math.PI / 180;
    const slot = TAU / p.armsPerRing;
    const irr = p.irregularity;
    const rings = samples.rings;
    for (let i = 0; i < rings.length; i++) {
      const ring = rings[i];
      const sample = ring.sample;
      for (let a = 0; a < p.armsPerRing; a++) {
        const angle = i * twist + a * slot + (rng() - 0.5) * 0.9 * irr;
        const lengthV = (p.armLengthM / vs) * (1 + (rng() - 0.5) * 0.7 * irr);
        const rise = (p.armRiseDeg + (rng() - 0.5) * 30 * irr) * Math.PI / 180;
        const sag = p.armSag * (1 + (rng() - 0.5) * 0.6 * irr);
        const swing = (rng() - 0.5) * 0.6 * irr;
        let out = directionAt(sample, angle);
        out = norm({ x: out.x, y: out.y, z: 0 });
        const across = { x: -out.y, y: out.x, z: 0 };
        const root = { x: sample.x, y: sample.y, z: sample.z };
        const steps = Math.max(8, Math.ceil(lengthV * 2));
        const raw = [];
        for (let s = 0; s <= steps; s++) {
          const u = s / steps;
          const reach = sample.radius * 0.5 + lengthV * Math.cos(rise) * u * (1 - 0.18 * u);
          const lift = lengthV * (Math.sin(rise) * u - sag * u * u * 0.6);
          raw.push(add(root, add(scale(out, reach), add(scale(across, swing * lengthV * u * u), { x: 0, y: 0, z: lift }))));
        }
        const r0 = Math.max(1, ring.midRadius * p.armThickness);
        const arm = tubeSamples(raw, function (u) { return Math.max(0.75, r0 * (1 - 0.55 * u)); });
        const own = sweep(arm);
        if (own.overflow) {
          cells.overflow = true;
          return arms;
        }
        for (const [key, cell] of own) {
          if (cells.has(key)) continue;
          const inner = cell.radius - p.shellVoxels;
          cell.c = inner >= 0.5 && Math.sqrt(cell.d2) <= inner ? VG.PALETTE.CORE : VG.PALETTE.SHELL;
          cells.set(key, cell);
        }
        // Pods are spaced by how far out they hang, not along the arm: where
        // the tip turns down, even steps along it would stack them.
        let reach = 0;
        arm[0].h = 0;
        for (let k = 1; k < arm.length; k++) {
          reach += Math.hypot(arm[k].x - arm[k - 1].x, arm[k].y - arm[k - 1].y);
          arm[k].h = reach;
        }
        for (let k = 0; k < arm.length; k++) arm[k].h = reach > 0 ? arm[k].h / reach : 0;
        arms.push({ ring: i, slot: a, samples: arm, out: out });
      }
    }
    return arms;
  }

  function sampleAt(tube, key, value) {
    let best = tube[0];
    for (let i = 1; i < tube.length; i++) {
      if (Math.abs(tube[i][key] - value) < Math.abs(best[key] - value)) best = tube[i];
    }
    return best;
  }

  // Where each pickle hangs from: the pod is the top of the fruit, at the
  // bottom of its stem. Stems are drawn now; the pods are found once every
  // other voxel is down, so a leaf cannot sit on one.
  function hangPods(cells, samples, arms, p) {
    const rng = stream(p.seed, "pods");
    const rings = samples.rings;
    const twist = p.podTwistDeg * Math.PI / 180;
    const slot = TAU / p.podsPerRing;
    const stem = p.pedicelM / p.voxelSize;
    const pods = [];
    for (let i = 0; i < rings.length; i++) {
      const sample = rings[i].sample;
      if (arms.length) {
        const mine = arms.filter(function (arm) { return arm.ring === i; });
        for (let a = 0; a < mine.length; a++) {
          const share = Math.floor(p.podsPerRing / mine.length) + (a < p.podsPerRing % mine.length ? 1 : 0);
          for (let j = 0; j < share; j++) {
            const gap = share === 1 ? 0.3 : 0.6 / (share - 1);
            const u = share === 1 ? 0.75 : 0.38 + gap * j;
            const at = sampleAt(mine[a].samples, "h", Math.min(1, u + (rng() - 0.5) * gap * 0.5 * p.irregularity));
            const top = { x: at.x, y: at.y, z: at.z - at.radius };
            const end = { x: top.x, y: top.y, z: top.z - stem };
            if (stem > 0) plotLine(cells, { x: at.x, y: at.y, z: at.z }, end, VG.PALETTE.PETIOLE);
            pods.push({ end: end, out: mine[a].out });
          }
        }
      } else {
        for (let s = 0; s < p.podsPerRing; s++) {
          const angle = i * twist + s * slot + (rng() - 0.5) * 0.5 * p.irregularity;
          const dir = directionAt(sample, angle);
          const surface = add({ x: sample.x, y: sample.y, z: sample.z }, scale(dir, sample.radius + 1));
          const end = add(surface, { x: dir.x * Math.min(2, stem * 0.3), y: dir.y * Math.min(2, stem * 0.3), z: -stem });
          if (stem > 0) plotLine(cells, add({ x: sample.x, y: sample.y, z: sample.z }, scale(dir, sample.radius - 1)), end, VG.PALETTE.PETIOLE);
          pods.push({ end: end, out: dir });
        }
      }
    }
    return pods;
  }

  function nearRing(rings, arc, width) {
    for (let i = 0; i < rings.length; i++) {
      if (Math.abs(arc - rings[i].arc) <= width) return true;
    }
    return false;
  }

  // Five lobes, longest along the midrib, with a notch where the petiole joins.
  function lobeRadius(theta) {
    const lobes = [
      { a: 0, amp: 1, w: 0.4 },
      { a: 0.62, amp: 0.78, w: 0.32 },
      { a: -0.62, amp: 0.78, w: 0.32 },
      { a: 1.22, amp: 0.5, w: 0.28 },
      { a: -1.22, amp: 0.5, w: 0.28 },
    ];
    let r = 0.14;
    for (let i = 0; i < lobes.length; i++) {
      const L = lobes[i];
      let d = theta - L.a;
      if (d > Math.PI) d -= TAU;
      if (d < -Math.PI) d += TAU;
      const g = Math.exp(-(d * d) / (2 * L.w * L.w));
      const v = L.amp * g;
      if (v > r) r = v;
    }
    const back = Math.max(0, -Math.cos(theta));
    return r * (1 - 0.5 * back * back);
  }

  function paint(cells, x, y, z, colour, overwrite) {
    if (z < 0 || x <= -OFF || x >= OFF || y <= -OFF || y >= OFF || z >= OFF) return;
    const key = keyOf(x, y, z);
    const prev = cells.get(key);
    if (!prev) {
      cells.set(key, { x: x, y: y, z: z, c: colour, d2: 0, radius: 0, arc: 0 });
      return;
    }
    if (overwrite && (prev.c === VG.PALETTE.PETIOLE || prev.c === VG.PALETTE.TENDRIL)) prev.c = colour;
  }

  function plotLine(cells, a, b, colour) {
    const steps = Math.max(1, Math.ceil(Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z) * 2));
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      paint(cells,
        Math.floor(a.x + (b.x - a.x) * t),
        Math.floor(a.y + (b.y - a.y) * t),
        Math.floor(a.z + (b.z - a.z) * t),
        colour, false);
    }
  }

  function directionAt(sample, angle) {
    return norm(add(scale(sample.N, Math.cos(angle)), scale(sample.B, Math.sin(angle))));
  }

  function addLeaves(cells, samples, arms, p) {
    const rng = stream(p.seed, "leaves");
    const lengthV = p.leafSizeM / p.voxelSize;
    const droop = p.leafDroopDeg * Math.PI / 180;
    const tilt = p.leafTiltDeg * Math.PI / 180;
    // Midway between whatever leaves the stalk: arms when there are any, else pods.
    const slot = TAU / (arms.length ? p.armsPerRing : p.podsPerRing);
    const twist = p.podTwistDeg * Math.PI / 180;
    const rings = samples.rings;
    for (let i = 0; i < rings.length && p.leavesPerRing > 0; i++) {
      const sample = rings[i].sample;
      for (let leaf = 0; leaf < p.leavesPerRing; leaf++) {
        const angle = i * twist + (leaf + 0.5) * slot + (rng() - 0.5) * 0.3;
        const outward = directionAt(sample, angle);
        const origin = add({ x: sample.x, y: sample.y, z: sample.z }, scale(outward, sample.radius));
        drawLeaf(cells, origin, outward, lengthV, droop, tilt);
      }
    }
    for (let a = 0; a < arms.length; a++) {
      const tube = arms[a].samples;
      const out = arms[a].out;
      const across = { x: -out.y, y: out.x, z: 0 };
      // Along the arm, sides alternating, leaning out and up a little so the
      // blade sits beside the fruit, not on it.
      for (let k = 0; k < p.leavesPerArm; k++) {
        const side = k % 2 === 0 ? 1 : -1;
        const at = sampleAt(tube, "h", 0.22 + 0.7 * (k + 0.5) / p.leavesPerArm + (rng() - 0.5) * 0.08);
        const outward = norm(add(add(scale(across, side), scale(out, 0.45)), { x: 0, y: 0, z: 0.25 }));
        drawLeaf(cells, add(at, scale(outward, at.radius)), outward, lengthV * (0.75 + rng() * 0.25), droop, tilt);
      }
      if (!p.armTipLeaf) continue;
      const tip = tube[tube.length - 1];
      const ahead = norm({ x: tip.T.x, y: tip.T.y, z: Math.max(tip.T.z, -0.2) });
      drawLeaf(cells, tip, ahead, lengthV * 0.7, droop * 0.6, tilt);
    }
  }

  function drawLeaf(cells, origin, outward, lengthV, droop, tilt) {
    const midrib = norm(add(scale(outward, Math.cos(droop)), { x: 0, y: 0, z: -Math.sin(droop) }));
    let side = cross(midrib, { x: 0, y: 0, z: 1 });
    if (len(side) < 1e-4) side = cross(midrib, { x: 1, y: 0, z: 0 });
    side = norm(side);
    let normal = norm(cross(side, midrib));
    side = norm(add(scale(side, Math.cos(tilt)), scale(normal, Math.sin(tilt))));

    const reach = lengthV * 0.72;
    const center = add(origin, scale(midrib, lengthV * 0.28));
    plotLine(cells, origin, center, VG.PALETTE.PETIOLE);

    const step = 0.7;
    for (let u = -reach; u <= reach; u += step) {
      for (let v = -reach; v <= reach; v += step) {
        const theta = Math.atan2(v, u);
        const rho = Math.hypot(u, v);
        if (rho > lobeRadius(theta) * reach) continue;
        const point = add(center, add(scale(midrib, u), scale(side, v)));
        paint(cells, Math.floor(point.x), Math.floor(point.y), Math.floor(point.z), VG.PALETTE.LEAF, true);
      }
    }
  }

  // Tendrils curl off the stalk, or off the arms' last third when there are arms.
  function addTendrils(cells, samples, arms, p) {
    const rng = stream(p.seed, "tendrils");
    const rings = samples.rings;
    const slot = TAU / p.podsPerRing;
    const twist = p.podTwistDeg * Math.PI / 180;
    const lengthV = p.tendrilLengthM / p.voxelSize;
    const usable = Math.max(1, rings.length - 1);
    for (let n = 0; n < p.tendrilCount; n++) {
      const ring = rings[Math.floor(rng() * usable)];
      let sample = ring.sample;
      let outward = directionAt(sample, ring.index * twist + (Math.floor(rng() * p.podsPerRing) + 0.5) * slot);
      if (arms.length) {
        const arm = arms[Math.floor(rng() * arms.length)];
        sample = sampleAt(arm.samples, "u", 0.65 + rng() * 0.35);
        outward = norm(add(arm.out, { x: 0, y: 0, z: 0.6 }));
      }
      const side = norm(cross(outward, sample.T));
      const start = add(
        { x: sample.x, y: sample.y, z: sample.z },
        scale(outward, sample.radius + 0.8));
      const steps = Math.max(4, Math.ceil(lengthV * 3));
      let prev = start;
      for (let i = 1; i <= steps; i++) {
        const t = i / steps;
        const ang = t * p.tendrilCurl * TAU;
        const curl = 0.35 + t * 2.4;
        const point = add(start, add(
          scale(outward, t * lengthV * 0.55),
          add(scale(side, Math.cos(ang) * curl),
            add(scale(sample.T, Math.sin(ang) * curl), { x: 0, y: 0, z: -t * lengthV * 0.18 }))));
        plotLine(cells, prev, point, VG.PALETTE.TENDRIL);
        prev = point;
      }
    }
  }

  function podName(index) {
    const text = String(index);
    return "@Pod" + (text.length < 2 ? "0" + text : text);
  }

  function pack(cells, samples, pods, p) {
    if (cells.overflow) {
      const failed = emptyResult(p, "The vine left the range the generator can place. Lower the wobble, lean, or helix.");
      failed.overflow = true;
      return failed;
    }
    const joined = connect(cells);

    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    let maxZ = 0;
    const voxels = [];
    let shell = 0;
    let knuckle = 0;
    let core = 0;
    let leaf = 0;
    let tendril = 0;
    for (const cell of cells.values()) {
      voxels.push(cell);
      if (cell.x < minX) minX = cell.x;
      if (cell.y < minY) minY = cell.y;
      if (cell.x > maxX) maxX = cell.x;
      if (cell.y > maxY) maxY = cell.y;
      if (cell.z > maxZ) maxZ = cell.z;
      if (cell.c === VG.PALETTE.SHELL) shell++;
      else if (cell.c === VG.PALETTE.KNUCKLE) knuckle++;
      else if (cell.c === VG.PALETTE.CORE) core++;
      else if (cell.c === VG.PALETTE.LEAF || cell.c === VG.PALETTE.PETIOLE) leaf++;
      else if (cell.c === VG.PALETTE.TENDRIL) tendril++;
    }
    if (!voxels.length) {
      return emptyResult(p, "The vine produced no voxels.");
    }

    const markers = placeMarkers(pods, cells);
    const hang = hangCheck(markers, cells, p);
    for (let i = 0; i < voxels.length; i++) {
      voxels[i] = {
        x: voxels[i].x - minX,
        y: voxels[i].y - minY,
        z: voxels[i].z,
        c: voxels[i].c,
      };
    }
    for (let i = 0; i < markers.length; i++) {
      markers[i].x -= minX;
      markers[i].y -= minY;
    }
    voxels.sort(function (a, b) {
      return a.z - b.z || a.y - b.y || a.x - b.x || a.c - b.c;
    });
    markers.sort(function (a, b) {
      return a.name < b.name ? -1 : a.name > b.name ? 1 : 0;
    });

    const bounds = { x: maxX - minX + 1, y: maxY - minY + 1, z: maxZ + 1 };
    let footX = 0;
    let footY = 0;
    let footN = 0;
    for (let i = 0; i < voxels.length; i++) {
      if (voxels[i].z !== 0) continue;
      footX += voxels[i].x + 0.5;
      footY += voxels[i].y + 0.5;
      footN++;
    }
    const vs = p.voxelSize;
    const warnings = [];
    const total = voxels.length;
    if (total > VG.TREE_VOXELS) {
      warnings.push(total.toLocaleString("en-US") + " voxels. The tall tree that already spikes collision is " + VG.TREE_VOXELS.toLocaleString("en-US") + ".");
    }
    if (joined.stranded > 0) {
      warnings.push(joined.stranded + " voxels float more than " + VG.BRIDGE_REACH
        + " voxels clear of the vine. In the game they hold until the first shot anywhere on it, then fall.");
    }
        if (core === 0) {
      warnings.push("No core. The stalk is thinner than twice the shell, so a carve will not reveal anything.");
    }
    if (hang.blocked + hang.grounded > 0) {
      warnings.push((hang.blocked + hang.grounded) + " of " + markers.length + " pods have no "
        + VG.PICKLE_HEIGHT_M + " m drop below them (" + hang.blocked + " into the vine, "
        + hang.grounded + " into the ground). Lengthen the arms, raise their rise, or shorten the stems.");
    }
    if (markers.length > 1 && hang.spacingM < VG.POD_SPACING_M) {
      warnings.push("Two pods are " + hang.spacingM.toFixed(2) + " m apart; hanging pickles overlap under "
        + VG.POD_SPACING_M + " m. Fewer pods per ring, or more arms.");
    }
    let error = null;
    if (bounds.x > VG.AXIS_LIMIT || bounds.y > VG.AXIS_LIMIT || bounds.z > VG.AXIS_LIMIT) {
      error = "The vine is " + bounds.x + " × " + bounds.y + " × " + bounds.z
        + " voxels. MagicaVoxel stores each axis in a byte, so a model stops at 256. Raise voxel size, or lower the height, lean, wobble, or leaves.";
    }

    const rings = [];
    for (let i = 0; i < samples.rings.length; i++) {
      const ring = samples.rings[i];
      rings.push({
        index: ring.index,
        radius: ring.radius,
        midRadius: ring.midRadius,
        angle0: ring.angle0,
      });
    }

    return {
      ok: error == null,
      error: error,
      warnings: warnings,
      params: p,
      voxelSize: vs,
      voxels: voxels,
      markers: markers,
      rings: rings,
      counts: {
        shell: shell + knuckle,
        knuckle: knuckle,
        core: core,
        leaf: leaf,
        tendril: tendril,
        bridged: joined.bridged,
        stranded: joined.stranded,
        total: total,
        markers: markers.length,
        podsBlocked: hang.blocked,
        podsGrounded: hang.grounded,
        podSpacingM: hang.spacingM,
      },
      bounds: bounds,
      metres: {
        x: bounds.x * vs,
        y: bounds.y * vs,
        z: bounds.z * vs,
        height: p.heightM,
      },
      foot: footN ? { x: footX / footN, y: footY / footN, z: 0 } : { x: bounds.x / 2, y: bounds.y / 2, z: 0 },
    };
  }

  // How far a bridge reaches through empty cells to join a loose part.
  VG.BRIDGE_REACH = 24;

  // Every voxel joined to the ground face to face. The game holds a part up
  // only by faces (an edge or a corner is not a joint), and checks the whole
  // vine at its first hit: a leaf a cell off its arm, or a stem meeting it on
  // a diagonal, held until a shot anywhere on the vine and then fell with it.
  // Each part that does not reach the ground is joined to the nearest part
  // that does, along the shortest run of empty cells, in its own colour.
  function connect(cells) {
    const NEIGH = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];
    const held = new Set();
    let queue = [];
    for (const [key, cell] of cells) {
      if (cell.z === 0) {
        held.add(key);
        queue.push(cell);
      }
    }
    function flood(from, into) {
      const list = [];
      let q = from;
      while (q.length) {
        const next = [];
        for (const c of q) {
          list.push(c);
          for (const d of NEIGH) {
            const key = keyOf(c.x + d[0], c.y + d[1], c.z + d[2]);
            const n = cells.get(key);
            if (n && !into.has(key)) {
              into.add(key);
              next.push(n);
            }
          }
        }
        q = next;
      }
      return list;
    }
    flood(queue, held);

    let bridged = 0;
    let stranded = 0;
    const seen = new Set();
    for (const [key, cell] of cells) {
      if (held.has(key) || seen.has(key)) continue;
      seen.add(key);
      const island = flood([cell], seen);
      // Out through empty cells, one ring at a time, until a held voxel is
      // beside the front: then back along the way it came.
      const from = new Map();
      let front = [];
      for (const c of island) {
        const k = keyOf(c.x, c.y, c.z);
        from.set(k, null);
        front.push({ x: c.x, y: c.y, z: c.z, k: k });
      }
      let end = null;
      for (let step = 0; step <= VG.BRIDGE_REACH && front.length && !end; step++) {
        const next = [];
        for (const c of front) {
          for (const d of NEIGH) {
            const x = c.x + d[0], y = c.y + d[1], z = c.z + d[2];
            if (z < 0) continue;
            const k = keyOf(x, y, z);
            if (held.has(k)) {
              end = c;
              break;
            }
            if (from.has(k) || cells.has(k)) continue;
            from.set(k, c.k);
            next.push({ x: x, y: y, z: z, k: k });
          }
          if (end) break;
        }
        front = next;
      }
      if (!end) {
        stranded += island.length;
        continue;
      }
      const colour = island[0].c;
      for (let k = end.k; k != null && !cells.has(k); k = from.get(k)) {
        const z = Math.floor(k / (SPAN * SPAN)) - OFF;
        const y = Math.floor((k % (SPAN * SPAN)) / SPAN) - OFF;
        const x = (k % SPAN) - OFF;
        cells.set(k, { x: x, y: y, z: z, c: colour, d2: 0, radius: 0, arc: 0 });
        held.add(k);
        bridged++;
      }
      for (const c of island) held.add(keyOf(c.x, c.y, c.z));
    }
    return { bridged: bridged, stranded: stranded };
  }

  // The first empty cell at or below the stem's end; out from the vine if the
  // fall is blocked for eight cells. A placed pod claims its cell, so two pods
  // never share one.
  function placeMarkers(pods, cells) {
    const markers = [];
    const claimed = new Set();
    function free(x, y, z) {
      const key = keyOf(x, y, z);
      return !cells.has(key) && !claimed.has(key);
    }
    for (let i = 0; i < pods.length; i++) {
      const pod = pods[i];
      let x = Math.floor(pod.end.x);
      let y = Math.floor(pod.end.y);
      let z = Math.max(0, Math.floor(pod.end.z));
      let found = false;
      for (let step = 0; step < 8 && !found; step++) {
        const zz = Math.max(0, z - step);
        if (free(x, y, zz)) {
          z = zz;
          found = true;
        }
      }
      for (let step = 1; step <= 8 && !found; step++) {
        const xx = Math.floor(pod.end.x + pod.out.x * step);
        const yy = Math.floor(pod.end.y + pod.out.y * step);
        if (xx > -OFF && xx < OFF && yy > -OFF && yy < OFF && free(xx, yy, z)) {
          x = xx;
          y = yy;
          found = true;
        }
      }
      claimed.add(keyOf(x, y, z));
      markers.push({ name: podName(i + 1), x: x, y: y, z: z });
    }
    return markers;
  }

  // Can a pickle hang from each pod? Its body runs PICKLE_HEIGHT_M straight
  // down; it is blocked by a voxel or another pod in that column, or by the ground.
  function hangCheck(markers, cells, p) {
    const drop = Math.ceil(VG.PICKLE_HEIGHT_M / p.voxelSize);
    const pods = new Set();
    for (let i = 0; i < markers.length; i++) pods.add(keyOf(markers[i].x, markers[i].y, markers[i].z));
    let blocked = 0;
    let grounded = 0;
    for (let i = 0; i < markers.length; i++) {
      const m = markers[i];
      for (let z = m.z - 1; z >= m.z - drop; z--) {
        if (z < 0) { grounded++; break; }
        const key = keyOf(m.x, m.y, z);
        if (cells.has(key) || pods.has(key)) { blocked++; break; }
      }
    }
    let nearest = Infinity;
    for (let i = 0; i < markers.length; i++) {
      for (let j = i + 1; j < markers.length; j++) {
        const d = Math.hypot(markers[i].x - markers[j].x, markers[i].y - markers[j].y, markers[i].z - markers[j].z);
        if (d < nearest) nearest = d;
      }
    }
    return { blocked: blocked, grounded: grounded, spacingM: Number.isFinite(nearest) ? nearest * p.voxelSize : 0 };
  }

  VG.describe = function (vine, file) {
    return {
      file: file || null,
      seed: vine.params.seed,
      ok: vine.ok,
      error: vine.error,
      warnings: vine.warnings,
      voxelSize: vine.voxelSize,
      counts: vine.counts,
      bounds: vine.bounds,
      metres: vine.metres,
    };
  };
})(window.VG = window.VG || {});
