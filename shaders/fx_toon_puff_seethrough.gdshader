// EXPERIMENT: fx_toon_puff with ways to see through the dust. Swapped onto
// the dust at runtime by DustLab (scripts/debug), chosen by a word after
// "--" on the command line: dust=lens, dust=shell, dust=lensshell,
// dust=thin, dust=near. The game itself still draws fx_toon_puff; a mode
// that earns its place is folded into that shader and DustStats.
//
// Drawn after the ink pass, not before it: the ink is found from depth, and
// every hole left in a puff that writes depth was inked, so thinned dust read
// as black speckle. This dust writes no depth (it is in the transparent pass,
// with ALPHA 1 and no blending to pay for), sits above the ink by render
// priority (DustLab), and inks its own outline: a ring ink_px wide round each
// puff, drawn back to front so nearer puffs overlap farther ones as lobes.
// It is thinned by leaving pixels out, never by blending.
//
//   solid      (no mode) the puffs as they are, with their own outline: to
//              compare against fx_toon_puff
//   lens       an ordered dither that thins the puffs toward the middle of
//              the screen, where the player looks, to lens_min of them; the
//              edges of the view stay full cloud
//   shell      every puff a ring: its outline band drawn, its middle open
//   lensshell  rings in the lens, full puffs outside it
//   thin       the same dither everywhere, to thin_cover
//   near       puffs close to the camera thinned, far ones whole: inside a
//              cloud you see out, from outside it is a cloud
shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled, shadows_disabled;

uniform float shadow_strength : hint_range(0.0, 1.0) = 0.62;
uniform vec4 shadow_tint : source_color = vec4(0.78, 0.74, 0.95, 1.0);
uniform vec2 light_offset = vec2(-0.22, -0.28);
uniform vec4 tint : source_color = vec4(1.0);
uniform float lumpiness : hint_range(0.0, 0.5) = 0.0;
uniform float lump_count : hint_range(2.0, 12.0) = 5.0;

// 0 solid, 1 lens, 2 shell, 3 lensshell, 4 thin, 5 near.
uniform int see_mode = 0;
// The lens: how much of the dust is left at its middle, and where it starts
// and ends, as a share of the screen's height from its centre.
uniform float lens_min : hint_range(0.0, 1.0) = 0.3;
uniform float lens_inner : hint_range(0.0, 1.0) = 0.12;
uniform float lens_outer : hint_range(0.0, 1.5) = 0.45;
// Shell: the ring's width as a share of the puff's radius.
uniform float shell_width : hint_range(0.02, 1.0) = 0.22;
// Thin: how much of the dust is drawn everywhere.
uniform float thin_cover : hint_range(0.0, 1.0) = 0.5;
// Near: within near_start metres a puff is gone; past near_end, whole.
uniform float near_start = 6.0;
uniform float near_end = 30.0;
// The dither's cell, in pixels: 1 is a fine screen door, 3-4 reads as
// halftone, which the comic look takes better than single-pixel noise.
uniform float dither_px : hint_range(1.0, 8.0) = 3.0;
// The puff's own outline, in pixels, and its colour (toon_ink's).
uniform float ink_px : hint_range(0.0, 6.0) = 2.0;
uniform vec3 ink_color : source_color = vec3(0.05, 0.04, 0.06);

varying float v_seed;
varying float v_depth;

void vertex() {
	mat4 world = mat4(
		normalize(INV_VIEW_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]),
		MODEL_MATRIX[3]);
	world = world * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
		vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	v_seed = float(INSTANCE_ID) * 2.399 + INSTANCE_CUSTOM.x;
	v_depth = -(MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).z;
}

float lumps(float angle, float seed) {
	float a = sin(angle * lump_count + seed);
	float b = sin(angle * (lump_count + 3.0) + seed * 1.7);
	return clamp(0.5 + 0.35 * a + 0.15 * b, 0.0, 1.0);
}

// 4x4 Bayer threshold, 0..1, in cells of dither_px pixels.
float bayer(vec2 frag) {
	ivec2 c = ivec2(floor(frag / dither_px)) & ivec2(3);
	int i = c.x + c.y * 4;
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[i]) + 0.5) / 16.0;
}

// 0 at the middle of the screen to 1 at the lens's edge and beyond.
float lens(vec2 frag, vec2 view) {
	vec2 d = (frag - view * 0.5) / view.y;
	return smoothstep(lens_inner, lens_outer, length(d));
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float size = clamp(COLOR.a, 0.0, 1.0);
	float angle = atan(p.y, p.x);
	float edge = size * (1.0 - lumpiness * (1.0 - lumps(angle, v_seed)));
	if (r > edge) {
		discard;
	}

	float cover = 1.0;
	if (see_mode == 1) {
		cover = mix(lens_min, 1.0, lens(FRAGCOORD.xy, VIEWPORT_SIZE));
	} else if (see_mode == 2) {
		if (r < edge * (1.0 - shell_width)) {
			discard;
		}
	} else if (see_mode == 3) {
		// The ring narrows to shell_width at the middle, widens to whole outside.
		float w = mix(shell_width, 1.0, lens(FRAGCOORD.xy, VIEWPORT_SIZE));
		if (r < edge * (1.0 - w)) {
			discard;
		}
	} else if (see_mode == 4) {
		cover = thin_cover;
	} else if (see_mode == 5) {
		cover = smoothstep(near_start, near_end, v_depth);
	}
	if (cover < 1.0 && bayer(FRAGCOORD.xy) >= cover) {
		discard;
	}

	ALPHA = 1.0;
	// Outline: within ink_px pixels of the puff's edge (fwidth turns UV into
	// pixels, so it is as thick far off as near).
	float px = max(fwidth(r), 1e-5);
	vec2 q = p - light_offset * size;
	float fold = 1.0 - lumpiness * 0.8 * (1.0 - lumps(atan(q.y, q.x), v_seed * 1.3 + 2.0));
	bool lit = length(q) < size * 0.82 * fold;
	vec3 c = COLOR.rgb * tint.rgb;
	if (!lit) {
		c *= mix(vec3(1.0), shadow_tint.rgb, shadow_strength) * (1.0 - shadow_strength * 0.45);
	}
	ALBEDO = edge - r < ink_px * px ? ink_color : c;
}
