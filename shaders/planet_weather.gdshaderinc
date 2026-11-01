// The menu planet's weather: how the air turns the cloud pattern round the
// planet. Shared by the cloud deck (planet_clouds, whose material holds these
// settings) and the bake that draws the turning into a map each frame
// (planet_weather_bake, by CloudMaps), so the deck reads one texture per sample
// instead of working the storms out again for every one.
//
// The air does not turn as one: it runs in bands round the planet, each at its
// own speed (jet streams), and in places it winds into storms that turn one way
// in the north and the other in the south. Each is a turning of the point the
// noise is read at, so the clouds shear and curl as the time goes on.

// How fast the whole deck drifts round the poles.
uniform float drift_degrees_per_second = 0.6;

// Jet swing is how far, in degrees round the poles, neighbouring bands are slid
// apart (0 keeps the deck whole), jets how many bands; storms are seven fixed
// cyclones and anticyclones, storm size their reach (planet radii) and storm
// twist how far, in degrees, the clouds at a storm's centre are wound.
uniform float jet_swing_degrees : hint_range(0.0, 90.0) = 14.0;
uniform float jets : hint_range(0.0, 12.0) = 5.0;
uniform float storm_size : hint_range(0.05, 1.5) = 0.4;
uniform float storm_twist_degrees : hint_range(0.0, 720.0) = 110.0;

// Vortices are a direction on the planet and a signed strength (north +, south -).
const vec4 STORMS[7] = vec4[7](
	vec4(-0.80, 0.45, -0.45, 1.0),
	vec4(-0.70, -0.35, -0.62, -1.0),
	vec4(-0.20, 0.62, -0.75, 0.8),
	vec4(0.30, 0.65, 0.70, 1.0),
	vec4(0.75, -0.45, 0.50, -1.0),
	vec4(-0.55, 0.25, 0.80, -0.6),
	vec4(0.55, 0.30, -0.78, 0.7));

vec3 turn_about(vec3 v, vec3 axis, float angle) {
	float s = sin(angle);
	float c = cos(angle);
	return (v * c) + (cross(axis, v) * s) + (axis * dot(axis, v) * (1.0 - c));
}

// Where the pattern at direction d (a unit vector in the planet's space) is read
// from now. Turning keeps the length, so it is a unit vector too.
vec3 weather_flow(vec3 d) {
	float latitude = d.y;
	vec3 q = d;
	float reach = storm_size * 1.5;
	// Storms: the pattern is wound round each centre, most at the middle, so the
	// clouds there are bent into spirals. They breathe a little, never run away.
	for (int i = 0; i < 7; i++) {
		vec3 centre = normalize(STORMS[i].xyz);
		float away = length(q - centre);
		// Falls to exactly zero at the storm's reach, with no step: a step there
		// is a perfect circle in the clouds. Past it the turn is nothing.
		if (away < reach) {
			float wind = smoothstep(0.0, 1.0, 1.0 - (away / reach));
			float breath = 1.0 + (0.2 * sin((TIME * 0.05) + float(i)));
			q = turn_about(q, centre, radians(storm_twist_degrees) * STORMS[i].w * wind * breath);
		}
	}
	// Bands: each latitude is shifted round the poles by its own amount, some one
	// way and some the other, so neighbouring bands slide past each other.
	float shift = radians(jet_swing_degrees) * sin((latitude * jets) + (0.4 * sin(TIME * 0.03)));
	// The whole deck then drifts, together, on top.
	return turn_about(q, vec3(0.0, 1.0, 0.0), shift + (radians(drift_degrees_per_second) * TIME));
}

// The weather map's layout: octahedral (a direction folded onto a square, the
// lower half turned out into the corners), so a read needs no trigonometry. The
// map has a border one texel wide all round that holds the directions just past
// each fold, so blending across a fold is blending neighbours.
vec2 weather_fold(vec3 d) {
	vec3 n = d / (abs(d.x) + abs(d.y) + abs(d.z));
	vec2 o = n.xz;
	if (n.y < 0.0) {
		vec2 away = vec2(n.x >= 0.0 ? 1.0 : -1.0, n.z >= 0.0 ? 1.0 : -1.0);
		o = (1.0 - abs(n.zx)) * away;
	}
	return o;
}

vec3 weather_unfold(vec2 o) {
	// Past an edge of the square is the other side of the same fold.
	if (o.x > 1.0) { o = vec2(2.0 - o.x, -o.y); }
	if (o.x < -1.0) { o = vec2(-2.0 - o.x, -o.y); }
	if (o.y > 1.0) { o = vec2(-o.x, 2.0 - o.y); }
	if (o.y < -1.0) { o = vec2(-o.x, -2.0 - o.y); }
	vec3 n = vec3(o.x, 1.0 - abs(o.x) - abs(o.y), o.y);
	if (n.y < 0.0) {
		vec2 away = vec2(n.x >= 0.0 ? 1.0 : -1.0, n.z >= 0.0 ? 1.0 : -1.0);
		n.xz = (1.0 - abs(n.zx)) * away;
	}
	return normalize(n);
}

// Where in a map of edge texels the folded point o is read (inside the border).
vec2 weather_uv(vec2 o, float edge) {
	return ((((o * 0.5) + 0.5) * (edge - 2.0)) + 1.0) / edge;
}

