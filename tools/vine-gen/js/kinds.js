// Kinds of vine, each a set of ranges, and the archetype that holds them.
//
// A vine's settings are not values but ranges. A kind's pool is a handful of
// seeds, and each seed rolls every setting inside its range, each from its own
// named stream: widening one range never reshuffles the others. The nest then
// places copies drawn from the pools (docs/decisions/0018: generation draws
// from authored pools and one seed). In the game each pool vine is one .vox.
(function (VG) {
  "use strict";

  // Settings a kind rolls. Voxel size is the archetype's, shared by every
  // vine, and the seed is the pool's.
  VG.RANGE_FIELDS = VG.FIELDS.filter(function (field) {
    return !field.global && field.key !== "seed";
  });

  // The hanging share of the hive: 95 % of 25,000 (docs/decisions/0029).
  VG.HIVE_HANGING = 23750;

  function range(lo, hi) { return [lo, hi]; }

  // Where in the nest each kind grows is its zone, a share of the nest's
  // radius: 0 is the heart, 1 the outer edge.
  VG.DEFAULT_KINDS = [
    {
      name: "Heart", weight: 1, zone: [0, 0], pool: 2,
      ranges: {
        heightM: range(100, 110), ringCount: range(11, 12), bareStalkM: range(28, 32),
        wobbleM: range(1, 3), leanDeg: range(-3, 3), tipNodDeg: range(10, 30), irregularity: range(0.4, 0.6),
        baseRadiusM: range(3.4, 3.8), tipRadiusM: range(1.1, 1.3),
        armsPerRing: range(4, 4), armLengthM: range(20, 23), armRiseDeg: range(38, 44), armSag: range(1.3, 1.5),
        armThickness: range(0.32, 0.36),
        podsPerRing: range(18, 20), podTwistDeg: range(35, 45), pedicelM: range(2, 2.5),
        leavesPerRing: range(0, 0), leavesPerArm: range(0, 0), leafSizeM: range(9, 10),
        tendrilCount: range(16, 22),
      },
      chances: { tendrils: 1, armTipLeaf: 1 },
    },
    {
      name: "Fruiting", weight: 3, zone: [0.05, 0.45], pool: 4,
      ranges: {
        heightM: range(84, 96), ringCount: range(12, 14), bareStalkM: range(20, 26),
        wobbleM: range(1, 4), leanDeg: range(0, 5), tipNodDeg: range(20, 50), irregularity: range(0.4, 0.7),
        baseRadiusM: range(2.6, 3.0), tipRadiusM: range(0.9, 1.1),
        armsPerRing: range(3, 4), armLengthM: range(14, 18), armRiseDeg: range(36, 44), armSag: range(1.1, 1.4),
        podsPerRing: range(12, 16), podTwistDeg: range(30, 60), pedicelM: range(1.5, 2.25),
        leavesPerRing: range(0, 0), leavesPerArm: range(0, 0), leafSizeM: range(7, 9),
        tendrilCount: range(10, 16),
      },
      chances: { tendrils: 0.8, armTipLeaf: 1 },
    },
    {
      name: "Weeping", weight: 3, zone: [0.35, 0.8], pool: 4,
      ranges: {
        heightM: range(70, 82), ringCount: range(9, 11), bareStalkM: range(22, 28),
        wobbleM: range(1, 4), leanDeg: range(4, 9), tipNodDeg: range(20, 50), irregularity: range(0.5, 0.7),
        baseRadiusM: range(2.6, 3.0), tipRadiusM: range(0.9, 1.1),
        armsPerRing: range(4, 4), armLengthM: range(16, 20), armRiseDeg: range(36, 44), armSag: range(1.3, 1.5),
        podsPerRing: range(12, 16), podTwistDeg: range(30, 50), pedicelM: range(1.75, 2.25),
        leavesPerRing: range(0, 1), leavesPerArm: range(0, 1), leafSizeM: range(8, 10),
        tendrilCount: range(10, 16),
      },
      chances: { tendrils: 0.7, armTipLeaf: 1 },
    },
    {
      name: "Leafy", weight: 3, zone: [0.75, 1], pool: 4,
      ranges: {
        heightM: range(50, 62), ringCount: range(6, 8), bareStalkM: range(10, 15),
        wobbleM: range(2, 5), leanDeg: range(10, 16), tipNodDeg: range(30, 80), irregularity: range(0.5, 0.8),
        baseRadiusM: range(2.4, 2.9), tipRadiusM: range(0.8, 1.0),
        armsPerRing: range(2, 3), armLengthM: range(9, 13), armRiseDeg: range(25, 40), armSag: range(0.5, 0.9),
        podsPerRing: range(2, 5), podTwistDeg: range(60, 140), pedicelM: range(1, 1.5),
        leavesPerRing: range(1, 1), leavesPerArm: range(1, 2), leafSizeM: range(11, 14),
        leafTiltDeg: range(20, 40), leafDroopDeg: range(20, 40),
        tendrilCount: range(14, 22),
      },
      chances: { tendrils: 1, armTipLeaf: 1 },
    },
  ];

  function fieldOf(key) {
    for (let i = 0; i < VG.FIELDS.length; i++) if (VG.FIELDS[i].key === key) return VG.FIELDS[i];
    return null;
  }
  VG.fieldOf = fieldOf;

  function num(value, fallback, min, max) {
    let n = Number(value);
    if (!Number.isFinite(n)) n = fallback;
    return Math.min(max, Math.max(min, n));
  }

  // Every rangeable setting present, low never above high, chances in 0..1.
  VG.clampKind = function (raw) {
    const src = raw || {};
    const ranges = {};
    const chances = {};
    const inRanges = src.ranges || {};
    const inChances = src.chances || {};
    for (let i = 0; i < VG.RANGE_FIELDS.length; i++) {
      const field = VG.RANGE_FIELDS[i];
      if (field.type === "bool") {
        const fallback = field.fallback ? 1 : 0;
        chances[field.key] = num(inChances[field.key], fallback, 0, 1);
        continue;
      }
      const pair = Array.isArray(inRanges[field.key]) ? inRanges[field.key] : [field.fallback, field.fallback];
      let lo = num(pair[0], field.fallback, field.min, field.max);
      let hi = num(pair[1], lo, field.min, field.max);
      if (field.integer) { lo = Math.round(lo); hi = Math.round(hi); }
      if (hi < lo) { const swap = lo; lo = hi; hi = swap; }
      ranges[field.key] = [lo, hi];
    }
    const zone = Array.isArray(src.zone) ? src.zone : [0, 1];
    let z0 = num(zone[0], 0, 0, 1);
    let z1 = num(zone[1], 1, 0, 1);
    if (z1 < z0) { const swap = z0; z0 = z1; z1 = swap; }
    const kind = {
      name: String(src.name || "Vine").slice(0, 24),
      weight: num(src.weight, 1, 0, 10),
      zone: [z0, z1],
      pool: Math.round(num(src.pool, 4, 1, 8)),
      ranges: ranges,
      chances: chances,
    };
    // A kind made from one saved vine keeps that vine's seed, so it grows the
    // same vine and not merely the same settings.
    if (Number.isFinite(Number(src.fixedSeed))) kind.fixedSeed = Math.round(num(src.fixedSeed, 0, 0, 9999999));
    return kind;
  };

  // The seed of a kind's pool vine, from the archetype's seed. Renaming a
  // kind gives it a new pool; reordering kinds does not.
  VG.poolSeed = function (seed, kindName, index) {
    return (Math.imul((seed >>> 0) ^ VG.hashName(kindName + "#" + index), 2654435761) >>> 0) % 10000000;
  };

  // One vine's settings, each rolled inside its range from its own stream.
  VG.rollVine = function (kind, seed, voxelSize) {
    const params = { seed: seed, voxelSize: voxelSize };
    for (let i = 0; i < VG.RANGE_FIELDS.length; i++) {
      const field = VG.RANGE_FIELDS[i];
      const t = VG.stream(seed, "roll:" + field.key)();
      if (field.type === "bool") {
        params[field.key] = t < kind.chances[field.key];
        continue;
      }
      const lo = kind.ranges[field.key][0];
      const hi = kind.ranges[field.key][1];
      if (field.integer) {
        params[field.key] = Math.min(hi, lo + Math.floor(t * (hi - lo + 1)));
      } else {
        const step = field.step || 0;
        let v = lo + (hi - lo) * t;
        if (step > 0) v = Math.min(hi, Math.max(lo, Math.round(v / step) * step));
        params[field.key] = Math.round(v * 1000) / 1000;
      }
    }
    return VG.clampParams(params);
  };

  VG.defaultArchetype = function () {
    return VG.clampArchetype({ seed: 1, voxelSize: 0.5, kinds: VG.DEFAULT_KINDS });
  };

  VG.clampArchetype = function (raw) {
    const src = raw || {};
    const kinds = (Array.isArray(src.kinds) && src.kinds.length ? src.kinds : VG.DEFAULT_KINDS).slice(0, 8).map(VG.clampKind);
    // Kind names are pool seeds and file names: unique, and safe in a file name.
    const seen = new Set();
    for (let i = 0; i < kinds.length; i++) {
      let name = kinds[i].name.replace(/[^A-Za-z0-9 _-]/g, "").trim() || "Vine";
      let n = 2;
      const base = name;
      while (seen.has(name.toLowerCase())) name = base + " " + n++;
      seen.add(name.toLowerCase());
      kinds[i].name = name;
    }
    const voxelField = fieldOf("voxelSize");
    return {
      seed: Math.round(num(src.seed, 1, 0, 9999999)),
      voxelSize: num(src.voxelSize, voxelField.fallback, voxelField.min, voxelField.max),
      kinds: kinds,
      nest: VG.clampNest ? VG.clampNest(src.nest) : {},
    };
  };

  // A version 1 or 2 file is one vine: it becomes one kind whose ranges are
  // that vine's values, so it loads exactly as it was.
  VG.readArchetype = function (data) {
    if (data && data.version >= 3 && Array.isArray(data.kinds)) return VG.clampArchetype(data);
    if (data && data.params) {
      const params = VG.readPreset(data);
      const ranges = {};
      const chances = {};
      for (let i = 0; i < VG.RANGE_FIELDS.length; i++) {
        const field = VG.RANGE_FIELDS[i];
        if (field.type === "bool") chances[field.key] = params[field.key] ? 1 : 0;
        else ranges[field.key] = [params[field.key], params[field.key]];
      }
      return VG.clampArchetype({
        seed: params.seed,
        voxelSize: params.voxelSize,
        kinds: [{ name: "Imported", weight: 1, zone: [0, 1], pool: 1, ranges: ranges, chances: chances, fixedSeed: params.seed }],
      });
    }
    throw new Error("That file is not a vine archetype or preset.");
  };

  VG.archetypeJson = function (archetype) {
    const a = VG.clampArchetype(archetype);
    return JSON.stringify({
      tool: "dill-control-vine",
      version: 3,
      seed: a.seed,
      voxelSize: a.voxelSize,
      kinds: a.kinds,
      nest: a.nest,
    }, null, 2);
  };

  // The vines a kind's pool rolls, as settings. The page caches the grown
  // vines by these, so editing one kind regrows only that kind.
  VG.poolParams = function (archetype, kindIndex) {
    const kind = archetype.kinds[kindIndex];
    const list = [];
    for (let i = 0; i < kind.pool; i++) {
      const seed = kind.fixedSeed != null ? kind.fixedSeed + i : VG.poolSeed(archetype.seed, kind.name, i);
      list.push(VG.rollVine(kind, seed, archetype.voxelSize));
    }
    return list;
  };
})(window.VG = window.VG || {});
