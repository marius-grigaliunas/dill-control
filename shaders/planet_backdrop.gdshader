// The menu planet, cel shaded: a few flat colours in hard bands, no grid, no
// hatching, nothing that is there for walking on. Read from orbit.
//
// Colour comes from altitude (sea, shallows, shore, lowland, highland), slope
// (rock), latitude (ice, tundra) and a slow weather noise (lush or dry), all
// taken in the planet's own space, so the look turns with the planet and the
// sun stays where it is. Everything below the sea level is pressed flat to it,
// so the sea is a smooth sphere and the coast is where the land comes through it.
//
// Every edge is wobbled by noise before it is cut, so no line follows the
// mesh's facets or the cube-sphere's grid, and the shore, foam and ice lines
// are a fixed number of pixels wide however steep or flat the ground is.
//
// PlanetBackdrop pushes planet_radius, terrain_amplitude and sea_level, and
// height_map: the ground's height baked into a cube from the planet's own
// heightfield, a height every 10 m or so. Every colour is cut from that, per
// pixel, not from the mesh's height, which is known only at the mesh's corners
// (100 m apart) and blended straight between them: cut from that, the sea's
// depth bands and the coast were chains of straight lines.
//
// Cost (the menu's 60 fps on Iris Xe): a pixel reads the noise about twenty
// times. Where a read's coordinates stay inside the lattice texture's window
// (NoiseLattice, the global planet_noise: cells -64 to 63) it is a texture read of
// the same values, lattice_noise(); the rest of the land's finest scales hash.
// The ice cap is only worked out where its ragged edge can reach.

shader_type spatial;
render_mode cull_back, diffuse_toon, specular_disabled;

uniform float planet_radius = 5000.0;
uniform float terrain_amplitude = 300.0;

// Altitude, -1 the deepest hollow to +1 the highest ridge, at which the sea stands.
uniform float sea_level : hint_range(-1.0, 1.0) = 0.22;

uniform vec3 deep_sea : source_color = vec3(0.04, 0.17, 0.42);
uniform vec3 mid_sea : source_color = vec3(0.07, 0.32, 0.62);
uniform vec3 shallow_sea : source_color = vec3(0.16, 0.56, 0.78);
uniform vec3 foam : source_color = vec3(0.82, 0.93, 0.95);
uniform vec3 shore : source_color = vec3(0.78, 0.72, 0.5);
uniform vec3 forest : source_color = vec3(0.13, 0.36, 0.17);
uniform vec3 lowland : source_color = vec3(0.24, 0.5, 0.2);
uniform vec3 midland : source_color = vec3(0.4, 0.55, 0.22);
uniform vec3 highland : source_color = vec3(0.55, 0.47, 0.3);
uniform vec3 rock : source_color = vec3(0.4, 0.37, 0.35);
uniform vec3 tundra : source_color = vec3(0.45, 0.5, 0.4);
uniform vec3 ice : source_color = vec3(0.92, 0.96, 1.0);
uniform vec3 ice_edge : source_color = vec3(0.62, 0.8, 0.95);

// How flat ground has to be to stay green, and how steep before it is bare rock.
uniform float rock_slope_start : hint_range(0.0, 1.0) = 0.93;
uniform float rock_slope_end : hint_range(0.0, 1.0) = 0.8;

// The coast. Wobble is how far, in altitude units, noise pushes every edge
// cut at the shore; the widths are in pixels, with a ceiling in altitude units
// so a nearly flat coast does not paint a whole plain.
uniform float coast_wobble : hint_range(0.0, 0.3) = 0.07;
uniform float foam_pixels = 2.0;
uniform float foam_max = 0.012;
uniform float sand_pixels = 3.0;
uniform float sand_max = 0.03;

// Sea. The depth bands are the sea floor and never move, as from orbit: the
// shelf round each coast is shallow, past its edge the slope is a narrow band,
// and beyond that the sea is deep and stays deep. Depths below the sea level, in
// terrain amplitudes. What moves is on the surface: the ripples run in over
// the shallows.
uniform float shelf_depth : hint_range(0.0, 1.0) = 0.22;
uniform float slope_depth : hint_range(0.0, 1.0) = 0.3;
uniform float ripple_lines = 4.0;
uniform float ripple_speed = 0.06;

