// The page: an archetype of vine kinds and a nest, every setting a range.
//
// Kind shows a kind's pool, the vines its ranges rolled, side by side. Nest
// lays the colony out on the test planet from every kind's pool. Grown vines
// are cached by their rolled settings, so moving one range regrows only the
// vines it changed. Growing is spread over frames to keep the page alive.
(function (VG) {
  "use strict";

  const paramsEl = document.getElementById("params");
  const statsEl = document.getElementById("stats");
  const statsTitle = document.getElementById("statsTitle");
  const warnEl = document.getElementById("warn");
  const fileStatus = document.getElementById("fileStatus");
  const countStatus = document.getElementById("countStatus");
  const helpEl = document.getElementById("help");
  const connectBtn = document.getElementById("connectBtn");
  const reconnectBtn = document.getElementById("reconnectBtn");
  const writeBtn = document.getElementById("writeBtn");
  const kindBtn = document.getElementById("kindBtn");
  const nestBtn = document.getElementById("nestBtn");
  const canvas = document.getElementById("view");

  let arch = VG.defaultArchetype();
  let kindIndex = 0;
  let tab = "kinds";
  let mode = "kind";
  let growing = false;
  let token = 0;
  let timer = 0;
  let writing = false;
  let framed = "";
  const cache = new Map();

  const hash = location.hash.replace("#", "");
  if (hash === "nest") { mode = "nest"; tab = "nest"; }
  const kindHash = /^kind=(\d+)$/.exec(hash);
  if (kindHash) kindIndex = Math.min(arch.kinds.length - 1, Number(kindHash[1]));

  const view = VG.createView(canvas, {
    onSpeed: function (text) {
      const fly = document.getElementById("fly");
      if (fly) fly.textContent = text;
    },
  });
  if (!view.ok) helpEl.textContent = view.error;

  function fmtN(n) { return Math.round(n).toLocaleString("en-US"); }
  function esc(text) {
    return String(text).replace(/[&<>"]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[c]; });
  }
  function keyOf(params) { return JSON.stringify(params); }
  function tick() { return new Promise(function (resolve) { setTimeout(resolve, 0); }); }

  // ---------------------------------------------------------------- form

  function show(field, value) {
    if (field.integer) return String(Math.round(value));
    const step = field.step || 0.01;
    const places = step >= 1 ? 0 : step >= 0.1 ? 1 : 2;
    return Number(value).toFixed(places);
  }

  function readout(field, lo, hi) {
    const unit = field.suffix ? " " + field.suffix : "";
    if (hi == null || lo === hi) return show(field, lo) + unit;
    return show(field, lo) + " – " + show(field, hi) + unit;
  }

  function dualHtml(path, field, pair) {
    const attrs = " min=\"" + field.min + "\" max=\"" + field.max + "\" step=\"" + field.step + "\"";
    return "<div class=\"field dual-field\" data-path=\"" + path + "\" data-kind=\"dual\">"
      + "<span>" + esc(field.label) + " <b data-readout>" + esc(readout(field, pair[0], pair[1])) + "</b></span>"
      + "<div class=\"dual\"><div class=\"dual-fill\"></div>"
      + "<input type=\"range\" data-end=\"0\"" + attrs + " value=\"" + pair[0] + "\" aria-label=\"" + esc(field.label) + " low\">"
      + "<input type=\"range\" data-end=\"1\"" + attrs + " value=\"" + pair[1] + "\" aria-label=\"" + esc(field.label) + " high\">"
      + "</div>"
      + "<div class=\"pair\"><input type=\"number\" data-end=\"0\"" + attrs + " value=\"" + pair[0] + "\">"
      + "<input type=\"number\" data-end=\"1\"" + attrs + " value=\"" + pair[1] + "\"></div>"
      + "</div>";
  }

  function singleHtml(path, field, value) {
    const attrs = " min=\"" + field.min + "\" max=\"" + field.max + "\" step=\"" + field.step + "\"";
    return "<label class=\"field\" data-path=\"" + path + "\" data-kind=\"single\">"
      + "<span>" + esc(field.label) + " <b data-readout>" + esc(readout(field, value)) + "</b></span>"
      + "<input type=\"range\"" + attrs + " value=\"" + value + "\">"
      + "<input type=\"number\"" + attrs + " value=\"" + value + "\"></label>";
  }

  function chanceField(field) {
    return { label: field.label, min: 0, max: 1, step: 0.05, suffix: "chance" };
  }
  const WEIGHT = { label: "Weight", min: 0, max: 10, step: 0.5 };
  const ZONE = { label: "Zone", min: 0, max: 1, step: 0.05, suffix: "of the radius" };
  const POOL = { label: "Pool", min: 1, max: 8, step: 1, integer: true, suffix: "vines" };
  const SEED = { label: "Seed", min: 0, max: 9999999, step: 1, integer: true };

  function buildForm() {
    let html = "<div class=\"tabs\" role=\"tablist\">"
      + "<button class=\"tab\" type=\"button\" data-tab=\"kinds\" aria-selected=\"" + (tab === "kinds") + "\">Kinds</button>"
      + "<button class=\"tab\" type=\"button\" data-tab=\"nest\" aria-selected=\"" + (tab === "nest") + "\">Nest</button></div>";
    html += "<fieldset><legend>Archetype</legend>"
      + "<div class=\"seed-row\"><label class=\"field\" data-path=\"arch.seed\" data-kind=\"single\"><span>Seed <b data-readout>" + arch.seed + "</b></span>"
      + "<input type=\"number\" min=\"0\" max=\"9999999\" step=\"1\" value=\"" + arch.seed + "\"></label>"
      + "<button class=\"btn\" type=\"button\" id=\"rerollBtn\">Reroll</button></div>"
      + singleHtml("arch.voxelSize", VG.fieldOf("voxelSize"), arch.voxelSize)
      + "</fieldset>";

    if (tab === "kinds") {
      const kind = arch.kinds[kindIndex];
      html += "<div class=\"chips\">";
      for (let i = 0; i < arch.kinds.length; i++) {
        html += "<button class=\"chip\" type=\"button\" data-kind-index=\"" + i + "\" aria-pressed=\"" + (i === kindIndex) + "\">" + esc(arch.kinds[i].name) + "</button>";
      }
      html += "<button class=\"chip add\" type=\"button\" id=\"addKind\" title=\"Copy this kind\"" + (arch.kinds.length >= 8 ? " disabled" : "") + ">+ Copy</button></div>";
      html += "<fieldset><legend>Kind</legend>"
        + "<label class=\"field name\"><span>Name</span><input type=\"text\" id=\"kindName\" maxlength=\"24\" value=\"" + esc(kind.name) + "\"></label>"
        + singleHtml("kind.weight", WEIGHT, kind.weight)
        + dualHtml("kind.zone", ZONE, kind.zone)
        + singleHtml("kind.pool", POOL, kind.pool)
        + "<button class=\"btn small\" type=\"button\" id=\"removeKind\"" + (arch.kinds.length <= 1 ? " disabled" : "") + ">Remove kind</button>"
        + "</fieldset>";
      let group = "";
      for (let i = 0; i < VG.RANGE_FIELDS.length; i++) {
        const field = VG.RANGE_FIELDS[i];
        if (field.group !== group) {
          if (group) html += "</fieldset>";
          group = field.group;
          html += "<fieldset><legend>" + esc(group) + "</legend>";
        }
        if (field.type === "bool") html += singleHtml("kind.chances." + field.key, chanceField(field), kind.chances[field.key]);
        else html += dualHtml("kind.ranges." + field.key, field, kind.ranges[field.key]);
      }
      html += "</fieldset>";
    } else {
      let group = "";
      for (let i = 0; i < VG.NEST_FIELDS.length; i++) {
        const field = VG.NEST_FIELDS[i];
        if (field.group !== group) {
          if (group) html += "</fieldset>";
          group = field.group;
          html += "<fieldset><legend>" + esc(group) + "</legend>";
        }
        if (field.single) html += singleHtml("nest." + field.key, field, arch.nest[field.key]);
        else html += dualHtml("nest." + field.key, field, arch.nest[field.key]);
      }
      html += "</fieldset><p class=\"note pad\">Kinds stand where their zone covers a site's share of the radius: 0 is the heart, 1 the edge. Swing reach is how far one swing carries, anchor to anchor; the grapple itself reaches " + VG.GRAPPLE_RANGE_M + " m.</p>";
    }
    paramsEl.innerHTML = html;
    paramsEl.querySelectorAll("[data-kind=dual]").forEach(paintDual);
  }

  function fieldFor(path) {
    const parts = path.split(".");
    if (parts[0] === "arch") return parts[1] === "seed" ? SEED : VG.fieldOf("voxelSize");
    if (parts[0] === "nest") return VG.NEST_FIELDS.filter(function (f) { return f.key === parts[1]; })[0];
    if (parts[1] === "weight") return WEIGHT;
    if (parts[1] === "zone") return ZONE;
    if (parts[1] === "pool") return POOL;
    if (parts[1] === "chances") return chanceField(VG.fieldOf(parts[2]));
    return VG.fieldOf(parts[2]);
  }

  function getValue(path) {
    const parts = path.split(".");
    if (parts[0] === "arch") return arch[parts[1]];
    if (parts[0] === "nest") return arch.nest[parts[1]];
    const kind = arch.kinds[kindIndex];
    return parts.length === 2 ? kind[parts[1]] : kind[parts[1]][parts[2]];
  }

  function setValue(path, value) {
    const parts = path.split(".");
    if (parts[0] === "arch") { arch[parts[1]] = value; return; }
    if (parts[0] === "nest") { arch.nest[parts[1]] = value; return; }
    const kind = arch.kinds[kindIndex];
    if (parts.length === 2) kind[parts[1]] = value;
    else kind[parts[1]][parts[2]] = value;
  }

  function paintDual(row) {
    const field = fieldFor(row.getAttribute("data-path"));
    const pair = getValue(row.getAttribute("data-path"));
    const span = field.max - field.min || 1;
    const fill = row.querySelector(".dual-fill");
    fill.style.left = ((pair[0] - field.min) / span * 100) + "%";
    fill.style.right = (100 - (pair[1] - field.min) / span * 100) + "%";
    row.querySelector("[data-readout]").textContent = readout(field, pair[0], pair[1]);
    row.querySelectorAll("input").forEach(function (input) {
      if (input === document.activeElement && input.type === "number") return;
      input.value = pair[Number(input.getAttribute("data-end"))];
    });
  }

  function onEdit(event) {
    const target = event.target;
    if (target.id === "kindName") {
      if (event.type !== "change") return;
      arch.kinds[kindIndex].name = target.value;
      arch = VG.clampArchetype(arch);
      buildForm();
      schedule();
      return;
    }
    const row = target.closest("[data-path]");
    if (!row || target.tagName !== "INPUT") return;
    const path = row.getAttribute("data-path");
    const field = fieldFor(path);
    let n = Number(target.value);
    if (!Number.isFinite(n)) return;
    n = Math.min(field.max, Math.max(field.min, field.integer ? Math.round(n) : n));
    if (row.getAttribute("data-kind") === "dual") {
      const end = Number(target.getAttribute("data-end"));
      const pair = getValue(path).slice();
      pair[end] = n;
      // A thumb pushes the other ahead of it rather than crossing it.
      if (pair[0] > pair[1]) pair[1 - end] = n;
      setValue(path, pair);
      paintDual(row);
    } else {
      setValue(path, n);
      row.querySelector("[data-readout]").textContent = readout(field, n);
      row.querySelectorAll("input").forEach(function (input) {
        if (input !== target) input.value = n;
      });
    }
    schedule();
  }

  paramsEl.addEventListener("input", onEdit);
  paramsEl.addEventListener("change", onEdit);
  paramsEl.addEventListener("click", function (event) {
    const tabBtn = event.target.closest("[data-tab]");
    if (tabBtn) {
      tab = tabBtn.getAttribute("data-tab");
      buildForm();
      setMode(tab === "nest" ? "nest" : "kind");
      return;
    }
    const chip = event.target.closest("[data-kind-index]");
    if (chip) {
      kindIndex = Number(chip.getAttribute("data-kind-index"));
      buildForm();
      if (mode !== "kind") setMode("kind");
      else { framed = ""; schedule(0); }
      return;
    }
    if (event.target.id === "addKind") {
      const copy = JSON.parse(JSON.stringify(arch.kinds[kindIndex]));
      copy.name = copy.name + " copy";
      arch.kinds.splice(kindIndex + 1, 0, copy);
      arch = VG.clampArchetype(arch);
      kindIndex += 1;
      buildForm();
      schedule(0);
    } else if (event.target.id === "removeKind" && arch.kinds.length > 1) {
      arch.kinds.splice(kindIndex, 1);
      kindIndex = Math.max(0, kindIndex - 1);
      buildForm();
      schedule(0);
    } else if (event.target.id === "rerollBtn") {
      arch.seed = Math.floor(Math.random() * 1000000) + 1;
      buildForm();
      schedule(0);
    }
  });

  // ---------------------------------------------------------------- growing

  function poolKeys(ki) {
    return VG.poolParams(arch, ki).map(function (params) { return { params: params, key: keyOf(params) }; });
  }

  function entryFor(ki, i) {
    const keys = poolKeys(ki);
    return keys[i] ? cache.get(keys[i].key) : null;
  }

  function schedule(delay) {
    clearTimeout(timer);
    timer = setTimeout(refresh, delay == null ? 160 : delay);
  }

  // Grow whatever the view needs that is not cached, one vine a frame, and
  // redraw as each arrives. A newer edit cancels an older run.
  async function refresh() {
    arch = VG.clampArchetype(arch);
    const run = ++token;
    const wanted = mode === "kind" ? [kindIndex] : arch.kinds.map(function (_, i) { return i; });
    const jobs = [];
    for (let w = 0; w < wanted.length; w++) {
      const keys = poolKeys(wanted[w]);
      for (let i = 0; i < keys.length; i++) if (!cache.has(keys[i].key)) jobs.push(keys[i]);
    }
    prune();
    growing = jobs.length > 0;
    paint();
    for (let j = 0; j < jobs.length; j++) {
      countStatus.innerHTML = "growing <b>" + (j + 1) + "</b> of " + jobs.length;
      countStatus.className = "status";
      await tick();
      if (run !== token) return;
      if (cache.has(jobs[j].key)) continue;
      const vine = VG.generate(jobs[j].params);
      const model = view.ok && vine.voxels.length ? view.makeModel(vine) : null;
      cache.set(jobs[j].key, { vine: vine, model: model });
      if (j === jobs.length - 1 || mode === "kind" || j % 3 === 2) paint();
    }
    growing = false;
    paint();
  }

  // Vines no kind in the archetype rolls any more are dropped, GPU and all.
  function prune() {
    const live = new Set();
    for (let k = 0; k < arch.kinds.length; k++) poolKeys(k).forEach(function (entry) { live.add(entry.key); });
    for (const [key, entry] of cache) {
      if (live.has(key)) continue;
      if (view.ok) view.dropModel(entry.model);
      cache.delete(key);
    }
  }

  // ---------------------------------------------------------------- showing

  function paint() {
    if (mode === "kind") paintKind();
    else paintNest();
  }

  function frameOnce(what) {
    if (!view.ok || framed === what) return;
    framed = what;
    view.frame();
  }

  function paintKind() {
    const kind = arch.kinds[kindIndex];
    const keys = poolKeys(kindIndex);
    const entries = keys.map(function (k) { return cache.get(k.key) || null; });
    if (view.ok) {
      view.showLineup(entries.map(function (e) { return e ? e.model : null; }));
      if (entries.every(Boolean)) frameOnce("kind:" + kindIndex + ":" + kind.pool);
    }
    statsTitle.textContent = kind.name + " pool";
    let rows = "";
    let voxels = 0;
    let pods = 0;
    let grown = 0;
    const warnings = new Map();
    for (let i = 0; i < keys.length; i++) {
      const e = entries[i];
      if (!e) { rows += "<tr><td>" + (i + 1) + "</td><td colspan=\"4\" class=\"muted\">growing…</td></tr>"; continue; }
      const v = e.vine;
      const c = v.counts;
      grown++;
      voxels += c.total;
      pods += c.markers;
      const clear = c.markers - (c.podsBlocked || 0) - (c.podsGrounded || 0);
      const over = c.total > VG.TREE_VOXELS || !v.ok;
      rows += "<tr" + (over ? " class=\"over\"" : "") + "><td>" + (i + 1) + "</td><td>" + (v.ok ? v.metres.z.toFixed(0) + " m" : "—") + "</td><td>" + fmtN(c.total)
        + "</td><td>" + c.markers + "</td><td>" + (c.markers ? Math.round(clear / c.markers * 100) + "%" : "—") + "</td></tr>";
      const notes = v.error ? [v.error] : v.warnings;
      for (let w = 0; w < notes.length; w++) {
        const head = notes[w].replace(/[\d,.]+/g, "#");
        if (!warnings.has(head)) warnings.set(head, { text: notes[w], vines: [] });
        warnings.get(head).vines.push(i + 1);
      }
    }
    statsEl.innerHTML = "<table class=\"pool\"><thead><tr><th>#</th><th>Height</th><th>Voxels</th><th>Pods</th><th>Clear</th></tr></thead><tbody>" + rows + "</tbody></table>"
      + "<dl><dt>Zone</dt><dd>" + kind.zone[0].toFixed(2) + " – " + kind.zone[1].toFixed(2) + "</dd>"
      + "<dt>Mean voxels</dt><dd>" + (grown ? fmtN(voxels / grown) : "—") + " <span>of " + fmtN(VG.TREE_VOXELS) + "</span></dd>"
      + "<dt>Mean pods</dt><dd>" + (grown ? fmtN(pods / grown) : "—") + "</dd></dl>";
    const list = [];
    for (const w of warnings.values()) list.push("Vine " + w.vines.join(", ") + ": " + w.text);
    warnEl.hidden = list.length === 0;
    warnEl.innerHTML = list.map(esc).join("<br>");
    if (!growing) {
      countStatus.innerHTML = "<b>" + esc(kind.name) + "</b> · " + keys.length + " vines · " + fmtN(voxels) + " voxels";
      countStatus.className = "status";
    }
  }

  function vineInfo(ki, pool) {
    const e = entryFor(ki, pool);
    if (!e || !e.vine.ok) return null;
    return { height: e.vine.metres.z, pods: e.vine.counts.markers, voxels: e.vine.counts.total };
  }

  function paintNest() {
    const layout = VG.layoutNest(arch);
    const keys = arch.kinds.map(function (_, k) { return poolKeys(k); });
    const report = VG.nestReport(layout, vineInfo);
    if (view.ok) {
      view.showNest(layout, function (site) {
        const entry = keys[site.kind][site.pool] && cache.get(keys[site.kind][site.pool].key);
        return entry ? entry.model : null;
      }, report);
      frameOnce("nest");
    }
    const byKind = arch.kinds.map(function () { return 0; });
    for (let i = 0; i < layout.sites.length; i++) byKind[layout.sites[i].kind]++;
    const p = layout.params;
    let kindsHtml = "";
    for (let k = 0; k < arch.kinds.length; k++) kindsHtml += "<dt>" + esc(arch.kinds[k].name) + "</dt><dd>" + byKind[k] + "</dd>";
    statsTitle.textContent = "This nest";
    statsEl.innerHTML = "<dl>"
      + "<dt>Vines</dt><dd>" + report.vines + " <span>" + report.models + " models</span></dd>" + kindsHtml
      + "<dt>Hanging pickles</dt><dd>" + fmtN(report.pods) + " <span>" + Math.round(report.pods / VG.HIVE_HANGING * 100) + "% of " + fmtN(VG.HIVE_HANGING) + "</span></dd>"
      + "<dt>Voxels</dt><dd>" + fmtN(report.voxels) + "</dd>"
      + "<dt>Radius</dt><dd>" + p.radiusM.toFixed(0) + " m <span>" + layout.avenues.length + " avenues</span></dd>"
      + "<dt>Swings to heart</dt><dd>" + (report.swingsToHeart >= 0 ? report.swingsToHeart : "none") + "</dd>"
      + "<dt>Linked</dt><dd>" + Math.round(report.largestShare * 100) + "% <span>" + report.links.length + " swings</span></dd>"
      + "<dt>Widest gap</dt><dd>" + report.widestGap.toFixed(0) + " m <span>reach " + report.reach + "</span></dd>"
      + "<dt>Nearest trunks</dt><dd>" + report.nearestTrunks.toFixed(1) + " m</dd>"
      + "</dl>";
    const warnings = growing ? report.warnings.filter(function (w) { return w.indexOf("did not grow") < 0 && w.indexOf("cannot be reached") < 0 && w.indexOf("no neighbour") < 0; }) : report.warnings;
    warnEl.hidden = warnings.length === 0;
    warnEl.innerHTML = warnings.map(esc).join("<br>");
    if (!growing) {
      countStatus.innerHTML = "<b>" + report.vines + "</b> vines · <b>" + fmtN(report.pods) + "</b> pickles";
      countStatus.className = "status";
    }
  }

  function setMode(next) {
    mode = next;
    kindBtn.setAttribute("aria-pressed", mode === "kind" ? "true" : "false");
    nestBtn.setAttribute("aria-pressed", mode === "nest" ? "true" : "false");
    framed = "";
    schedule(0);
  }

  // ---------------------------------------------------------------- files

  function folderText() {
    if (!VG.files.supported) {
      fileStatus.textContent = "Save downloads the file";
      connectBtn.hidden = true;
      reconnectBtn.hidden = true;
      return;
    }
    connectBtn.hidden = VG.files.connected || !!VG.files.pending;
    reconnectBtn.hidden = VG.files.connected || !VG.files.pending;
    fileStatus.textContent = VG.files.connected ? "folder: " + VG.files.name : "Save downloads the file";
  }

  function slug(name) { return name.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_|_$/g, ""); }
  function vineFile(ki, i) { return "prp_vine_" + slug(arch.kinds[ki].name) + "_" + (i + 1); }

  function fail(message) {
    fileStatus.textContent = message;
    fileStatus.className = "status warn";
  }

  async function saveArchetype() {
    const json = VG.archetypeJson(arch);
    try {
      if (VG.files.connected) {
        await VG.files.write("vine_archetype.json", json);
        fileStatus.textContent = "wrote vine_archetype.json";
      } else {
        VG.files.download("vine_archetype.json", json, "application/json");
        fileStatus.textContent = "downloaded vine_archetype.json";
      }
      fileStatus.className = "status";
    } catch (e) {
      fail(e.message || "Could not write the file.");
    }
  }

  // Every pool vine as a .vox with its settings beside it, the nest's layout,
  // the archetype and a report. The game reads the layout, not the archetype.
  async function writeAll() {
    if (writing) return;
    if (!VG.files.connected) { fail("Connect a folder to write the pool and the nest."); return; }
    writing = true;
    writeBtn.disabled = true;
    fileStatus.className = "status";
    try {
      const report = { tool: "dill-control-vine", version: 3, seed: arch.seed, vines: [] };
      const names = [];
      for (let k = 0; k < arch.kinds.length; k++) {
        const keys = poolKeys(k);
        names.push([]);
        for (let i = 0; i < keys.length; i++) {
          fileStatus.textContent = "writing " + vineFile(k, i) + "…";
          await tick();
          let entry = cache.get(keys[i].key);
          if (!entry) {
            const vine = VG.generate(keys[i].params);
            entry = { vine: vine, model: view.ok && vine.voxels.length ? view.makeModel(vine) : null };
            cache.set(keys[i].key, entry);
          }
          const base = vineFile(k, i);
          names[k].push(entry.vine.ok ? base + ".vox" : null);
          if (entry.vine.ok) {
            await VG.files.write(base + ".vox", VG.encodeVox(entry.vine));
            await VG.files.write(base + ".json", VG.presetJson(entry.vine.params));
          }
          const row = VG.describe(entry.vine, entry.vine.ok ? base + ".vox" : null);
          row.kind = arch.kinds[k].name;
          report.vines.push(row);
        }
      }
      const layout = VG.layoutNest(arch);
      const summary = VG.nestReport(layout, vineInfo);
      const nest = {
        tool: "dill-control-vine",
        version: 1,
        seed: arch.seed,
        voxelSize: arch.voxelSize,
        radiusM: layout.params.radiusM,
        avenues: layout.avenues,
        // x and z on the plane tangent at the nest's centre, metres; rotationY
        // is Godot's, in radians. ring -1 is the heart.
        sites: layout.sites.map(function (site) {
          return {
            vine: names[site.kind][site.pool],
            kind: arch.kinds[site.kind].name,
            ring: site.ring,
            x: Math.round(site.x * 100) / 100,
            z: Math.round(site.z * 100) / 100,
            rotationY: Math.round(-site.yaw * 10000) / 10000,
          };
        }),
        report: {
          vines: summary.vines, pods: summary.pods, voxels: summary.voxels,
          swingsToHeart: summary.swingsToHeart, linkedShare: summary.largestShare,
          widestGapM: summary.widestGap, warnings: summary.warnings,
        },
      };
      await VG.files.write("vine_nest.json", JSON.stringify(nest, null, 2));
      await VG.files.write("vine_archetype.json", VG.archetypeJson(arch));
      await VG.files.write("vine_report.json", JSON.stringify(report, null, 2));
      const wrote = report.vines.filter(function (row) { return row.ok; }).length;
      fileStatus.textContent = "wrote " + wrote + " vines, vine_nest.json, vine_archetype.json";
    } catch (e) {
      fail(e.message || "Could not write the files.");
    }
    writing = false;
    writeBtn.disabled = false;
  }

  document.getElementById("loadBtn").addEventListener("click", function () {
    document.getElementById("loadFile").click();
  });
  document.getElementById("loadFile").addEventListener("change", function () {
    const file = this.files && this.files[0];
    this.value = "";
    if (!file) return;
    const reader = new FileReader();
    reader.onload = function () {
      try {
        arch = VG.readArchetype(JSON.parse(String(reader.result)));
        kindIndex = 0;
        buildForm();
        framed = "";
        schedule(0);
        fileStatus.textContent = "loaded " + file.name;
        fileStatus.className = "status";
      } catch (e) {
        fail(e.message || "Could not read that file.");
      }
    };
    reader.readAsText(file);
  });
  document.getElementById("saveBtn").addEventListener("click", saveArchetype);
  writeBtn.addEventListener("click", writeAll);
  connectBtn.addEventListener("click", async function () {
    try {
      await VG.files.connect();
      folderText();
    } catch (e) {
      if (e && e.name === "AbortError") return;
      fail(e.message || "Could not open that folder.");
    }
  });
  reconnectBtn.addEventListener("click", async function () {
    try {
      if (await VG.files.reconnect()) folderText();
    } catch (e) {
      fail(e.message || "Could not reconnect.");
    }
  });

  kindBtn.addEventListener("click", function () {
    if (tab !== "kinds") { tab = "kinds"; buildForm(); }
    setMode("kind");
  });
  nestBtn.addEventListener("click", function () {
    if (tab !== "nest") { tab = "nest"; buildForm(); }
    setMode("nest");
  });
  document.getElementById("frameBtn").addEventListener("click", function () {
    if (view.ok) view.frame();
  });
  document.getElementById("orbitBtn").addEventListener("click", function () {
    if (!view.ok) return;
    if (mode !== "nest") { tab = "nest"; buildForm(); setMode("nest"); }
    view.pullBack();
    framed = "nest";
  });
  document.getElementById("markers").addEventListener("change", function () {
    if (view.ok) view.setMarkers(this.checked);
  });
  document.getElementById("links").addEventListener("change", function () {
    if (view.ok) view.setLinks(this.checked);
  });

  buildForm();
  setMode(mode);
  folderText();
  VG.files.restore().then(folderText).catch(function () { folderText(); });
})(window.VG = window.VG || {});
