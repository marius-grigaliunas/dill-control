// Ink outlines as one full-screen pass, from the depth buffer alone.
//
// Screen space on purpose: inverted-hull outlines cost a second draw of every
// mesh, and with twenty-five thousand pickles that is the most expensive way
// there is to draw a line. Here the cost is five depth reads a pixel whatever
// is on screen.
//
// Depth only, and drawn as alpha-blended ink rather than a recomposited
// screen. The first version also read the normal buffer (for creases) and
// the screen texture (to band the colours): measured at 5-6 ms of GPU on the
// target Iris Xe, because the first makes Forward+ render a normal buffer
// every frame and the second copies the whole screen with mipmaps. The bands
// belong in the materials' own toon lighting, which costs nothing extra.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

uniform vec3 ink_color : source_color = vec3(0.05, 0.04, 0.06);

// Line width in pixels, at 1080p; scaled to the viewport height.
uniform float thickness : hint_range(0.5, 4.0) = 2.0;

// A depth step counts as an edge when it is this share of the nearer depth.
// Relative, so a line means the same thing at 2 m and at 200 m.
uniform float depth_threshold : hint_range(0.005, 0.5) = 0.06;

// Lines fade out past this distance, so the horizon does not turn into a
// solid black band where every far pickle meets every other.
uniform float ink_fade_start = 70.0;
uniform float ink_fade_end = 160.0;

void vertex() {
	// A two-unit quad pinned to the whole screen; it never moves.
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// Linear depth from a raw sample with four scalars instead of a mat4 product.
// A camera with no frustum offset has an inverse projection whose z and w
// rows do not depend on screen x and y, so view z / view w is
// (a*raw + b) / (c*raw + d) -- the same answer, for a multiply-add and a
// divide rather than sixteen multiplies, five times a pixel.
float linear_depth(float raw, vec4 k) {
	// The sky sits at the far plane, which with an infinite projection is a
	// divide by zero; a large finite depth keeps the arithmetic sane.
	return min(-(k.x * raw + k.y) / max(k.z * raw + k.w, 1e-6), 100000.0);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 texel = (1.0 / VIEWPORT_SIZE) * thickness * (VIEWPORT_SIZE.y / 1080.0);
	vec4 k = vec4(INV_PROJECTION_MATRIX[2].z, INV_PROJECTION_MATRIX[3].z,
	              INV_PROJECTION_MATRIX[2].w, INV_PROJECTION_MATRIX[3].w);

	float dc = linear_depth(texture(depth_tex, uv).r, k);
	float dl = linear_depth(texture(depth_tex, uv - vec2(texel.x, 0.0)).r, k);
	float dr = linear_depth(texture(depth_tex, uv + vec2(texel.x, 0.0)).r, k);
	float du = linear_depth(texture(depth_tex, uv - vec2(0.0, texel.y)).r, k);
	float dd = linear_depth(texture(depth_tex, uv + vec2(0.0, texel.y)).r, k);

	// Silhouettes: a depth step against any neighbour, as a share of the
	// nearer of the two depths, inked on both sides. Inking only the nearer
	// side was tried first and drew a one-pixel line on the object itself --
	// dark ink on a dark gun, invisible -- so the line straddles the step and
	// always shows against whatever is behind.
	float near_l = min(dc, dl);
	float near_r = min(dc, dr);
	float near_u = min(dc, du);
	float near_d = min(dc, dd);
	float jump = max(
		max(abs(dl - dc) / max(near_l, 0.05), abs(dr - dc) / max(near_r, 0.05)),
		max(abs(du - dc) / max(near_u, 0.05), abs(dd - dc) / max(near_d, 0.05)));
	float silhouette = smoothstep(depth_threshold, depth_threshold * 1.6, jump);

	// Creases from the depth alone: on a flat face the depth changes
	// linearly, so the second difference is near zero; at a fold it is not.
	float bend = abs((dl + dr) - (2.0 * dc)) + abs((du + dd) - (2.0 * dc));
	float crease = smoothstep(depth_threshold * 0.5, depth_threshold, bend / max(dc, 0.05));

	float edge = max(silhouette, crease * 0.8);
	// Faded by the nearer depth round this pixel, so a far-side pixel of a
	// near silhouette (sky behind a tree) keeps its ink.
	float near_all = min(min(min(dl, dr), min(du, dd)), dc);
	edge *= 1.0 - smoothstep(ink_fade_start, ink_fade_end, near_all);

	// Most of the screen is not an edge. Dropping those pixels skips the
	// blend's read-modify-write over the whole frame, which on an integrated
	// GPU sharing memory with the CPU is bandwidth worth having.
	if (edge < 0.004) {
		discard;
	}

	ALBEDO = ink_color;
	ALPHA = edge;
}
