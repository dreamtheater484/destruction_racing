// Car body paint: livery by rest position (no UVs needed), solid or metallic base under a clear coat,
// damage layers (scuffed clear coat -> primer -> bare metal -> rust, plus soot) from a 3D damage volume,
// and road dirt. Back faces render as a dark interior so open gaps and glass never show the void.
// debug_mode 1 = zebra (reflection stripes), 2 = clay. Object space: X right, Y up, -Z forward.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

#include "res://assets/shaders/car_deform.gdshaderinc"

group_uniforms livery;
uniform vec3 paint_color : source_color = vec3(0.55, 0.06, 0.04);
uniform vec3 paint_color_2 : source_color = vec3(0.92, 0.92, 0.9);
uniform vec3 pinstripe_color : source_color = vec3(0.03);
uniform int livery_mode = 0;              // 0 solid, 1 twin top stripes, 2 lower two-tone, 3 side band
uniform float stripe_inner = 0.10;        // twin stripes: |x| from inner to outer
uniform float stripe_outer = 0.24;
uniform float band_low = 0.36;            // two-tone / side band heights (m)
uniform float band_high = 0.62;
uniform float pinstripe = 0.006;          // pinstripe width at colour edges (0 = none)

group_uniforms race_number;
uniform sampler2D number_atlas : hint_default_black, filter_linear_mipmap;   // race_digits_sdf.png
uniform int race_number = -1;             // 0..99, -1 = none
uniform vec3 number_color : source_color = vec3(0.02);
uniform vec3 roundel_color : source_color = vec3(0.88, 0.88, 0.84);
uniform vec3 door_number = vec3(0.0, 0.55, 0.0);   // centre z, centre y, digit height (0 = none)
uniform vec3 roof_number = vec3(0.6, 1.1, 0.0);    // centre z, lowest y, digit height (0 = none)

group_uniforms finish;
uniform float metallic_paint : hint_range(0.0, 1.0) = 0.0;
uniform float paint_roughness : hint_range(0.0, 1.0) = 0.38;
uniform float clearcoat_amount : hint_range(0.0, 1.0) = 1.0;
uniform float clearcoat_roughness : hint_range(0.0, 1.0) = 0.06;
uniform float flake_scale = 1800.0;
uniform float flake_strength : hint_range(0.0, 0.5) = 0.12;

group_uniforms damage;
uniform sampler3D damage_volume : hint_default_black, filter_linear;   // r scrape, g rust, b soot
uniform vec3 volume_min = vec3(-1.0, 0.0, -2.5);
uniform vec3 volume_size = vec3(2.0, 1.5, 5.0);
uniform float wear : hint_range(0.0, 1.0) = 0.0;          // global age: chips, faded clear coat
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D rust_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D dirt_albedo : source_color, filter_linear_mipmap, repeat_enable;

group_uniforms debug;
uniform int debug_mode = 0;
uniform vec3 interior_color : source_color = vec3(0.025, 0.024, 0.023);

varying vec3 rest_pos;
varying vec3 rest_normal;
varying float gap_occlusion;      // baked into vertex colour: 0 deep in a panel gap, 1 on open paint

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float vnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x);
	float b = mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x);
	float c = mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x);
	float d = mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x);
	return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
}

float fbm(vec3 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * vnoise(p);
		p = p * 2.03 + vec3(1.7, 9.2, 3.1);
		a *= 0.5;
	}
	return s;
}

vec3 triplanar(sampler2D tex, vec3 p, vec3 n, float scale) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	return texture(tex, p.zy * scale).rgb * w.x + texture(tex, p.xz * scale).rgb * w.y
		+ texture(tex, p.xy * scale).rgb * w.z;
}

// Colour edge with an optional pinstripe: returns (blend to colour 2, pinstripe mask).
vec2 edge(float d) {
	float aa = fwidth(d) + 1e-5;
	float t = smoothstep(-aa, aa, d);
	float pin = pinstripe > 0.0 ? 1.0 - smoothstep(pinstripe * 0.5 - aa, pinstripe * 0.5 + aa, abs(d)) : 0.0;
	return vec2(t, pin);
}

