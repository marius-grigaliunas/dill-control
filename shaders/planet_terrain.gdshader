// PLACEHOLDER -- replaced by real terrain materials in the art pass.
//
// Ported from the lab's planet terrain, without the wetness field (the
// coolant and the swamp it watered stayed in the lab). What is left:
//
// 1. Ground that is not one flat tone. Slope picks between soil and rock,
//    altitude between low and high ground, and value noise breaks up the rest.
//    Arithmetic rather than textures. A biome (decision 0021) sets these
//    colours; nothing else about the shader changes per planet.
// 2. The graybox grid, triplanar so it does not smear at the cube-face
//    diagonals, fading with distance before it aliases. It gives untextured
//    ground the optical flow you judge speed by.
// 3. Snow: altitude and flatness blended to a cold colour.
// 4. The dig floor: crater walls darken to scorched rock and the floor is
//    magma, read from the vertex colour PlanetTerrain writes.
//
// Everything assumes the planet is centred on the world origin (decision
// 0018): up at a point is normalize(world_position). PlanetTerrain pushes
// planet_radius and terrain_amplitude at build.

shader_type spatial;
// diffuse_toon: a hard light/shadow terminator, the art direction's toon
// bands at no cost over Burley. The ink pass (toon_ink.gdshader) draws the lines.
render_mode cull_back, diffuse_toon, specular_schlick_ggx;

uniform vec3 base_color : source_color = vec3(0.46, 0.44, 0.35);
uniform vec3 line_color : source_color = vec3(0.34, 0.33, 0.27);
uniform vec3 major_line_color : source_color = vec3(0.58, 0.56, 0.46);

// Terrain palette. Low ground silts up, high ground is scoured back toward
// rock, and anything steep is rock regardless of how high it is.
uniform vec3 low_color : source_color = vec3(0.22, 0.31, 0.15);
uniform vec3 high_color : source_color = vec3(0.58, 0.55, 0.45);
uniform vec3 rock_color : source_color = vec3(0.32, 0.30, 0.29);

// Dry scree, scattered over the soil in noise-shaped patches.
uniform vec3 scree_color : source_color = vec3(0.44, 0.31, 0.19);
uniform float patch_strength : hint_range(0.0, 1.0) = 0.85;
uniform vec3 snow_color : source_color = vec3(0.86, 0.89, 0.94);

uniform float cell_size = 4.0;
uniform int major_every = 8;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.94;

// Pushed in by the level. Altitude blending has to know what sea level is and
// how much relief there is to normalise against; hard-coding either would tie
// the material to one planet's radius.
uniform float planet_radius = 400.0;
uniform float terrain_amplitude = 14.0;

// How much the surface normal has to agree with up before the ground stops
// being treated as cliff. 1.0 is flat, 0.0 is a vertical wall.
uniform float rock_slope_start : hint_range(0.0, 1.0) = 0.965;
uniform float rock_slope_end : hint_range(0.0, 1.0) = 0.80;

// Where snow starts, as a fraction of the amplitude above mean radius, and how
// flat a surface has to be to hold any.
uniform float snow_altitude : hint_range(-1.0, 1.0) = 0.55;
uniform float snow_slope : hint_range(0.0, 1.0) = 0.80;
uniform float snow_amount : hint_range(0.0, 1.0) = 0.75;

// Macro variation: metres per unit of the first octave, and how far it is
// allowed to push the colour.
uniform float macro_scale = 6.5;
uniform float macro_strength : hint_range(0.0, 1.0) = 0.22;

// Below this triplanar weight a projection is dropped instead of evaluated.
// See the note where it is used for why this is not a quality setting.
const float grid_weight_epsilon = 0.004;

// Metres over which the grid fades out.
uniform float grid_fade_distance = 130.0;
uniform float grid_strength : hint_range(0.0, 1.0) = 0.7;

