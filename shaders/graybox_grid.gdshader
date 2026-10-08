// Gray-box surface shader: flat value plus a world-space grid.
//
// PLACEHOLDER, from the lab. Deleted when the gym and the flats get real art.
//
// The grid is not decoration. Judging whether movement feels fast, whether a
// sightline is long, or whether a landing punch reads at all is close to
// impossible on untextured gray -- there is no optical flow. One-metre cells
// give the eye something to measure against, and because they are world-space
// and triplanar they need no UVs on primitive meshes.
shader_type spatial;
// diffuse_toon: a hard light/shadow terminator, the art direction's toon
// bands at no cost over Burley. The ink pass (toon_ink.gdshader) draws the lines.
render_mode cull_back, diffuse_toon, specular_schlick_ggx;

uniform vec3 base_color : source_color = vec3(0.32, 0.33, 0.34);
uniform vec3 line_color : source_color = vec3(0.20, 0.21, 0.22);
uniform vec3 major_line_color : source_color = vec3(0.46, 0.47, 0.48);
uniform float cell_size = 1.0;
uniform int major_every = 8;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.92;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;

// Curved surfaces set this. Picking one plane per fragment is free and seamless
// on boxes, but on a sphere it cuts the surface into six cube faces that seam at
// the 45-degree normal boundaries and smear into stripes at the edge of each --
// exactly where the grid is supposed to be telling you how fast you are moving.
// Off by default so every existing box material keeps the cheaper path.
uniform bool triplanar_blend = false;

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// Antialiased grid line coverage for coordinates already divided by the cell
// size, with the screen-space width of one cell handed in.
//
// The width used to be taken here with fwidth(), which meant every caller paid
// for its own derivatives -- two sets on the cheap path and six on the
// triplanar one, for what are only one and three distinct projections. fwidth
// is linear, so the major grid's width is exactly the minor grid's divided by
// major_every. Same reasoning, and the same shape, as planet_terrain.gdshader.
float grid_coverage(vec2 scaled, vec2 width) {
	vec2 distance_to_line = abs(fract(scaled - 0.5) - 0.5) / max(width, vec2(0.0001));
	return 1.0 - min(min(distance_to_line.x, distance_to_line.y), 1.0);
}

void fragment() {
	float inv_cell = 1.0 / cell_size;
	float inv_major = 1.0 / float(major_every);
	float minor;
	float major;

	if (triplanar_blend) {
		// Blend the three projections by a sharpened normal weight. The blend
		// is on the antialiased coverage rather than on colour: it is cheaper
		// and it keeps the line width right where two projections meet.
		//
		// Squared twice rather than pow(x, 4.0): identical for a non-negative
		// input, and it trades three log/exp pairs for two multiplies.
		vec3 w = abs(world_normal);
		w = w * w;
		w = w * w;
		w /= (w.x + w.y + w.z);

		vec2 scaled_xz = world_position.xz * inv_cell;
		vec2 scaled_zy = world_position.zy * inv_cell;
		vec2 scaled_xy = world_position.xy * inv_cell;

		vec2 width_xz = fwidth(scaled_xz);
		vec2 width_zy = fwidth(scaled_zy);
		vec2 width_xy = fwidth(scaled_xy);

		minor = (w.y * grid_coverage(scaled_xz, width_xz))
			  + (w.x * grid_coverage(scaled_zy, width_zy))
			  + (w.z * grid_coverage(scaled_xy, width_xy));
		major = (w.y * grid_coverage(scaled_xz * inv_major, width_xz * inv_major))
			  + (w.x * grid_coverage(scaled_zy * inv_major, width_zy * inv_major))
			  + (w.z * grid_coverage(scaled_xy * inv_major, width_xy * inv_major));
	} else {
		// Project onto whichever plane the surface most faces. Cheaper than
		// blending and there is no seam worth caring about on boxes.
		vec3 n = abs(world_normal);
		vec2 coord;
		if (n.y >= n.x && n.y >= n.z) {
			coord = world_position.xz;
		} else if (n.x >= n.z) {
			coord = world_position.zy;
		} else {
			coord = world_position.xy;
		}

		// One set of derivatives for both scales. This is the path every box in
		// the graybox takes, so it is the one that covers the level.
		vec2 scaled = coord * inv_cell;
		vec2 width = fwidth(scaled);

		minor = grid_coverage(scaled, width);
		major = grid_coverage(scaled * inv_major, width * inv_major);
	}

	vec3 albedo = mix(base_color, line_color, minor);
	albedo = mix(albedo, major_line_color, major);

	ALBEDO = albedo;
	ROUGHNESS = roughness_value;
	METALLIC = metallic_value;
}