// Race number in a stadium roundel at local (u right, v up) metres from its centre, digit height h.
// Returns (roundel coverage, digit coverage). The atlas holds 10 digit cells (1 x 1.5) of signed distance.
vec2 race_number_mask(vec2 uv, float h) {
	int count = race_number >= 10 ? 2 : 1;
	float k = 0.92;                                    // digit advance, in cell widths
	float s = 1.5 / h;                                 // metres -> cell units
	vec2 c = uv * s + vec2(float(count) * k * 0.5, 0.75);
	vec2 gx = dFdx(c) / vec2(10.0, -1.5);
	vec2 gy = dFdy(c) / vec2(10.0, -1.5);
	float half_w = float(count) * k * 0.5 * h / 1.5;
	float r = 0.7 * h;
	float rd = length(vec2(max(abs(uv.x) - max(half_w - 0.31 * h, 0.0), 0.0), uv.y)) - r;
	float aa = fwidth(rd) + 1e-5;
	float roundel = 1.0 - smoothstep(-aa, aa, rd);
	float i = floor(c.x / k);
	float digit = 0.0;
	if (i >= 0.0 && i < float(count) && c.y > 0.0 && c.y < 1.5) {
		int d = count == 2 ? (i < 0.5 ? race_number / 10 : race_number % 10) : race_number;
		float lx = c.x - i * k + (1.0 - k) * 0.5;
		float v = textureGrad(number_atlas, vec2((float(d) + lx) / 10.0, 1.0 - c.y / 1.5), gx, gy).r;
		float sd = (0.5 - v) * 0.5 / s;                // metres (atlas: 0.5 = edge, spread 0.25 cells)
		float a2 = max(length(vec2(gx.x * 10.0, gx.y * 1.5)), length(vec2(gy.x * 10.0, gy.y * 1.5))) / s + 1e-5;
		digit = 1.0 - smoothstep(-a2, a2, sd);
	}
	// Thin dark ring around the roundel, like a hand-painted derby number.
	float ring = 1.0 - smoothstep(0.012 * h / 0.4 - aa, 0.012 * h / 0.4 + aa, abs(rd + 0.014 * h / 0.4));
	return vec2(roundel, max(digit * roundel, ring));
}

// Paints the race number onto both front doors and the roof (rest space, so it dents with the panel).
// Returns the coverage so callers can drop metallic flake under the decal.
float race_number_paint(inout vec3 base, vec3 p, vec3 n) {
	if (race_number < 0) return 0.0;
	vec2 m = vec2(0.0);
	if (door_number.z > 0.0) {
		float side = smoothstep(0.5, 0.75, abs(n.x));
		vec2 uv = vec2((door_number.x - p.z) * sign(p.x), p.y - door_number.y);
		m = max(m, race_number_mask(uv, door_number.z) * side);
	}
	if (roof_number.z > 0.0) {
		float top = smoothstep(0.7, 0.9, n.y) * step(roof_number.y, p.y);
		vec2 uv = vec2(p.x, roof_number.x - p.z);   // reads from behind the car
		m = max(m, race_number_mask(uv, roof_number.z) * top);
	}
	base = mix(base, roundel_color, m.x);
	base = mix(base, number_color, m.y);
	return m.x;
}

vec3 livery(vec3 p, vec3 n) {
	vec2 e = vec2(0.0);
	if (livery_mode == 1) {
		float top = smoothstep(0.15, 0.35, n.y) + smoothstep(0.55, 0.75, abs(n.z));
		float ax = abs(p.x);
		float d = min(ax - stripe_inner, stripe_outer - ax);     // > 0 inside a stripe
		e = edge(d);
		e *= clamp(top, 0.0, 1.0);
	} else if (livery_mode == 2) {
		e = edge(band_high - p.y);
	} else if (livery_mode == 3) {
		float side = smoothstep(0.35, 0.6, abs(n.x));
		e = edge(min(p.y - band_low, band_high - p.y)) * side;
	}
	vec3 c = mix(paint_color, paint_color_2, e.x);
	return mix(c, pinstripe_color, e.y);
}

void vertex() {
	rest_pos = VERTEX;
	rest_normal = NORMAL;
	gap_occlusion = COLOR.r;
	vec3 rp;
	float cr;
	car_deform(VERTEX, NORMAL, rp, cr);
	rest_position = rp;
	crumple_amount = cr;
}