// ---- the dig floor ----
// How far a vertex has been dug toward PlanetTerrain.DigFloorDepth arrives as
// 1 - COLOR.r: 0 on ground nobody has touched, 1 on the floor. Inverted
// because a chunk with no colour array reads COLOR as white, and every chunk
// nobody has dug carries none. The walls darken to scorched rock on the way
// down, and the floor itself is magma.
uniform vec3 dig_rock_color : source_color = vec3(0.17, 0.13, 0.12);
uniform vec3 magma_crust_color : source_color = vec3(0.10, 0.06, 0.05);
uniform vec3 magma_hot_color : source_color = vec3(0.95, 0.22, 0.03);
uniform vec3 magma_core_color : source_color = vec3(1.0, 0.55, 0.08);
uniform float magma_start : hint_range(0.5, 1.0) = 0.88;
uniform float magma_energy = 1.1;

varying vec3 world_position;
varying vec3 world_normal;
varying float dug;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	dug = 1.0 - COLOR.r;
}

// Antialiased grid line coverage for a set of 2D coordinates, already divided
// by the cell size, with the screen-space width of one cell handed in.
//
// The width used to be taken here with fwidth(), which meant six sets of
// derivatives per fragment for what are only three distinct projections at two
// scales. fwidth is linear, so the major grid's width is exactly the minor
// grid's divided by major_every -- three fwidth pairs and three divides do the
// work the other three were duplicating.
float grid_coverage(vec2 scaled, vec2 width) {
	vec2 distance_to_line = abs(fract(scaled - 0.5) - 0.5) / max(width, vec2(0.0001));
	return 1.0 - min(min(distance_to_line.x, distance_to_line.y), 1.0);
}

// Integer bit-mixing rather than the usual fract(p * 0.1031) float hash.
//
// The float hash is fine for small inputs and quietly stops being a hash for
// large ones. A 2 m cell on a 400 m planet puts cell indices in the hundreds,
// and there the three components of fract(p * 0.1031) stay nearly equal from
// one cell to the next, the multiply-and-fract chain fails to decorrelate
// them, and the result correlates hard and biases high. Measured, not
// guessed: with that hash every octave finer than about ten metres came back
// as a flat constant, which is why the terrain stayed one colour through
// three attempts to vary it.
//
// This one is exact at every magnitude the planet can produce. The +1024
// carries negative coordinates into unsigned range.
uint hash_bits(uvec3 q) {
	q *= uvec3(1597334673u, 3812015801u, 2798796415u);
	return (q.x ^ q.y ^ q.z) * 1597334673u;
}

// Takes the integer cell rather than a position, so value_noise can step its
// eight corners with an integer add instead of eight floor-and-convert chains.
// The +1024 that carries negative coordinates into unsigned range is applied
// once by the caller.
float hash_cell(uvec3 q) {
	return float(hash_bits(q)) * (1.0 / 4294967295.0);
}

// Trilinear value noise. Cheap, band-limited enough for macro tinting, and it
// needs no texture -- which matters because there is nothing to import one with.
float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	f = f * f * (3.0 - 2.0 * f);

	uvec3 c = uvec3(ivec3(i) + 1024);

	float n000 = hash_cell(c);
	float n100 = hash_cell(c + uvec3(1u, 0u, 0u));
	float n010 = hash_cell(c + uvec3(0u, 1u, 0u));
	float n110 = hash_cell(c + uvec3(1u, 1u, 0u));
	float n001 = hash_cell(c + uvec3(0u, 0u, 1u));
	float n101 = hash_cell(c + uvec3(1u, 0u, 1u));
	float n011 = hash_cell(c + uvec3(0u, 1u, 1u));
	float n111 = hash_cell(c + uvec3(1u, 1u, 1u));

	return mix(
		mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
		mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y),
		f.z);
}

