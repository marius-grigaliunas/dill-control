// PLACEHOLDER-adjacent: the explosion's fireball, as a cel-shaded lumpy ball
// that cools in bands and then breaks up, rather than a translucent sphere
// that fades.
//
// Opaque with a dissolve, not alpha-blended, for two reasons. It writes depth,
// so the screen-space ink pass outlines it with no work of its own -- which is
// most of what makes it read as part of the same drawn world as everything
// else. And an opaque layer costs one fragment a pixel where two stacked
// translucent shells cost two blends, which is the overdraw the near-blast
// budget in ExplosionEffect exists to contain.
//
// Driven per instance -- progress and seed are instance uniforms -- so every
// blast shares one material and nothing is duplicated per explosion.
shader_type spatial;
render_mode unshaded, cull_back, fog_disabled, shadows_disabled;

// 0 at the instant of detonation, 1 when the layer is spent.
instance uniform float progress = 0.0;
// Any number; decorrelates one blast's lumps from the next.
instance uniform float seed = 0.0;

// Hottest to coolest. Every band is a flat colour on purpose.
uniform vec4 col_hot : source_color = vec4(1.0, 0.97, 0.7, 1.0);
uniform vec4 col_bright : source_color = vec4(1.0, 0.78, 0.08, 1.0);
uniform vec4 col_mid : source_color = vec4(1.0, 0.38, 0.03, 1.0);
uniform vec4 col_deep : source_color = vec4(0.86, 0.1, 0.12, 1.0);
uniform vec4 col_smoke : source_color = vec4(0.3, 0.23, 0.3, 1.0);

// Scales the hot bands. The level tonemaps with AgX, which desaturates
// anything much over 1 toward white, so the bands are saturated colours at 1
// rather than bright ones pushed into glow.
uniform float energy : hint_range(0.5, 4.0) = 1.0;
// Added to the heat everywhere. The core layer runs hotter than the shell.
uniform float heat_bias : hint_range(-1.0, 1.0) = 0.0;
// How far heat falls over the layer's life; past about 1 the whole ball ends as smoke.
uniform float cooling : hint_range(0.0, 2.5) = 1.05;

// Lumps, as a share of the radius, and how many across the ball.
uniform float lump_amount : hint_range(0.0, 0.6) = 0.3;
uniform float lump_scale : hint_range(0.5, 8.0) = 2.4;
// Share of the life at which the ball starts to break apart.
uniform float dissolve_start : hint_range(0.0, 1.0) = 0.5;

varying float v_lump;
varying vec3 v_local;

float hash3(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.11, 0.17, 0.13));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x),
		    mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x),
		    mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y),
		f.z);
}

void vertex() {
	// The lumps boil outward as the ball ages; a static shape would read as a
	// rock that got bigger.
	vec3 p = VERTEX * lump_scale * 2.0 + vec3(seed * 7.31, seed * 3.17, progress * 1.6);
	float n = value_noise(p);
	v_lump = n;
	VERTEX += NORMAL * (n - 0.45) * 2.0 * lump_amount * (0.6 + progress * 0.8);
	v_local = VERTEX;
}

void fragment() {
	// One noise lookup a pixel, for the dissolve edge. The lump value from the
	// vertex stage carries the coarse shape for free.
	float fine = value_noise(v_local * 7.0 + vec3(seed * 1.93));

	float dissolve = (progress - dissolve_start) / max(1.0 - dissolve_start, 0.001);
	if (dissolve > 0.0 && fine * 0.7 + v_lump * 0.3 < dissolve) {
		discard;
	}

	// Facing the camera is hot, the silhouette is cool -- the classic toon
	// fireball, which is a white heart ringed in orange and red.
	float facing = clamp(dot(normalize(NORMAL), VIEW), 0.0, 1.0);
	float heat = facing * 0.75 + v_lump * 0.45 + fine * 0.12 - progress * cooling + heat_bias;

	vec3 c = col_smoke.rgb;
	if (heat > 0.95) {
		c = col_hot.rgb * energy;
	} else if (heat > 0.7) {
		c = col_bright.rgb * energy;
	} else if (heat > 0.45) {
		c = col_mid.rgb * energy;
	} else if (heat > 0.2) {
		c = col_deep.rgb;
	}
	ALBEDO = c;
}
