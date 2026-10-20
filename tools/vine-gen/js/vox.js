// MagicaVoxel .vox, version 150. The chunk layout matches tools/vox-tools/make_graybox.py,
// which the importer already reads: SIZE/XYZI pairs, then the scene graph, RGBA, MATL.
//
// One model is the stalk, leaves and tendrils together, named Stalk, standing on
// z = 0. Each pod is a one-voxel shape named @PodNN that reuses a single marker
// model; the importer turns those into Marker3D nodes and does not draw them.
(function (VG) {
  "use strict";

  const encoder = new TextEncoder();

  function concat(parts) {
    let n = 0;
    for (let i = 0; i < parts.length; i++) n += parts[i].length;
    const out = new Uint8Array(n);
    let o = 0;
    for (let i = 0; i < parts.length; i++) {
      out.set(parts[i], o);
      o += parts[i].length;
    }
    return out;
  }

  function ascii(text) {
    const out = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i++) out[i] = text.charCodeAt(i);
    return out;
  }

  function i32s(values) {
    const out = new Uint8Array(values.length * 4);
    const view = new DataView(out.buffer);
    for (let i = 0; i < values.length; i++) view.setInt32(i * 4, values[i] | 0, true);
    return out;
  }

  function str(text) {
    const bytes = encoder.encode(text);
    return concat([i32s([bytes.length]), bytes]);
  }

  function dict(obj) {
    const keys = Object.keys(obj);
    const parts = [i32s([keys.length])];
    for (let i = 0; i < keys.length; i++) {
      parts.push(str(keys[i]), str(String(obj[keys[i]])));
    }
    return concat(parts);
  }

  function chunk(id, content, children) {
    const kids = children || new Uint8Array(0);
    const head = new Uint8Array(8);
    const view = new DataView(head.buffer);
    view.setInt32(0, content.length, true);
    view.setInt32(4, kids.length, true);
    return concat([ascii(id), head, content, kids]);
  }

  function xyzi(voxels) {
    const body = new Uint8Array(4 + voxels.length * 4);
    const view = new DataView(body.buffer);
    view.setInt32(0, voxels.length, true);
    for (let i = 0; i < voxels.length; i++) {
      const voxel = voxels[i];
      const o = 4 + i * 4;
      body[o] = voxel.x;
      body[o + 1] = voxel.y;
      body[o + 2] = voxel.z;
      body[o + 3] = voxel.c;
    }
    return body;
  }

  function rgba() {
    const out = new Uint8Array(256 * 4);
    const colours = VG.PALETTE_RGB;
    for (const key in colours) {
      const index = Number(key);
      const rgb = colours[key];
      const o = (index - 1) * 4;
      out[o] = rgb[0];
      out[o + 1] = rgb[1];
      out[o + 2] = rgb[2];
      out[o + 3] = 255;
    }
    return out;
  }

  function transform(id, name, child, layer, translation) {
    const frame = translation ? { _t: translation } : {};
    const attributes = name ? { _name: name } : {};
    return chunk("nTRN", concat([
      i32s([id]),
      dict(attributes),
      i32s([child, -1, layer, 1]),
      dict(frame),
    ]));
  }

  function shape(id, model) {
    return chunk("nSHP", concat([
      i32s([id]),
      dict({}),
      i32s([1, model]),
      dict({}),
    ]));
  }

  VG.encodeVox = function (vine) {
    if (!vine || !vine.ok) return null;
    const sx = vine.bounds.x;
    const sy = vine.bounds.y;
    const sz = vine.bounds.z;
    const parts = [
      chunk("SIZE", i32s([sx, sy, sz])),
      chunk("XYZI", xyzi(vine.voxels)),
    ];
    if (vine.markers.length) {
      parts.push(chunk("SIZE", i32s([1, 1, 1])));
      parts.push(chunk("XYZI", xyzi([{ x: 0, y: 0, z: 0, c: VG.PALETTE.SHELL }])));
    }

    const childIds = [2];
    for (let i = 0; i < vine.markers.length; i++) childIds.push(4 + i * 2);

    parts.push(transform(0, "", 1, -1, ""));
    parts.push(chunk("nGRP", concat([
      i32s([1]),
      dict({}),
      i32s([childIds.length]),
      i32s(childIds),
    ])));
    parts.push(transform(2, "Stalk", 3, 0, "0 0 " + (sz >> 1)));
    parts.push(shape(3, 0));

    const halfX = sx >> 1;
    const halfY = sy >> 1;
    for (let i = 0; i < vine.markers.length; i++) {
      const marker = vine.markers[i];
      const id = 4 + i * 2;
      // A 1×1×1 model's centre is the voxel's corner. Shift by half a voxel
      // so the marker sits on the same centre the stalk voxel would.
      const tx = marker.x - halfX;
      const ty = marker.y - halfY;
      const tz = marker.z;
      parts.push(transform(id, marker.name, id + 1, 0, tx + " " + ty + " " + tz));
      parts.push(shape(id + 1, 1));
    }

    parts.push(chunk("RGBA", rgba()));
    const materials = VG.MATERIAL;
    for (const key in materials) {
      parts.push(chunk("MATL", concat([
        i32s([Number(key)]),
        dict({ _type: materials[key] }),
      ])));
    }

    return concat([ascii("VOX "), i32s([150]), chunk("MAIN", new Uint8Array(0), concat(parts))]);
  };

  VG.presetJson = function (params) {
    return JSON.stringify({ tool: "dill-control-vine", version: VG.PRESET_VERSION, params: VG.clampParams(params) }, null, 2);
  };
})(window.VG = window.VG || {});
