// Procedural sky for the Bowl's golden-hour and night presets (the afternoon uses the Alps HDRI).
// A horizon-to-zenith gradient, a glow and disk around the first directional light (sun or moon),
// a band of soft clouds lit from the light's side, and stars at night. No TIME, so the radiance map is
// only rendered when a parameter changes.
shader_type sky;

uniform vec3 zenith_color : source_color = vec3(0.10, 0.22, 0.48);
uniform vec3 horizon_color : source_color = vec3(0.95, 0.62, 0.36);
uniform vec3 mid_color : source_color = vec3(0.85, 0.80, 0.62);
uniform float mid_height = 0.12;
uniform vec3 ground_color : source_color = vec3(0.06, 0.05, 0.05);
uniform float horizon_power = 0.42;
uniform vec3 glow_color : source_color = vec3(1.0, 0.55, 0.25);
uniform float glow_strength = 1.6;
uniform float glow_tightness = 6.0;
uniform vec3 disk_color : source_color = vec3(1.0, 0.85, 0.6);
uniform float disk_size = 0.028;
uniform float disk_energy = 30.0;
uniform float cloud_cover = 0.45;
uniform vec3 cloud_lit : source_color = vec3(1.0, 0.62, 0.38);
uniform vec3 cloud_shade : source_color = vec3(0.32, 0.24, 0.30);
uniform float star_density = 0.0;
uniform float energy = 1.0;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float noise2(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
			mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 6; i++) { s += a * noise2(p); p = p * 2.03 + vec2(17.1, 9.7); a *= 0.5; }
	return s;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	float h = d.y;
	// Three-stop gradient: horizon -> a pale band -> zenith (a two-stop orange-to-blue mix goes purple).
	float hp = pow(clamp(h, 0.0, 1.0), horizon_power);
	float mp = pow(mid_height, horizon_power);
	vec3 col = hp < mp ? mix(horizon_color, mid_color, hp / mp) : mix(mid_color, zenith_color, (hp - mp) / (1.0 - mp));
	// Below the horizon (hidden by the ground and mountains, but it feeds the reflections).
	col = mix(col, ground_color, smoothstep(0.0, -0.08, h));

	vec3 light_dir = LIGHT0_ENABLED ? LIGHT0_DIRECTION : vec3(0.0, 1.0, 0.0);
	float c = dot(d, light_dir);
	if (LIGHT0_ENABLED) {
		// Wide atmospheric glow, strongest near the horizon.
		float glow = pow(max(c, 0.0), glow_tightness) * (0.35 + 0.65 * (1.0 - clamp(h * 2.5, 0.0, 1.0)));
		col += glow_color * glow * glow_strength;
		col += glow_color * pow(max(c, 0.0), 120.0) * glow_strength * 2.0;
	}

	// Clouds on a dome: a scrolling-free fbm field projected onto a plane above the arena.
	if (h > 0.0 && cloud_cover > 0.0) {
		vec2 uv = d.xz / (h + 0.18) * 1.6;
		float f = fbm(uv * 1.3 + vec2(3.0, 1.0));
		float stretch = fbm(vec2(uv.x * 0.6, uv.y * 2.2) + 11.0);
		float density = smoothstep(1.0 - cloud_cover, 1.0 - cloud_cover + 0.35, f * 0.75 + stretch * 0.35);
		density *= smoothstep(0.0, 0.12, h) * (1.0 - smoothstep(0.55, 0.95, h));
		// Lit from the light's side: brighter rims towards it, shaded undersides away from it.
		float facing = 0.5 + 0.5 * c;
		vec3 cloud = mix(cloud_shade, cloud_lit, pow(facing, 2.2));
		cloud += glow_color * pow(max(c, 0.0), 16.0) * 1.5;
		col = mix(col, cloud, density * 0.92);
	}

	// Stars: one candidate per cell of a direction grid, faded out near the horizon.
	if (star_density > 0.0 && h > 0.0) {
		vec2 g = vec2(atan(d.x, d.z) * 180.0, asin(clamp(h, -1.0, 1.0)) * 180.0);
		vec2 cell = floor(g);
		float r = hash12(cell);
		if (r > 1.0 - star_density) {
			vec2 centre = cell + vec2(hash12(cell + 7.0), hash12(cell + 13.0));
			float dist = length(g - centre);
			float twinkle = 0.5 + 0.5 * hash12(cell + 31.0);
			col += vec3(0.85, 0.9, 1.0) * smoothstep(0.22, 0.0, dist) * twinkle * twinkle * 1.4 * smoothstep(0.02, 0.25, h);
		}
	}

	// The disk of the sun or moon.
	if (LIGHT0_ENABLED) {
		float disk = smoothstep(cos(disk_size), cos(disk_size * 0.85), c);
		col = mix(col, disk_color * disk_energy, disk);
	}
	COLOR = col * energy;
}