// Forests: where the lowland is dark, and how much of it.
uniform float forest_share : hint_range(0.0, 1.0) = 0.4;

// Ice caps and the tundra round them: where they start (1 is the pole), how
// ragged the edge is (noise, in latitude units), and how wide the tundra is.
uniform float ice_latitude : hint_range(0.0, 1.0) = 0.8;
uniform float ice_ragged = 0.22;
uniform float tundra_width = 0.12;

// A second palette for the dry lands, and where it takes over. The weather is
// a slow noise on the planet's direction, bent by another so the regions are
// not blobs, and pushed dry in the horse latitudes.
uniform vec3 dry_lowland : source_color = vec3(0.7, 0.55, 0.28);
uniform vec3 dry_midland : source_color = vec3(0.78, 0.4, 0.2);
uniform vec3 dry_highland : source_color = vec3(0.55, 0.3, 0.22);
uniform float dry_scale = 2.2;
uniform float dry_share : hint_range(0.0, 1.0) = 0.42;

global uniform sampler3D planet_noise : filter_linear, repeat_enable;
// The noise texture's edge, in lattice cells (NoiseLattice.Edge).
const float NOISE_EDGE = 128.0;

// The ground's height over the mean radius, in terrain_amplitude units, by
// direction in the planet's own space.
uniform samplerCube height_map : filter_linear;

varying vec3 local_dir;
varying vec3 local_normal;

uint hash_bits(uvec3 q) {
	q *= uvec3(1597334673u, 3812015801u, 2798796415u);
	return (q.x ^ q.y ^ q.z) * 1597334673u;
}

float hash_cell(uvec3 q) {
	return float(hash_bits(q)) * (1.0 / 4294967295.0);
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	uvec3 c = uvec3(ivec3(i) + 1024);
	return mix(
		mix(mix(hash_cell(c), hash_cell(c + uvec3(1u, 0u, 0u)), f.x),
			mix(hash_cell(c + uvec3(0u, 1u, 0u)), hash_cell(c + uvec3(1u, 1u, 0u)), f.x), f.y),
		mix(mix(hash_cell(c + uvec3(0u, 0u, 1u)), hash_cell(c + uvec3(1u, 0u, 1u)), f.x),
			mix(hash_cell(c + uvec3(0u, 1u, 1u)), hash_cell(c + uvec3(1u, 1u, 1u)), f.x), f.y),
		f.z);
}

// value_noise() read from the lattice texture: the same values while every
// coordinate stays between -64 and 63, and other values past it.
float lattice_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	return textureLod(planet_noise, (i + f + 0.5) / NOISE_EDGE, 0.0).r;
}

// Three octaves, 0 to 1, centred on a half.
float fbm(vec3 p) {
	return value_noise(p) * 0.55 + value_noise(p * 2.13 + 3.7) * 0.3 + value_noise(p * 4.37 + 9.1) * 0.15;
}

// fbm() from the lattice texture: the hashed values for p no further than 12
// from the origin (its third octave reaches 4.37 times as far, plus 9.1).
float lattice_fbm(vec3 p) {
	return lattice_noise(p) * 0.55 + lattice_noise(p * 2.13 + 3.7) * 0.3 + lattice_noise(p * 4.37 + 9.1) * 0.15;
}

void vertex() {
	float r = length(VERTEX);
	local_dir = VERTEX / r;

	float sea_radius = planet_radius + (sea_level * terrain_amplitude);
	if (r < sea_radius) {
		VERTEX = local_dir * sea_radius;
		NORMAL = local_dir;
	}
	local_normal = NORMAL;
}

