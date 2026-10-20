// The preview. WebGL2, no library. Voxels are instanced cubes in Godot's space
// (Y up; MagicaVoxel Z up and -Y front, the same map as VoxSpace). Each grown
// vine is one model on the GPU, at three levels of detail with its hidden
// voxels left out; each placement of it is one draw with its own transform,
// so a nest of a hundred vines costs no more memory than its pool. The planet
// is the 1200 m test planet. The camera is the Godot 3D viewport: scroll
// dollies, middle mouse pans, right mouse is freelook, Alt + left mouse orbits.
//
// Positions are subtracted from the camera, and the fragment shader writes a
// logarithmic depth, so a 0.5 m voxel and the whole planet share one view.
(function (VG) {
  "use strict";

  const INSTANCED = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNor;
layout(location=2) in vec3 iCenter;
layout(location=3) in vec3 iColor;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCam;
uniform mat3 uBasis;
uniform vec3 uOrigin;
uniform vec3 uBox;
out vec3 vCol;
out vec3 vNor;
void main() {
  vec3 world = uOrigin + uBasis * (iCenter + aPos * uBox);
  vec4 view = uView * vec4(world - uCam, 1.0);
  gl_Position = uProj * view;
  vNor = normalize(uBasis * aNor);
  vCol = iColor;
}`;

  const LINE = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCam;
void main() {
  gl_Position = uProj * (uView * vec4(aPos - uCam, 1.0));
}`;

  const LINE_FRAG = `#version 300 es
precision highp float;
uniform float uFar;
uniform vec3 uColor;
out vec4 oCol;
void main() {
  oCol = vec4(uColor, 1.0);
  float dist = 1.0 / gl_FragCoord.w;
  gl_FragDepth = log2(dist + 1.0) / log2(uFar + 1.0) - 0.0005;
}`;

  const MESH = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNor;
uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCam;
out vec3 vNor;
out vec3 vWorld;
void main() {
  vec4 view = uView * vec4(aPos - uCam, 1.0);
  gl_Position = uProj * view;
  vNor = aNor;
  vWorld = aPos;
}`;

  const TOON = `#version 300 es
precision highp float;
in vec3 vCol;
in vec3 vNor;
uniform float uFar;
uniform vec3 uLight;
out vec4 oCol;
void main() {
  float ndl = dot(normalize(vNor), uLight);
  float band = ndl > 0.12 ? 1.0 : 0.4;
  oCol = vec4(vCol * (0.16 + 0.84 * band), 1.0);
  // gl_FragCoord.w is 1/clip.w after clipping, so this stays correct on the
  // ground plane where vertices fall behind the camera. Hardware depth with
  // a 40 km far plane rounds an 80 m vine to 1 and it loses to the clear.
  float dist = 1.0 / gl_FragCoord.w;
  gl_FragDepth = log2(dist + 1.0) / log2(uFar + 1.0);
}`;

  const PLANET = `#version 300 es
precision highp float;
in vec3 vNor;
in vec3 vWorld;
uniform float uFar;
uniform vec3 uLight;
uniform float uGround;
out vec4 oCol;
void main() {
  vec3 n = normalize(vNor);
  float ndl = dot(n, uLight);
  float band = ndl > 0.05 ? 1.0 : 0.45;
  vec3 col;
  if (uGround > 0.5) {
    float minor = step(0.94, fract(vWorld.x * 0.25)) + step(0.94, fract(vWorld.z * 0.25));
    float major = step(0.97, fract(vWorld.x * 0.05)) + step(0.97, fract(vWorld.z * 0.05));
    col = mix(vec3(0.55, 0.58, 0.42), vec3(0.28, 0.32, 0.22), clamp(minor * 0.45 + major * 0.7, 0.0, 1.0));
  } else {
    float height = n.y * 0.5 + 0.5;
    float mottled = sin(vWorld.x * 0.008) * sin(vWorld.z * 0.007);
    col = mix(vec3(0.48, 0.40, 0.24), vec3(0.36, 0.52, 0.30), clamp(height + mottled * 0.08, 0.0, 1.0));
  }
  oCol = vec4(col * (0.2 + 0.8 * band), 1.0);
  float dist = 1.0 / gl_FragCoord.w;
  gl_FragDepth = log2(dist + 1.0) / log2(uFar + 1.0);
}`;

  const FAR = 40000;
  const FOV = Math.PI / 3;
  const LIGHT = norm({ x: 0.32, y: 0.86, z: 0.28 });
  const MARKER = [1, 0.69, 0.18];
  const LINK = [0.95, 0.35, 0.85];
  // Past these, a placement draws its coarser model: 2 then 4 voxels a cell.
  const LOD_MID = 260;
  const LOD_FAR = 900;
  const PODS_NEAR = 420;

  function add(a, b) { return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z }; }
  function sub(a, b) { return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }; }
  function scale(a, s) { return { x: a.x * s, y: a.y * s, z: a.z * s }; }
  function dot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
  function cross(a, b) {
    return { x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x };
  }
  function len(a) { return Math.hypot(a.x, a.y, a.z); }
  function norm(a) {
    const l = len(a);
    return l < 1e-8 ? { x: 0, y: 1, z: 0 } : scale(a, 1 / l);
  }
  function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

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

  function toGodot(voxel, foot, vs) {
    return {
      x: (voxel.x + 0.5 - foot.x) * vs,
      y: (voxel.z + 0.5) * vs,
      z: -((voxel.y + 0.5 - foot.y) * vs),
    };
  }

  function identityPlace() {
    return {
      x: { x: 1, y: 0, z: 0 },
      y: { x: 0, y: 1, z: 0 },
      z: { x: 0, y: 0, z: 1 },
      apply: function (local) { return local; },
    };
  }

  function planetPlace(dir) {
    const up = norm(dir);
    const ref = Math.abs(up.y) < 0.95 ? { x: 0, y: 1, z: 0 } : { x: 1, y: 0, z: 0 };
    const east = norm(cross(ref, up));
    const north = norm(cross(up, east));
    const origin = scale(up, VG.PLANET_RADIUS);
    return {
      x: east,
      y: up,
      z: north,
      apply: function (local) {
        return add(origin, add(scale(east, local.x), add(scale(up, local.y), scale(north, local.z))));
      },
    };
  }

  // The nest's centre on the test planet: up, tilted towards the default camera.
  const NEST_UP = norm({ x: 0.18, y: 0.95, z: 0.26 });

  // A placement's frame: the vine's +x turned by yaw towards the plane's +z,
  // the same convention as VG.layoutNest.
  function yawed(place, yaw) {
    const c = Math.cos(yaw);
    const s = Math.sin(yaw);
    const x = add(scale(place.x, c), scale(place.z, s));
    const z = add(scale(place.x, -s), scale(place.z, c));
    return { x: x, y: place.y, z: z };
  }

  function nestPlace(x, z) {
    const centre = planetPlace(NEST_UP);
    const dir = add(scale(NEST_UP, VG.PLANET_RADIUS), add(scale(centre.x, x), scale(centre.z, z)));
    return planetPlace(dir);
  }

  function cubeMesh() {
    const faces = [
      { n: [1, 0, 0], q: [[1, -1, -1], [1, -1, 1], [1, 1, 1], [1, 1, -1]] },
      { n: [-1, 0, 0], q: [[-1, -1, 1], [-1, -1, -1], [-1, 1, -1], [-1, 1, 1]] },
      { n: [0, 1, 0], q: [[-1, 1, -1], [1, 1, -1], [1, 1, 1], [-1, 1, 1]] },
      { n: [0, -1, 0], q: [[-1, -1, 1], [1, -1, 1], [1, -1, -1], [-1, -1, -1]] },
      { n: [0, 0, 1], q: [[-1, -1, 1], [-1, 1, 1], [1, 1, 1], [1, -1, 1]] },
      { n: [0, 0, -1], q: [[1, -1, -1], [1, 1, -1], [-1, 1, -1], [-1, -1, -1]] },
    ];
    const data = [];
    for (let f = 0; f < faces.length; f++) {
      const face = faces[f];
      const order = [0, 1, 2, 0, 2, 3];
      for (let k = 0; k < order.length; k++) {
        const p = face.q[order[k]];
        data.push(p[0] * 0.5, p[1] * 0.5, p[2] * 0.5, face.n[0], face.n[1], face.n[2]);
      }
    }
    return new Float32Array(data);
  }

  function sphereMesh(stacks, slices, radius) {
    const data = [];
    function vert(phi, theta) {
      const y = Math.cos(phi);
      const s = Math.sin(phi);
      const x = s * Math.cos(theta);
      const z = s * Math.sin(theta);
      data.push(x * radius, y * radius, z * radius, x, y, z);
    }
    for (let i = 0; i < stacks; i++) {
      const p0 = Math.PI * i / stacks;
      const p1 = Math.PI * (i + 1) / stacks;
      for (let j = 0; j < slices; j++) {
        const t0 = Math.PI * 2 * j / slices;
        const t1 = Math.PI * 2 * (j + 1) / slices;
        vert(p0, t0); vert(p1, t1); vert(p0, t1);
        vert(p0, t0); vert(p1, t0); vert(p1, t1);
      }
    }
    return new Float32Array(data);
  }

  function groundMesh(size) {
    const y = -0.05;
    const s = size;
    const n = [0, 1, 0];
    const corners = [[-s, y, -s], [s, y, -s], [s, y, s], [-s, y, s]];
    const order = [0, 1, 2, 0, 2, 3];
    const data = [];
    for (let i = 0; i < order.length; i++) {
      const p = corners[order[i]];
      data.push(p[0], p[1], p[2], n[0], n[1], n[2]);
    }
    return new Float32Array(data);
  }

  function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      const log = gl.getShaderInfoLog(shader);
      gl.deleteShader(shader);
      throw new Error(log);
    }
    return shader;
  }

  function program(gl, vs, fs) {
    const prog = gl.createProgram();
    gl.attachShader(prog, compile(gl, gl.VERTEX_SHADER, vs));
    gl.attachShader(prog, compile(gl, gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      throw new Error(gl.getProgramInfoLog(prog));
    }
    return prog;
  }

  function perspective(aspect) {
    const out = new Float32Array(16);
    const f = 1 / Math.tan(FOV / 2);
    const near = 0.05;
    out[0] = f / aspect;
    out[5] = f;
    out[10] = (FAR + near) / (near - FAR);
    out[11] = -1;
    out[14] = (2 * FAR * near) / (near - FAR);
    return out;
  }

  function meshBuffer(gl, data) {
    const vao = gl.createVertexArray();
    const buf = gl.createBuffer();
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 24, 0);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 24, 12);
    gl.bindVertexArray(null);
    return { vao: vao, count: data.length / 6 };
  }

  function instanceVao(gl, cube, buffer) {
    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, cube);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 24, 0);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 24, 12);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    for (let loc = 0; loc < 2; loc++) {
      gl.enableVertexAttribArray(2 + loc);
      gl.vertexAttribPointer(2 + loc, 3, gl.FLOAT, false, 24, loc * 12);
      gl.vertexAttribDivisor(2 + loc, 1);
    }
    gl.bindVertexArray(null);
    return vao;
  }

  // The voxels of a level of detail: cells of k voxels a side, each taking the
  // colour of its first voxel, and only cells with an empty face.
  function surface(vine, k) {
    const cells = new Map();
    const voxels = vine.voxels;
    for (let i = 0; i < voxels.length; i++) {
      const v = voxels[i];
      const x = Math.floor(v.x / k);
      const y = Math.floor(v.y / k);
      const z = Math.floor(v.z / k);
      const key = x + "," + y + "," + z;
      if (!cells.has(key)) cells.set(key, { x: x, y: y, z: z, c: v.c });
    }
    const out = [];
    for (const cell of cells.values()) {
      if (cells.has((cell.x + 1) + "," + cell.y + "," + cell.z) && cells.has((cell.x - 1) + "," + cell.y + "," + cell.z)
        && cells.has(cell.x + "," + (cell.y + 1) + "," + cell.z) && cells.has(cell.x + "," + (cell.y - 1) + "," + cell.z)
        && cells.has(cell.x + "," + cell.y + "," + (cell.z + 1)) && cells.has(cell.x + "," + cell.y + "," + (cell.z - 1))) continue;
      out.push(cell);
    }
    const vs = vine.voxelSize;
    const data = new Float32Array(out.length * 6);
    let w = 0;
    for (let i = 0; i < out.length; i++) {
      const cell = out[i];
      const rgb = VG.PALETTE_RGB[cell.c] || [255, 255, 255];
      data[w++] = ((cell.x + 0.5) * k - vine.foot.x) * vs;
      data[w++] = (cell.z + 0.5) * k * vs;
      data[w++] = -(((cell.y + 0.5) * k - vine.foot.y) * vs);
      data[w++] = rgb[0] / 255; data[w++] = rgb[1] / 255; data[w++] = rgb[2] / 255;
    }
    return data;
  }

  VG.createView = function (canvas, hooks) {
    const gl = canvas.getContext("webgl2", { antialias: true, alpha: false, depth: true });
    if (!gl) {
      return { ok: false, error: "This browser has no WebGL2. The vine can still be saved; the preview needs it." };
    }

    let instProg;
    let meshProg;
    let lineProg;
    try {
      instProg = program(gl, INSTANCED, TOON);
      meshProg = program(gl, MESH, PLANET);
      lineProg = program(gl, LINE, LINE_FRAG);
    } catch (e) {
      return { ok: false, error: "The preview shader failed to compile. " + e.message };
    }

    const cube = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, cube);
    gl.bufferData(gl.ARRAY_BUFFER, cubeMesh(), gl.STATIC_DRAW);
    const planet = meshBuffer(gl, sphereMesh(64, 96, VG.PLANET_RADIUS));
    const ground = meshBuffer(gl, groundMesh(1200));
    const lineBuf = gl.createBuffer();
    const lineVao = gl.createVertexArray();
    gl.bindVertexArray(lineVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, lineBuf);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 12, 0);
    gl.bindVertexArray(null);

    const view = {
      ok: true,
      mode: "floor",
      showMarkers: true,
      showLinks: false,
      speed: 32,
      pos: { x: 18, y: 24, z: 46 },
      yaw: 0,
      pitch: -0.35,
      placements: [],
      lineCount: 0,
      focus: { x: 0, y: 30, z: 0 },
      extent: 40,
      aabb: null,
      drawn: 0,
    };

    const keys = new Set();
    let rmb = false;
    let mmb = false;
    let orbiting = false;
    let pivot = { x: 0, y: 30, z: 0 };
    let lastNote = "";
    let lastFrame = 0;
    const proj = new Float32Array(16);
    const viewMat = new Float32Array(16);

    function note() {
      const text = Math.round(view.speed) + " m/s";
      if (text === lastNote || !hooks || !hooks.onSpeed) return;
      lastNote = text;
      hooks.onSpeed(text);
    }

    function typing() {
      const el = document.activeElement;
      if (!el || el === document.body || el === canvas) return false;
      const tag = el.tagName;
      return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
    }

    function forward() {
      const cp = Math.cos(view.pitch);
      const sp = Math.sin(view.pitch);
      const cy = Math.cos(view.yaw);
      const sy = Math.sin(view.yaw);
      return { x: sy * cp, y: sp, z: -cy * cp };
    }

    function axes() {
      const fwd = forward();
      // Near the surface, up is the planet's radius so a vine stands upright.
      // Far enough to see the whole planet, up stays world Y.
      let upRef = { x: 0, y: 1, z: 0 };
      if (view.mode === "planet" && len(view.pos) < VG.PLANET_RADIUS * 1.8) upRef = norm(view.pos);
      let right = cross(fwd, upRef);
      if (len(right) < 1e-4) right = cross(fwd, Math.abs(upRef.y) < 0.9 ? { x: 0, y: 1, z: 0 } : { x: 1, y: 0, z: 0 });
      right = norm(right);
      const up = cross(right, fwd);
      return { fwd: fwd, right: right, up: up };
    }

    function aim(from, to) {
      const dir = norm(sub(to, from));
      view.pitch = Math.asin(clamp(dir.y, -1, 1));
      view.yaw = Math.atan2(dir.x, -dir.z);
    }

    function resize() {
      const dpr = Math.min(2, window.devicePixelRatio || 1);
      const w = Math.max(1, canvas.clientWidth);
      const h = Math.max(1, canvas.clientHeight);
      const pw = Math.floor(w * dpr);
      const ph = Math.floor(h * dpr);
      if (canvas.width !== pw || canvas.height !== ph) {
        canvas.width = pw;
        canvas.height = ph;
      }
    }

    function writeView() {
      const ax = axes();
      viewMat[0] = ax.right.x; viewMat[1] = ax.up.x; viewMat[2] = -ax.fwd.x; viewMat[3] = 0;
      viewMat[4] = ax.right.y; viewMat[5] = ax.up.y; viewMat[6] = -ax.fwd.y; viewMat[7] = 0;
      viewMat[8] = ax.right.z; viewMat[9] = ax.up.z; viewMat[10] = -ax.fwd.z; viewMat[11] = 0;
      viewMat[12] = 0; viewMat[13] = 0; viewMat[14] = 0; viewMat[15] = 1;
    }

    function ray(clientX, clientY) {
      const rect = canvas.getBoundingClientRect();
      const x = ((clientX - rect.left) / rect.width) * 2 - 1;
      const y = 1 - ((clientY - rect.top) / rect.height) * 2;
      const ax = axes();
      const tan = Math.tan(FOV / 2);
      const aspect = rect.width / Math.max(1, rect.height);
      return {
        origin: view.pos,
        dir: norm(add(ax.fwd, add(scale(ax.right, x * tan * aspect), scale(ax.up, y * tan)))),
      };
    }

    function raySphere(origin, dir, radius) {
      const b = dot(origin, dir);
      const c = dot(origin, origin) - radius * radius;
      const h = b * b - c;
      if (h < 0) return null;
      const s = Math.sqrt(h);
      if (-b - s > 0.05) return -b - s;
      if (-b + s > 0.05) return -b + s;
      return null;
    }

    function rayAabb(origin, dir, box) {
      if (!box) return null;
      let tMin = 0;
      let tMax = 1e9;
      const o = [origin.x, origin.y, origin.z];
      const d = [dir.x, dir.y, dir.z];
      for (let i = 0; i < 3; i++) {
        if (Math.abs(d[i]) < 1e-8) {
          if (o[i] < box.min[i] || o[i] > box.max[i]) return null;
          continue;
        }
        let t1 = (box.min[i] - o[i]) / d[i];
        let t2 = (box.max[i] - o[i]) / d[i];
        if (t1 > t2) { const swap = t1; t1 = t2; t2 = swap; }
        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;
        if (tMin > tMax) return null;
      }
      return tMin > 0.05 ? tMin : (tMax > 0.05 ? tMax : null);
    }

    function pick(clientX, clientY) {
      const shot = ray(clientX, clientY);
      let best = null;
      function consider(t) {
        if (t != null && (best == null || t < best)) best = t;
      }
      if (view.mode === "planet") consider(raySphere(shot.origin, shot.dir, VG.PLANET_RADIUS));
      else if (shot.dir.y < -1e-4 || shot.dir.y > 1e-4) {
        const t = -shot.origin.y / shot.dir.y;
        if (t > 0.05) consider(t);
      }
      consider(rayAabb(shot.origin, shot.dir, view.aabb));
      return best == null ? null : add(shot.origin, scale(shot.dir, best));
    }

    function focusPoint() {
      return view.focus;
    }

    function upload(data) {
      const buffer = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      return { buffer: buffer, vao: instanceVao(gl, cube, buffer), count: data.length / 6 };
    }

    // A grown vine on the GPU. The page keeps these while their settings are
    // in use and hands them back with dropModel.
    view.makeModel = function (vine) {
      const vs = vine.voxelSize;
      const pods = new Float32Array(vine.markers.length * 6);
      const drop = VG.PICKLE_HEIGHT_M * 0.5 - vs * 0.5;
      let w = 0;
      for (let i = 0; i < vine.markers.length; i++) {
        const top = toGodot(vine.markers[i], vine.foot, vs);
        pods[w++] = top.x; pods[w++] = top.y - drop; pods[w++] = top.z;
        pods[w++] = MARKER[0]; pods[w++] = MARKER[1]; pods[w++] = MARKER[2];
      }
      return {
        voxelSize: vs,
        height: vine.metres.z,
        reach: Math.max(vine.metres.x, vine.metres.y, vine.metres.z),
        width: Math.max(vine.metres.x, vine.metres.y),
        lods: [upload(surface(vine, 1)), upload(surface(vine, 2)), upload(surface(vine, 4))],
        pods: upload(pods),
      };
    };

    view.dropModel = function (model) {
      if (!model) return;
      const all = model.lods.concat([model.pods]);
      for (let i = 0; i < all.length; i++) {
        gl.deleteBuffer(all[i].buffer);
        gl.deleteVertexArray(all[i].vao);
      }
    };

    function bound(placements) {
      const box = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] };
      for (let i = 0; i < placements.length; i++) {
        const p = placements[i];
        if (!p.model) continue;
        const top = add(p.origin, scale(p.basis.y, p.model.height));
        const r = p.model.reach * 0.5;
        const pts = [p.origin, top];
        for (let k = 0; k < pts.length; k++) {
          box.min[0] = Math.min(box.min[0], pts[k].x - r); box.max[0] = Math.max(box.max[0], pts[k].x + r);
          box.min[1] = Math.min(box.min[1], pts[k].y - r); box.max[1] = Math.max(box.max[1], pts[k].y + r);
          box.min[2] = Math.min(box.min[2], pts[k].z - r); box.max[2] = Math.max(box.max[2], pts[k].z + r);
        }
      }
      return Number.isFinite(box.min[0]) ? box : null;
    }

    function sendLines(points) {
      gl.bindBuffer(gl.ARRAY_BUFFER, lineBuf);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(points), gl.DYNAMIC_DRAW);
      view.lineCount = points.length / 3;
    }

    // A kind's pool stood in a row on the floor.
    view.showLineup = function (models) {
      view.mode = "floor";
      let width = 0;
      let height = 10;
      for (let i = 0; i < models.length; i++) {
        if (!models[i]) continue;
        width = Math.max(width, models[i].width * 0.8);
        height = Math.max(height, models[i].height);
      }
      const gap = Math.max(20, width);
      const placements = [];
      for (let i = 0; i < models.length; i++) {
        const x = (i - (models.length - 1) / 2) * gap;
        placements.push({ model: models[i], origin: { x: x, y: 0, z: 0 }, basis: identityPlace() });
      }
      view.placements = placements;
      view.focus = { x: 0, y: height * 0.45, z: 0 };
      // The row's full width, outer vines included.
      view.extent = gap * (models.length - 1) + width * 1.25;
      view.aabb = bound(placements);
      sendLines([]);
    };

    // The nest on the planet. modelOf(site) is the grown vine for a site, or
    // null while it grows; anchors are the report's, for the grapple links.
    view.showNest = function (layout, modelOf, report) {
      view.mode = "planet";
      const placements = [];
      for (let i = 0; i < layout.sites.length; i++) {
        const site = layout.sites[i];
        const place = nestPlace(site.x, site.z);
        placements.push({ model: modelOf(site), origin: place.apply({ x: 0, y: 0, z: 0 }), basis: yawed(place, site.yaw), site: site });
      }
      view.placements = placements;
      const radius = layout.params.radiusM;
      view.focus = add(scale(NEST_UP, VG.PLANET_RADIUS), scale(NEST_UP, 40));
      view.extent = radius;
      view.aabb = bound(placements);
      const points = [];
      if (report) {
        for (let k = 0; k < report.links.length; k++) {
          const a = report.anchors[report.links[k][0]];
          const b = report.anchors[report.links[k][1]];
          if (!a || !b) continue;
          const pa = nestPlace(a.x, a.z).apply({ x: 0, y: a.y, z: 0 });
          const pb = nestPlace(b.x, b.z).apply({ x: 0, y: b.y, z: 0 });
          points.push(pa.x, pa.y, pa.z, pb.x, pb.y, pb.z);
        }
      }
      sendLines(points);
    };

    view.setMarkers = function (on) { view.showMarkers = !!on; };
    view.setLinks = function (on) { view.showLinks = !!on; };

    // Frame what is shown: the lineup from the front, the nest from above its edge.
    view.frame = function () {
      if (view.mode === "planet") {
        const centre = planetPlace(NEST_UP);
        const r = view.extent;
        const ground = scale(NEST_UP, VG.PLANET_RADIUS);
        view.pos = add(ground, add(scale(centre.z, r * 1.75), add(scale(centre.x, r * 0.4), scale(NEST_UP, r * 1.05))));
        aim(view.pos, add(ground, scale(NEST_UP, 30)));
        view.speed = clamp(r * 0.4, 20, 200);
      } else {
        const height = Math.max(8, view.focus.y / 0.45);
        const span = view.extent;
        const aspect = Math.max(0.3, canvas.clientWidth / Math.max(1, canvas.clientHeight));
        const distance = Math.max(height * 0.62, span * 0.55 / aspect) / Math.tan(FOV / 2) + 8;
        view.pos = { x: span * 0.08, y: height * 0.55, z: distance };
        aim(view.pos, { x: 0, y: height * 0.5, z: 0 });
        view.speed = clamp(height * 0.45, 8, 80);
      }
      note();
    };

    view.pullBack = function () {
      const dist = VG.PLANET_RADIUS * 2.4;
      view.pos = scale(norm(add(NEST_UP, { x: 0.25, y: 0.1, z: 0.55 })), dist);
      aim(view.pos, { x: 0, y: 0, z: 0 });
      view.speed = 480;
      note();
    };

    function move(dt) {
      if (!rmb || typing()) return;
      const ax = axes();
      const step = view.speed * (keys.has("shift") ? 4 : 1) * dt;
      let delta = { x: 0, y: 0, z: 0 };
      if (keys.has("w") || keys.has("arrowup")) delta = add(delta, ax.fwd);
      if (keys.has("s") || keys.has("arrowdown")) delta = sub(delta, ax.fwd);
      if (keys.has("d") || keys.has("arrowright")) delta = add(delta, ax.right);
      if (keys.has("a") || keys.has("arrowleft")) delta = sub(delta, ax.right);
      if (keys.has("e")) delta = add(delta, ax.up);
      if (keys.has("q")) delta = sub(delta, ax.up);
      if (len(delta) > 0) view.pos = add(view.pos, scale(norm(delta), step));
    }

    function draw() {
      resize();
      const aspect = canvas.width / Math.max(1, canvas.height);
      const p = perspective(aspect);
      for (let i = 0; i < 16; i++) proj[i] = p[i];
      writeView();

      gl.viewport(0, 0, canvas.width, canvas.height);
      gl.clearColor(0.62, 0.74, 0.78, 1);
      gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
      gl.enable(gl.DEPTH_TEST);

      gl.useProgram(meshProg);
      gl.uniformMatrix4fv(gl.getUniformLocation(meshProg, "uView"), false, viewMat);
      gl.uniformMatrix4fv(gl.getUniformLocation(meshProg, "uProj"), false, proj);
      gl.uniform3f(gl.getUniformLocation(meshProg, "uCam"), view.pos.x, view.pos.y, view.pos.z);
      gl.uniform1f(gl.getUniformLocation(meshProg, "uFar"), FAR);
      gl.uniform3f(gl.getUniformLocation(meshProg, "uLight"), LIGHT.x, LIGHT.y, LIGHT.z);
      const groundLoc = gl.getUniformLocation(meshProg, "uGround");
      if (view.mode === "planet") {
        gl.uniform1f(groundLoc, 0);
        gl.bindVertexArray(planet.vao);
        gl.drawArrays(gl.TRIANGLES, 0, planet.count);
      } else {
        gl.uniform1f(groundLoc, 1);
        gl.bindVertexArray(ground.vao);
        gl.drawArrays(gl.TRIANGLES, 0, ground.count);
      }

      gl.useProgram(instProg);
      gl.uniformMatrix4fv(gl.getUniformLocation(instProg, "uView"), false, viewMat);
      gl.uniformMatrix4fv(gl.getUniformLocation(instProg, "uProj"), false, proj);
      gl.uniform3f(gl.getUniformLocation(instProg, "uCam"), view.pos.x, view.pos.y, view.pos.z);
      gl.uniform1f(gl.getUniformLocation(instProg, "uFar"), FAR);
      gl.uniform3f(gl.getUniformLocation(instProg, "uLight"), LIGHT.x, LIGHT.y, LIGHT.z);
      const basisLoc = gl.getUniformLocation(instProg, "uBasis");
      const originLoc = gl.getUniformLocation(instProg, "uOrigin");
      const boxLoc = gl.getUniformLocation(instProg, "uBox");
      const fwd = forward();
      const basis = new Float32Array(9);
      let drawn = 0;
      for (let i = 0; i < view.placements.length; i++) {
        const p = view.placements[i];
        const model = p.model;
        if (!model) continue;
        const mid = add(p.origin, scale(p.basis.y, model.height * 0.5));
        const to = sub(mid, view.pos);
        const dist = len(to);
        // Behind the camera by more than the vine's size: not drawn.
        if (dot(to, fwd) < -model.reach) continue;
        const lod = view.mode === "floor" || dist < LOD_MID ? 0 : dist < LOD_FAR ? 1 : 2;
        const level = model.lods[lod];
        const k = lod === 0 ? 1 : lod === 1 ? 2 : 4;
        basis[0] = p.basis.x.x; basis[1] = p.basis.x.y; basis[2] = p.basis.x.z;
        basis[3] = p.basis.y.x; basis[4] = p.basis.y.y; basis[5] = p.basis.y.z;
        basis[6] = p.basis.z.x; basis[7] = p.basis.z.y; basis[8] = p.basis.z.z;
        gl.uniformMatrix3fv(basisLoc, false, basis);
        gl.uniform3f(originLoc, p.origin.x, p.origin.y, p.origin.z);
        if (level.count) {
          const box = model.voxelSize * k;
          gl.uniform3f(boxLoc, box, box, box);
          gl.bindVertexArray(level.vao);
          gl.drawArraysInstanced(gl.TRIANGLES, 0, 36, level.count);
          drawn += level.count;
        }
        if (view.showMarkers && model.pods.count && (view.mode === "floor" || dist < PODS_NEAR)) {
          gl.uniform3f(boxLoc, 0.8, VG.PICKLE_HEIGHT_M, 0.8);
          gl.bindVertexArray(model.pods.vao);
          gl.drawArraysInstanced(gl.TRIANGLES, 0, 36, model.pods.count);
        }
      }
      view.drawn = drawn;
      if (view.showLinks && view.lineCount && view.mode === "planet") {
        gl.useProgram(lineProg);
        gl.uniformMatrix4fv(gl.getUniformLocation(lineProg, "uView"), false, viewMat);
        gl.uniformMatrix4fv(gl.getUniformLocation(lineProg, "uProj"), false, proj);
        gl.uniform3f(gl.getUniformLocation(lineProg, "uCam"), view.pos.x, view.pos.y, view.pos.z);
        gl.uniform1f(gl.getUniformLocation(lineProg, "uFar"), FAR);
        gl.uniform3f(gl.getUniformLocation(lineProg, "uColor"), LINK[0], LINK[1], LINK[2]);
        gl.bindVertexArray(lineVao);
        gl.drawArrays(gl.LINES, 0, view.lineCount);
      }
      gl.bindVertexArray(null);
    }

    function frameLoop(now) {
      const dt = Math.min(0.05, lastFrame ? (now - lastFrame) / 1000 : 0);
      lastFrame = now;
      move(dt);
      draw();
      requestAnimationFrame(frameLoop);
    }

    canvas.addEventListener("contextmenu", function (event) { event.preventDefault(); });
    canvas.addEventListener("pointerdown", function (event) {
      if (typing()) document.activeElement.blur();
      canvas.focus();
      if (event.button === 2) {
        rmb = true;
        if (canvas.requestPointerLock) canvas.requestPointerLock();
      } else if (event.button === 1) {
        mmb = true;
        event.preventDefault();
      } else if (event.button === 0 && event.altKey) {
        orbiting = true;
        pivot = pick(event.clientX, event.clientY) || focusPoint();
      }
    });
    window.addEventListener("pointerup", function (event) {
      if (event.button === 2) {
        rmb = false;
        if (document.exitPointerLock && document.pointerLockElement === canvas) document.exitPointerLock();
      } else if (event.button === 1) {
        mmb = false;
      } else if (event.button === 0) {
        orbiting = false;
      }
    });
    window.addEventListener("pointermove", function (event) {
      const dx = event.movementX || 0;
      const dy = event.movementY || 0;
      if (rmb) {
        view.yaw += dx * 0.003;
        view.pitch = clamp(view.pitch - dy * 0.003, -1.45, 1.45);
      } else if (mmb) {
        const ax = axes();
        const step = view.speed * 0.0016;
        view.pos = add(view.pos, add(scale(ax.right, -dx * step), scale(ax.up, dy * step)));
      } else if (orbiting) {
        const offset = sub(view.pos, pivot);
        const yaw = -dx * 0.005;
        const c = Math.cos(yaw);
        const s = Math.sin(yaw);
        let spun = { x: offset.x * c - offset.z * s, y: offset.y, z: offset.x * s + offset.z * c };
        let right = cross(norm(spun), { x: 0, y: 1, z: 0 });
        if (len(right) > 1e-4) {
          spun = rotateAround(spun, norm(right), -dy * 0.005);
        }
        if (len(spun) < 0.3) spun = scale(norm(spun), 0.3);
        view.pos = add(pivot, spun);
        aim(view.pos, pivot);
      }
    });
    canvas.addEventListener("wheel", function (event) {
      event.preventDefault();
      if (rmb) {
        view.speed = clamp(view.speed * Math.exp(-event.deltaY * 0.0015), 0.4, 8000);
        note();
        return;
      }
      // Scroll up zooms in, the same as the Godot viewport. deltaY is positive downward.
      const zoom = clamp(-event.deltaY / 100, -4, 4);
      const hit = pick(event.clientX, event.clientY);
      if (hit) {
        const to = sub(hit, view.pos);
        const dist = len(to);
        const dir = scale(to, 1 / (dist || 1));
        const step = dist * 0.12 * zoom;
        const travel = zoom > 0 ? Math.min(step, Math.max(0, dist - 0.4)) : step;
        view.pos = add(view.pos, scale(dir, travel));
      } else {
        view.pos = add(view.pos, scale(forward(), view.speed * 0.35 * zoom));
      }
    }, { passive: false });

    window.addEventListener("keydown", function (event) {
      if (typing()) return;
      keys.add(event.key.toLowerCase());
      if (event.key.toLowerCase() === "f") {
        event.preventDefault();
        view.frame();
      }
    });
    window.addEventListener("keyup", function (event) {
      keys.delete(event.key.toLowerCase());
    });
    window.addEventListener("blur", function () {
      keys.clear();
      rmb = false;
      mmb = false;
      orbiting = false;
    });

    if (typeof ResizeObserver === "function") {
      new ResizeObserver(resize).observe(canvas);
    } else {
      window.addEventListener("resize", resize);
    }
    canvas.tabIndex = 0;
    resize();
    note();
    requestAnimationFrame(frameLoop);
    return view;
  };
})(window.VG = window.VG || {});