void fragment() {
	if (!FRONT_FACING) {
		ALBEDO = interior_color;
		ROUGHNESS = 1.0;
		SPECULAR = 0.1;
		NORMAL = -NORMAL;
	} else if (debug_mode == 1) {
		vec3 r = (INV_VIEW_MATRIX * vec4(reflect(-VIEW, NORMAL), 0.0)).xyz;
		float s = step(0.5, fract(r.y * 7.0 + r.x * 0.0));
		ALBEDO = vec3(0.0);
		ROUGHNESS = 1.0;
		SPECULAR = 0.0;
		EMISSION = vec3(s);
	} else if (debug_mode == 2) {
		ALBEDO = vec3(0.62);
		ROUGHNESS = 0.55;
	} else {
		NORMAL = crumple_normal(VERTEX, NORMAL, rest_position, crumple_amount);
		vec3 p = rest_pos;
		vec3 n = normalize(rest_normal);
		vec3 base = livery(p, n);
		float decal = race_number_paint(base, p, n);
		float metal = metallic_paint * (1.0 - decal);
		float rough = paint_roughness;
		float coat = clearcoat_amount;
		float coat_rough = clearcoat_roughness;

		// Metallic flake: per-flake brightness and roughness jitter under the clear coat.
		if (metallic_paint > 0.0) {
			float f = hash13(floor(p * flake_scale));
			base *= 1.0 + (f - 0.5) * 2.0 * flake_strength * metal;
			rough = clamp(rough + (f - 0.5) * 0.2 * metal, 0.05, 1.0);
		}

		// Global wear: dull, slightly orange-peel clear coat.
		coat *= 1.0 - 0.6 * wear;
		coat_rough = mix(coat_rough, 0.35, wear);

		// Damage volume.
		vec3 uvw = (p - volume_min) / volume_size;
		vec3 dmg = texture(damage_volume, uvw).rgb;
		// Scrapes run along the car: streaky breakup (fine across, long along Z).
		float breakup = fbm(vec3(p.x * 22.0, p.y * 64.0, p.z * 5.0));
		float scrape = clamp(dmg.r + (breakup - 0.5) * 0.7 * step(0.02, dmg.r), 0.0, 1.0);
		// Paint cracks off the sharpest folds of crumpled metal.
		scrape = max(scrape, crumple_amount * smoothstep(0.72, 0.97, crumple_folds_at(p)) * 0.9);
		vec3 rust = triplanar(rust_albedo, p, n, 1.4);
		if (scrape > 0.15) {                       // clear coat scuffed through
			float t = smoothstep(0.15, 0.25, scrape);
			coat *= 1.0 - 0.85 * t;
			rough = mix(rough, 0.62, 0.7 * t);
			base *= 1.0 - 0.12 * t;
		}
		if (scrape > 0.45) {                       // primer
			float t = smoothstep(0.45, 0.5, scrape);
			base = mix(base, vec3(0.24, 0.235, 0.225), t);
			rough = mix(rough, 0.8, t);
			metal = mix(metal, 0.0, t);
			coat = mix(coat, 0.0, t);
		}
		if (scrape > 0.68) {                       // bare steel, brushed along the scrape
			float t = smoothstep(0.68, 0.73, scrape);
			float streak = vnoise(vec3(p.x * 40.0, p.y * 260.0, p.z * 3.0));
			base = mix(base, vec3(0.21, 0.205, 0.2) * (0.65 + 0.5 * streak), t);
			metal = mix(metal, 0.6, t);
			rough = mix(rough, 0.6 + 0.2 * streak, t);
		}
		float rusting = clamp(dmg.g * 1.4 + (fbm(p * 5.0) - 0.5) * 0.6, 0.0, 1.0) * step(0.32, scrape + dmg.g);
		base = mix(base, rust, rusting);
		rough = mix(rough, 0.9, rusting);
		metal = mix(metal, 0.0, rusting);
		coat *= 1.0 - rusting;
		float soot = clamp(dmg.b * (0.7 + 0.6 * breakup), 0.0, 1.0);
		base = mix(base, vec3(0.02), soot);
		rough = mix(rough, 0.95, soot);
		coat *= 1.0 - soot;

		// Road dirt, heavier low down and in the wheel's spray.
		float low = clamp(1.0 - (p.y - 0.12) / 0.55, 0.0, 1.0);
		float d = clamp(dirt * (0.35 + low * 1.1) * (0.6 + 0.8 * fbm(p * 3.0 + 7.0)) - 0.15, 0.0, 1.0);
		vec3 mud = triplanar(dirt_albedo, p, n, 0.9) * 0.8;
		base = mix(base, mud, d * 0.9);
		rough = mix(rough, 0.92, d);
		coat *= 1.0 - d;

		float occ = gap_occlusion;
		ALBEDO = base * mix(0.06, 1.0, occ);
		METALLIC = metal;
		ROUGHNESS = rough;
		CLEARCOAT = coat * occ;
		CLEARCOAT_ROUGHNESS = coat_rough;
		SPECULAR = 0.5 * occ;
		AO = occ;
	}
}