void fragment() {
	float ground = textureLod(height_map, local_dir, 0.0).r;
	float depth = ground - sea_level;
	float px = fwidth(depth);

	// Wobble from two scales, about 100 m and 400 m on a 6 km planet, so a straight
	// run of the coast between two mesh vertices is broken up.
	float wobble = ((lattice_noise(local_dir * 55.0) - 0.5) * 0.5 + (lattice_noise(local_dir * 14.0 + 5.0) - 0.5) * 0.9 + (value_noise(local_dir * 150.0 + 2.0) - 0.5) * 0.25) * coast_wobble * 2.0;
	float edge_depth = depth + wobble;

	vec3 colour;

	if (edge_depth < 0.0) {
		float sea_depth = edge_depth;

		colour = mix(deep_sea, mid_sea, step(-slope_depth, sea_depth));
		colour = mix(colour, shallow_sea, step(-shelf_depth, sea_depth));

		// Ripples: pale lines that run along the depth contours and slide toward
		// the shore, a few pixels wide, so the shallows read as moving water.
		float phase = ((depth + ((lattice_fbm(local_dir * 5.0 + 3.0) - 0.5) * 0.3)) * ripple_lines) + (TIME * ripple_speed);
		float ripple_line = step(fract(phase), max(fwidth(phase) * 1.5, 0.03)) * step(-shelf_depth, sea_depth);
		colour = mix(colour, colour + vec3(0.14, 0.17, 0.12), ripple_line);

		// The surf line, a few pixels wide however the sea floor slopes.
		float surf = min(foam_pixels * px, foam_max);
		colour = mix(colour, foam, step(-surf, edge_depth));
	} else {
		float slope = dot(normalize(local_normal), local_dir);

		// Three flat bands of green to brown, their edges bent so they are not contours.
		float band_depth = depth + wobble * 0.7 + (lattice_noise(local_dir * 40.0 + 11.0) - 0.5) * 0.12;
		float band = floor(smoothstep(0.1, 0.85, band_depth) * 3.0) / 2.0;

		// Lush or dry: the weather, bent by a second noise, and drier in the
		// horse latitudes round 30 degrees. A hard edge, so regions read as regions.
		vec3 bend = vec3(lattice_noise(local_dir * 4.0), lattice_noise(local_dir * 4.0 + 17.0), lattice_noise(local_dir * 4.0 + 31.0)) - 0.5;
		float lat = abs(local_dir.y);
		float horse = exp(-pow((lat - 0.45) / 0.16, 2.0)) * 0.22;
		// Inside the texture's window while dry_scale is under 8.
		float weather = lattice_fbm((local_dir + bend * 0.55) * dry_scale) + horse + ((lattice_noise(local_dir * 38.0) - 0.5) * 0.14);
		float dry = step(1.0 - dry_share, weather);
		vec3 low_c = mix(lowland, dry_lowland, dry);
		vec3 mid_c = mix(midland, dry_midland, dry);
		vec3 high_c = mix(highland, dry_highland, dry);

		// Forest on the wet lowlands, in patches.
		float woods = fbm(local_dir * 17.0 + 23.0);
		low_c = mix(low_c, forest, step(1.0 - forest_share, woods) * (1.0 - dry));

		colour = mix(low_c, mid_c, clamp(band * 2.0, 0.0, 1.0));
		colour = mix(colour, high_c, clamp((band - 0.5) * 2.0, 0.0, 1.0));

		// The beach, a few pixels wide however steep the shore is.
		float beach = min(sand_pixels * px, sand_max);
		colour = mix(shore, colour, step(beach, edge_depth));
		colour = mix(rock, colour, smoothstep(rock_slope_end, rock_slope_start, slope));
	}

	// The ice cap. The noise moves its edge at most 1.3 ice_ragged and the
	// ground's height a little more, so nearer the equator than that it is not
	// worked out at all; what is left is what it was.
	if (abs(local_dir.y) + (1.3 * ice_ragged) + 0.06 >= ice_latitude - tundra_width) {
		// Its edge is cut from latitude plus noise at three scales and
		// the ground's height, so it is a ragged coast of its own, with the tundra
		// as a band of cold ground ahead of it.
		float ragged = (fbm(local_dir * 6.0 + 41.0) - 0.5) * 1.6 + (lattice_noise(local_dir * 22.0 + 7.0) - 0.5) * 0.7 + (value_noise(local_dir * 70.0) - 0.5) * 0.3;
		float cold = abs(local_dir.y) + (ragged * ice_ragged) + (ground * 0.04);
		if (edge_depth >= 0.0) {
			colour = mix(colour, tundra, step(ice_latitude - tundra_width, cold) * (1.0 - step(ice_latitude, cold)));
		}
		float cap = step(ice_latitude, cold);
		float rim = min(2.0 * fwidth(cold), 0.02);
		colour = mix(colour, ice_edge, step(ice_latitude, cold) * (1.0 - step(ice_latitude + rim, cold)));
		colour = mix(colour, ice, cap * step(ice_latitude + rim, cold));
	}

	ALBEDO = colour;
	ROUGHNESS = 1.0;
}
