// The menu planet's air, shared by its two passes (planet_atmosphere, the
// sight lines that end on the ground or in space; planet_atmosphere_clouds,
// those that end on a cloud): sunlight scattered along a sight line between two
// distances, and what is behind dimmed. The middle of the disc stays clear;
// toward the limb everything behind the air fades into blue haze, the limb
// glows, and where the sunlight grazes the day/night line it comes through
// reddened. Bands steps it into flat cel tones; 0 leaves it smooth.
//
// Why: the old glow coloured a sphere by how close the sight line passed to the
// centre. It never knew where the sight line stopped, so it could not haze what
// was behind the air, and its bright band at the planet's radius was painted
// across the clouds standing out past the limb.
//
// Single scattering (GPU Gems 2 ch. 16, simplified): the air thins
// exponentially with height, Rayleigh (blue, both ways) and Mie (haze, forward,
// the glow round the sun) each with its own scale height. The sight line is
// sampled; at each sample, the sunlight reaching it is dimmed by the air toward
// the sun (worked out in closed form, Schuler's approximation of the Chapman
// function) and nothing if the planet is in the way, the night side. What it
// adds is dimmed again by the air back to the camera.
//
// Where a sight line ends is worked out, not read from the depth buffer: the
// read cost 1.1 ms on Iris Xe, twice the scattering. The clouds mark their
// pixels in the stencil buffer, and each pass draws only its own.
//
// PlanetLayer pushes planet_radius, layer_radius and sun_direction (and
// stop_radius to the cloud pass), and copies the air pass's settings to the
// cloud pass, so the air is tuned in one material.

instance uniform float planet_radius = 5000.0;
instance uniform float layer_radius = 5800.0;
instance uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);

// How the air thins: the height, in metres, over which each kind falls to a
// third. Rayleigh is the air itself, Mie the haze low down.
uniform float rayleigh_height = 110.0;
uniform float mie_height = 25.0;
// How much each scatters at the surface, per metre: Earth's, scaled up for a
// planet this much smaller. Rayleigh is per colour (blue most).
uniform vec3 rayleigh = vec3(4.2e-4, 9.8e-4, 2.4e-3);
uniform float mie = 1.0e-3;
// How much the haze throws light forward (the glow round the sun).
uniform float mie_g : hint_range(0.0, 0.99) = 0.76;
// One knob over both: thicker or thinner air.
uniform float density : hint_range(0.0, 4.0) = 1.0;
uniform float sun_intensity = 20.0;
// How many times longer the sunlight's path through the air counts than it is.
// A small planet's air is thin edge-on: at the day/night line the sunlight
// crosses a few scale heights, not Earth's forty, and comes through white.
// Longer, it reddens there (sunrise, sunset) and is all but unchanged at noon.
uniform float sunset : hint_range(1.0, 10.0) = 1.0;
// How bright the reddened sunlight stays: 0 is as the air leaves it, dim as
// well as red (a sunset is then a dull ochre); 1 keeps its colour at the
// brightness it had before the air, so the day/night line glows orange.
uniform float sunset_glow : hint_range(0.0, 1.0) = 0.0;
// How much of the air's dimming of what is behind it is applied. One share for
// all colours dims toward grey, and on the dim side of the day/night line the
// air adds little light back: all of it washed the planet out.
uniform float dimming : hint_range(0.0, 1.0) = 1.0;
uniform float exposure = 1.0;
// How wide, in metres, the edge of the planet's shadow in the air is. A sharp
// edge, sampled a few times down each sight line, drew a hard line across the
// disc beside the day/night line.
uniform float shadow_soft : hint_range(0.0, 2000.0) = 400.0;
// 0 is smooth; 3 or 4 steps the air's brightness into flat tones, keeping its hue.
uniform float bands : hint_range(0.0, 8.0, 1.0) = 0.0;

// Sight-line samples: as many as the air crossed needs, one per step share of
// the air's thickness, between the least and the most. Looking down crosses a
// little air and takes the least; skimming the limb crosses kilometres and
// takes the most. Eight everywhere cost three milliseconds. The count is not
// rounded: the last sample covers what is left of a step, so where a sight line
// needs one more the new sample grows in from nothing (rounded, the count's
// steps were rings round the limb).
uniform float step_share : hint_range(0.1, 2.0) = 0.75;
const int MIN_SAMPLES = 3;
const int MAX_SAMPLES = 8;

// Both crossings of the sight line from o along d with a sphere of radius r
// round the centre, or (-1, -1) if it misses.
vec2 sphere_crossings(vec3 o, vec3 d, float r) {
	float b = dot(o, d);
	float disc = (b * b) - (dot(o, o) - (r * r));
	if (disc < 0.0) {
		return vec2(-1.0);
	}
	float root = sqrt(disc);
	return vec2(-b - root, -b + root);
}

// The air along a ray to space from a point at height/H = h, its direction at
// cos_zenith from straight up, on a planet of radius/H = x: in scale heights,
// as if all of it were as dense as at the surface. Schuler's approximation of
// the Chapman function (GPU Pro 3), times the density where the ray starts.
float chapman(float x, float h, float cos_zenith) {
	float c = sqrt(x + h);
	if (cos_zenith >= 0.0) {
		return (c / ((c * cos_zenith) + 1.0)) * exp(-h);
	}
	float x0 = sqrt(1.0 - (cos_zenith * cos_zenith)) * (x + h);
	float c0 = sqrt(x0);
	return (2.0 * c0 * exp(x - x0)) - ((c / (1.0 - (c * cos_zenith))) * exp(-h));
}