void fragment() {
	// One length() for both the up vector and the altitude, which were each
	// taking their own square root of the same position.
	float distance_from_centre = length(world_position);
	vec3 up = world_position / max(distance_from_centre, 0.0001);
	vec3 normal = normalize(world_normal);

	// -1 at the deepest hollow, +1 at the highest ridge.
	float altitude = clamp(
		(distance_from_centre - planet_radius) / max(terrain_amplitude, 0.001),
		-1.0, 1.0);

	// ---- macro variation ----
	// Three octaves, each centred on zero *before* they are weighted.
	//
	// Summing them raw and re-centring afterwards was the first attempt and it
	// produced almost nothing: value noise clusters around its own mean, so
	// three of them added together cluster harder still, and the result never
	// moved the colour more than a few percent. Centring each octave first
	// keeps the spread.
	//
	// Sampled in world space rather than in face coordinates, so the pattern
	// crosses a cube-sphere seam without a visible join -- the same reason the
	// heightfield itself is generated from the 3D direction.
	float n1 = value_noise(world_position / macro_scale);
	float n2 = value_noise(world_position * (3.4 / macro_scale));
	float n3 = value_noise(world_position * (9.0 / macro_scale));

	// Weighted toward the coarse octave for the patch shape, with the finer
	// two left to roughen its edge. Kept in 0..1 rather than centred, because
	// the thresholds below are easier to reason about that way.
	float macro = clamp((n1 * 0.6) + (n2 * 0.28) + (n3 * 0.12), 0.0, 1.0);

	// ---- palette ----
	vec3 ground = mix(low_color, high_color, smoothstep(-0.7, 1.0, altitude));
	ground = mix(ground, base_color, 0.25);

	// Patches of dry scree over the soil. This is what actually reads as
	// varied ground: a second material in a genuinely different hue, appearing
	// in noise-shaped patches. Brightness variation alone reads as uneven
	// lighting on one flat colour, which is exactly what several attempts at
	// it looked like -- the two colours have to be far enough apart to survive
	// the tonemapper.
	float patch = smoothstep(0.32, 0.58, macro);
	ground = mix(ground, scree_color, patch * patch_strength);

	// And a second, finer break-up so the patches are not the only scale in
	// the picture.
	ground *= 0.86 + (n3 * 0.28);

	// Slope rock on top, which is a real cliff rather than a patch.
	float slope = dot(normal, up);
	float rock = 1.0 - smoothstep(rock_slope_end, rock_slope_start, slope);
	vec3 albedo = mix(ground, rock_color * (1.0 + (macro * 0.25)), rock);

	// Snow lies on high, flat ground, and the noise makes its edge ragged
	// rather than a contour drawn round the planet.
	float snow = smoothstep(snow_altitude, snow_altitude + 0.3, altitude + (macro * 0.4))
			   * smoothstep(snow_slope - 0.15, snow_slope + 0.1, slope)
			   * snow_amount;
	albedo = mix(albedo, snow_color, snow);

	float rough = mix(roughness_value, 0.6, snow);

	// ---- grid ----
	float fade = 1.0 - smoothstep(grid_fade_distance * 0.35, grid_fade_distance, length(VERTEX));
	float grid_mix = grid_strength * fade;

	if (grid_mix > 0.001) {
		// Blend the three projections by a sharpened normal weight, on the
		// antialiased coverage rather than on colour: cheaper, and it keeps the
		// line width right where two projections meet.
		//
		// Squared twice rather than pow(x, 4.0): identical for a non-negative
		// input, and it trades three log/exp pairs for two multiplies.
		vec3 w = abs(normal);
		w = w * w;
		w = w * w;
		w /= (w.x + w.y + w.z);

		float inv_cell = 1.0 / cell_size;
		vec2 scaled_xz = world_position.xz * inv_cell;
		vec2 scaled_zy = world_position.zy * inv_cell;
		vec2 scaled_xy = world_position.xy * inv_cell;

		vec2 width_xz = fwidth(scaled_xz);
		vec2 width_zy = fwidth(scaled_zy);
		vec2 width_xy = fwidth(scaled_xy);

		float inv_major = 1.0 / float(major_every);

		// Only the projections that can be seen.
		//
		// The weights are a normal raised to the fourth and normalised, so on
		// ground this smooth one axis dominates hard and the other two are
		// usually thousandths. Evaluating all three was six antialiased grid
		// samples a pixel over the whole of the visible ground, to add
		// quantities below the precision of the colour they tint. Below the
		// epsilon a projection can shift a grid line's darkness by less than
		// half a percent, which is under one step of an 8-bit channel.
		//
		// The derivatives above are taken outside this, unconditionally: fwidth
		// in divergent control flow is undefined, and the three fwidth pairs
		// are the part that has to stay uniform. What is skipped here is only
		// the arithmetic that consumes them.
		//
		// The branch is coherent. The surface normal varies smoothly across a
		// screenful of sphere, so a wavefront takes the same side of it.
		float minor = 0.0;
		float major = 0.0;

		if (w.y > grid_weight_epsilon) {
			minor += w.y * grid_coverage(scaled_xz, width_xz);
			major += w.y * grid_coverage(scaled_xz * inv_major, width_xz * inv_major);
		}
		if (w.x > grid_weight_epsilon) {
			minor += w.x * grid_coverage(scaled_zy, width_zy);
			major += w.x * grid_coverage(scaled_zy * inv_major, width_zy * inv_major);
		}
		if (w.z > grid_weight_epsilon) {
			minor += w.z * grid_coverage(scaled_xy, width_xy);
			major += w.z * grid_coverage(scaled_xy * inv_major, width_xy * inv_major);
		}

		albedo = mix(albedo, line_color, minor * grid_mix);
		albedo = mix(albedo, major_line_color, major * grid_mix);
	}

	// ---- the dig floor ----
	// Coherent: almost every pixel on the planet has dug == 0 and skips all of it.
	if (dug > 0.02) {
		albedo = mix(albedo, dig_rock_color, smoothstep(0.3, 0.85, dug) * 0.85);

		float magma = smoothstep(magma_start, 0.99, dug);
		if (magma > 0.0) {
			// Slow churn: crust islands drifting over a hot body. World-space,
			// so it crosses chunk and face seams like the rest of the ground.
			float flow = value_noise(world_position * 0.32 + vec3(0.0, TIME * 0.35, TIME * 0.12));
			float crust = smoothstep(0.46, 0.56, flow + ((1.0 - magma) * 0.45));
			vec3 hot = mix(magma_core_color, magma_hot_color, smoothstep(0.15, 0.45, flow));
			albedo = mix(albedo, mix(hot, magma_crust_color, crust), magma);
			rough = mix(rough, 0.55, magma);
			EMISSION = hot * magma_energy * magma * (1.0 - (crust * 0.9));
		}
	}

	ALBEDO = albedo;
	ROUGHNESS = rough;
	METALLIC = 0.0;
}

