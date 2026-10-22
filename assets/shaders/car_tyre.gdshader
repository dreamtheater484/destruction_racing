// Tyre rubber (post-M8 car look): satin sidewalls with the maker's raised lettering (the fictional Dustbowl Tyres,
// tools/liverysmith/tyre_lettering.py), a duller tread, and the dust an event leaves on them (CarVisual).
// Model space is the wheel's own (axle along x, centre at the origin), so the lettering turns with the wheel.
shader_type spatial;

uniform sampler2D lettering : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float band_inner = 0.21;          // the lettering band's radii (m)
uniform float band_outer = 0.255;
uniform float tread_radius = 0.27;        // beyond this radius the rubber is the tread and its shoulder
uniform vec3 rubber : source_color = vec3(0.032, 0.032, 0.034);
uniform float letter_height = 0.0007;     // m: how far the moulded letters stand out
uniform float dust : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dust_color : source_color = vec3(0.30, 0.27, 0.22);

varying vec3 local_pos;
varying vec3 local_n;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			   mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	local_pos = VERTEX;
	local_n = NORMAL;
}

void fragment() {
	float r = length(local_pos.yz);
	float side = local_pos.x >= 0.0 ? 1.0 : -1.0;
	float wall = smoothstep(0.55, 0.85, abs(normalize(local_n).x));
	float tread = smoothstep(tread_radius - 0.01, tread_radius + 0.01, r) * (1.0 - wall);

	// The lettering reads from outside the wheel on either side; the strip is half a turn, repeated.
	float theta = atan(local_pos.z, local_pos.y);
	vec2 uv = vec2(-side * theta / PI, 1.0 - (r - band_inner) / (band_outer - band_inner));
	float in_band = step(0.0, uv.y) * step(uv.y, 1.0) * wall;
	vec3 tex = texture(lettering, uv).rgb;
	float letter = tex.r * in_band;
	float h = tex.g * in_band * letter_height;

	// Raised letters: a bump from the height's screen-space slope (surface-gradient bump mapping).
	vec3 n = normalize(NORMAL);
	vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
	vec3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
	if (abs(det) > 1e-12) NORMAL = normalize(abs(det) * n - grad);

	float grain = vnoise(local_pos * 900.0);
	vec3 base = rubber * (0.92 + 0.16 * grain);
	float rough = mix(0.74, 0.9, tread);
	base = mix(base, rubber * 1.35, letter);          // the moulded letters catch a little more light
	rough = mix(rough, 0.6, letter);
	base = mix(base, rubber * 1.25, tread);           // a scrubbed, slightly grey tread

	// Dust: a fine coat everywhere, heaviest on the tread and the shoulders, in patches.
	float patches = vnoise(local_pos * vec3(30.0, 18.0, 18.0)) * 0.6 + vnoise(local_pos * 140.0) * 0.4;
	float d = clamp(dust * (0.45 + 0.55 * tread + 0.3 * smoothstep(band_outer, tread_radius, r)) * (0.65 + 0.7 * patches), 0.0, 0.85);
	base = mix(base, dust_color * (0.8 + 0.3 * patches), d);
	rough = mix(rough, 0.95, d);

	ALBEDO = base;
	ROUGHNESS = rough;
	SPECULAR = 0.4;
}