// The air's light along the sight line from camera along ray, t0 to t1 (both
// in metres along it): what it adds (premultiplied) and how much of what is
// behind it dims.
void air_light(vec3 camera, vec3 ray, float t0, float t1, out vec3 colour, out float alpha) {
	vec3 to_sun = normalize(sun_direction);
	vec3 beta_r = rayleigh * density;
	float beta_m = mie * density;
	float x_r = planet_radius / rayleigh_height;
	float x_m = planet_radius / mie_height;
	// Chapman's sqrt(x + h) with h dropped (the air's top is a few scale
	// heights; the planet is tens): one per pixel, not two per sample.
	float c_r = sqrt(x_r);
	float c_m = sqrt(x_m);

	float share = clamp((t1 - t0) / max((layer_radius - planet_radius) * step_share, 1.0), float(MIN_SAMPLES), float(MAX_SAMPLES));
	float samples = ceil(share);
	float step_length = (t1 - t0) / share;
	// The air between the camera and the sample, both kinds, as surface-dense metres.
	float view_r = 0.0;
	float view_m = 0.0;
	vec3 sum_r = vec3(0.0);
	vec3 sum_m = vec3(0.0);
	for (int i = 0; i < MAX_SAMPLES; i++) {
		if (float(i) >= samples) {
			break;
		}
		float dt = step_length * min(share - float(i), 1.0);
		vec3 p = camera + (ray * (t0 + (step_length * float(i)) + (dt * 0.5)));
		float r = length(p);
		float height = max(r - planet_radius, 0.0);
		float dense_r = exp(-height / rayleigh_height);
		float dense_m = exp(-height / mie_height);
		float rho_r = dense_r * dt;
		float rho_m = dense_m * dt;
		view_r += rho_r * 0.5;
		view_m += rho_m * 0.5;

		// Sunlight here: none in the planet's shadow, the cylinder behind the
		// planet away from the sun, fading in over shadow_soft from its edge.
		// How far out of the cylinder: (d^2 - R^2) / 2R, d the distance from its
		// axis, near enough d - R by the edge and with no square root.
		// Above the horizon Chapman is the density here, already worked out,
		// times a factor of the sun's angle; below it, the whole of it.
		vec3 up = p / r;
		float cos_sun = dot(up, to_sun);
		float lit = 1.0;
		if (cos_sun < 0.0) {
			float out_of_shadow = ((r * r * (1.0 - (cos_sun * cos_sun))) - (planet_radius * planet_radius)) / (2.0 * planet_radius);
			lit = smoothstep(-0.5 * shadow_soft, 0.5 * shadow_soft, out_of_shadow);
		}
		if (lit > 0.0) {
			float sun_r;
			float sun_m;
			if (cos_sun >= 0.0) {
				sun_r = (c_r / ((c_r * cos_sun) + 1.0)) * dense_r;
				sun_m = (c_m / ((c_m * cos_sun) + 1.0)) * dense_m;
			} else {
				sun_r = chapman(x_r, height / rayleigh_height, cos_sun);
				sun_m = chapman(x_m, height / mie_height, cos_sun);
			}
			sun_r *= rayleigh_height * sunset;
			sun_m *= mie_height * sunset;
			// The glow takes back the dimming of red, the least dimmed colour, from
			// all three: the hue stays, the brightness comes back.
			float red_loss = (beta_r.r * sun_r) + (beta_m * 1.1 * sun_m);
			vec3 through = exp(-((beta_r * (view_r + sun_r)) + (vec3(beta_m * 1.1) * (view_m + sun_m))) + (sunset_glow * red_loss));
			sum_r += through * (rho_r * lit);
			sum_m += through * (rho_m * lit);
		}
		view_r += rho_r * 0.5;
		view_m += rho_m * 0.5;
	}

	float mu = dot(ray, to_sun);
	float phase_r = (3.0 / (16.0 * PI)) * (1.0 + (mu * mu));
	float g2 = mie_g * mie_g;
	float phase_m = (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - (2.0 * mie_g * mu), 1e-4), 1.5));

	vec3 inscatter = ((sum_r * beta_r * phase_r) + (sum_m * beta_m * phase_m)) * sun_intensity;
	colour = vec3(1.0) - exp(-inscatter * exposure);
	vec3 through_view = exp(-((beta_r * view_r) + vec3(beta_m * 1.1 * view_m)));
	alpha = (1.0 - dot(through_view, vec3(1.0 / 3.0))) * dimming;

	// Cel: one amount, the brighter of the air's light and its dimming, rounded
	// down to a step with an edge a pixel wide, and both scaled by it, so the
	// hue and the balance of light to dimming hold. One amount: stepped apart,
	// the dimming kept a step the light had lost, a grey shell out in space.
	// Down: the thin air at the top is nothing.
	if (bands > 0.5) {
		float amount = max(max(max(colour.r, colour.g), colour.b), alpha);
		float q = amount * bands;
		float w = max(fwidth(q), 1e-4);
		float stepped = (floor(q) + smoothstep(1.0 - w, 1.0, fract(q))) / bands;
		float scale = stepped / max(amount, 1e-5);
		colour *= scale;
		alpha *= scale;
	}

}