// ---- toon light with ink hatching in the shadow band ----
//
// The art direction asked for ink inside the surfaces as well as round
// them: hatching where the light does not reach. A custom light() so the
// strokes follow the actual terminator and the actual shadows (ATTENUATION
// carries the shadow maps), which a fragment-stage pattern cannot know.
// Strokes are anchored to the world, not the screen, so they stay painted on
// the ground as the view turns, and fade out where they would shimmer.
uniform float hatch_spacing : hint_range(0.1, 4.0) = 0.4;
uniform float hatch_width : hint_range(0.05, 0.9) = 0.18;
uniform float hatch_strength : hint_range(0.0, 1.0) = 0.85;
uniform float hatch_fade_start = 18.0;
uniform float hatch_fade_end = 55.0;

void light() {
	float ndl = dot(NORMAL, LIGHT);
	// One hard terminator, as diffuse_toon draws it, with shadows honoured.
	float lit = smoothstep(0.0, 0.04, ndl) * ATTENUATION;
	DIFFUSE_LIGHT += LIGHT_COLOR * lit / PI;

	// Hatching: diagonal strokes across the ground, only where unlit, as a
	// negative light that darkens the ambient fill along each stroke.
	float shadow = 1.0 - lit;
	if (shadow > 0.01) {
		float along = dot(world_position, normalize(vec3(1.0, 0.35, 0.7))) / hatch_spacing;
		float width = fwidth(along);
		float stroke = 1.0 - smoothstep(hatch_width - width, hatch_width + width, abs(fract(along) - 0.5) * 2.0);
		// Fade with distance and where strokes would be thinner than a pixel.
		float fade = (1.0 - smoothstep(hatch_fade_start, hatch_fade_end, -(VIEW_MATRIX * vec4(world_position, 1.0)).z))
		             * (1.0 - smoothstep(0.35, 0.7, width));
		DIFFUSE_LIGHT -= vec3(0.4) * stroke * shadow * fade * hatch_strength;
	}
}
