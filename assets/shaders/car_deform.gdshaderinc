// Shared car deformation (included by every car surface shader so all parts dent together). Two layers,
// both evaluated at the vertex's rest position so paint, glass and trim stay glued to the metal:
//  - crush: procedural crush shapes per damage zone (front, rear, left, right; 0..1). The end of the car is
//    shoved in, the hood or boot lid tents up in buckles, the wings bulge and the nose drops; a flank caves in.
//  - dent_volume: a 3D texture over the car, rgb = displacement (m), a = crumple amount, for individual hits.
// The normal follows the Jacobian of the summed displacement (finite differences), and crumple adds folds:
// coarse ones move the vertex, fine ones are bump-mapped in the fragment.

uniform sampler3D dent_volume : hint_default_black, filter_linear, repeat_disable;
uniform vec3 dent_min = vec3(-1.0, -0.1, -2.5);
uniform vec3 dent_size = vec3(2.0, 1.6, 5.0);
uniform vec3 dent_res = vec3(32.0, 24.0, 72.0);
uniform float crumple_depth = 0.022;      // fold relief at full crumple (m)
uniform float crumple_scale = 9.0;        // fold frequency (1/m)
uniform vec4 crush = vec4(0.0);           // zone crush: front, rear, left, right (0..1)
uniform vec3 crush_lo = vec3(-0.9, 0.0, -2.2);   // car extents in model space (X right, Y up, -Z forward)
uniform vec3 crush_hi = vec3(0.9, 1.3, 2.3);
uniform float crush_seed = 0.0;           // per car, so no two wrecks fold the same way

varying vec3 rest_position;
varying float crumple_amount;

vec4 dent_at(vec3 p) {
	return texture(dent_volume, (p - dent_min) / dent_size);
}

float dd_hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float dd_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(dd_hash13(i), dd_hash13(i + vec3(1, 0, 0)), f.x);
	float b = mix(dd_hash13(i + vec3(0, 1, 0)), dd_hash13(i + vec3(1, 1, 0)), f.x);
	float c = mix(dd_hash13(i + vec3(0, 0, 1)), dd_hash13(i + vec3(1, 0, 1)), f.x);
	float d = mix(dd_hash13(i + vec3(0, 1, 1)), dd_hash13(i + vec3(1, 1, 1)), f.x);
	return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
}

// Zone crush shapes: xyz displacement (m) and w = crumple contribution.
vec4 crush_field(vec3 p) {
	vec4 r = vec4(0.0);
	if (dot(crush, vec4(1.0)) <= 0.0) return r;
	vec3 ext = crush_hi - crush_lo;
	float hx = max(crush_hi.x, -crush_lo.x);
	float lat = clamp(p.x / hx, -1.0, 1.0);                       // -1 left flank .. 1 right flank
	float hgt = (p.y - crush_lo.y) / ext.y;
	float upper = smoothstep(0.42, 0.62, hgt);                    // hood / deck level, not bumper level
	for (int e = 0; e < 2; e++) {
		float k = e == 0 ? crush.x : crush.y;
		if (k <= 0.0) continue;
		float sgn = e == 0 ? 1.0 : -1.0;                          // front crush pushes back (+Z), rear forward
		float t = clamp((p.z - (e == 0 ? crush_lo.z : crush_hi.z)) * sgn / 1.35, 0.0, 1.0);
		float w = (1.0 - t) * (1.0 - t);
		float bias = sin(crush_seed * 3.1 + float(e) * 1.7);      // which corner took it harder
		float n1 = dd_noise(vec3(p.x * 3.0, p.y * 3.0, crush_seed + float(e) * 5.0));
		r.z += sgn * k * 0.42 * w * (1.0 + 0.35 * bias * lat) * (0.85 + 0.3 * n1);
		// Buckles: the panel tents up along one or two sharp, slightly diagonal creases (a triangle wave, not a
		// swell); the wings bulge out where the hood rises.
		float ph = t * 2.1 + lat * 0.22 * bias + crush_seed * 0.37 + n1 * 0.25;
		float wave = 1.0 - 4.0 * abs(fract(ph) - 0.5);            // -1..1, sharp at crest and trough
		float mid = (1.0 - t) * t * 4.0;
		r.y += k * 0.085 * mid * wave * upper * (1.0 - 0.45 * abs(lat));
		r.x += sign(p.x) * k * 0.06 * mid * max(wave, 0.0) * lat * lat;
		r.y -= k * 0.05 * w * (1.0 - upper);                      // the nose drops as the rails fold
		r.w = max(r.w, k * w * 1.25);                             // folds where the metal was compressed
	}
	for (int s = 0; s < 2; s++) {
		float k = s == 0 ? crush.z : crush.w;
		if (k <= 0.0) continue;
		float side = s == 0 ? -1.0 : 1.0;
		float u = clamp((hx - p.x * side) / 0.75, 0.0, 1.0);     // 0 at the flank, 1 inside the cabin
		float w = (1.0 - u) * (1.0 - u);
		float along = (p.z - 0.5 * (crush_lo.z + crush_hi.z)) / (ext.z * 0.5);
		float bell = exp(-along * along * 2.4);                   // centred on the doors
		float cage = 1.0 - 0.7 * smoothstep(0.62, 0.85, hgt);     // the roof rail holds better than the doors
		float n1 = dd_noise(vec3(p.y * 4.0, p.z * 2.5, crush_seed * 1.3 + float(s) * 7.0));
		r.x -= side * k * 0.30 * w * bell * cage * (0.75 + 0.5 * n1);
		r.w = max(r.w, k * w * bell * bell * cage * 0.9);
	}
	return r;
}

vec4 deform_at(vec3 p) {
	return dent_at(p) + crush_field(p);
}

// Ridged noise stretched along the push (0 = flat, 1 = ridge top): metal shoved along a panel folds into
// accordion creases across the push (a crushed hood or wing); a panel pushed straight in gets a few broad,
// smooth wrinkles instead of fine relief.
float crumple_folds(vec3 p, vec3 push) {
	float len = length(push);
	vec3 dir = len > 1e-4 ? push / len : vec3(0.0, 0.0, 1.0);
	vec3 q = p * (crumple_scale * 0.4) + dir * (dot(p, dir) * crumple_scale * 0.75);
	float r1 = 1.0 - abs(2.0 * dd_noise(q) - 1.0);
	float r2 = 1.0 - abs(2.0 * dd_noise(q * 2.1 + 11.0) - 1.0);
	return r1 * r1 * r1 * 0.78 + r2 * r2 * 0.22;
}

float crumple_folds_at(vec3 rest) {
	return crumple_folds(rest, deform_at(rest).xyz);
}

// Call from vertex() with locals, then copy them to the varyings rest_position and crumple_amount.
void car_deform(inout vec3 v, inout vec3 n, out vec3 rest, out float crumple) {
	rest = v;
	vec4 d0 = deform_at(v);
	vec3 h = dent_size / dent_res;
	vec3 ux = (deform_at(v + vec3(h.x, 0.0, 0.0)).xyz - deform_at(v - vec3(h.x, 0.0, 0.0)).xyz) / (2.0 * h.x);
	vec3 uy = (deform_at(v + vec3(0.0, h.y, 0.0)).xyz - deform_at(v - vec3(0.0, h.y, 0.0)).xyz) / (2.0 * h.y);
	vec3 uz = (deform_at(v + vec3(0.0, 0.0, h.z)).xyz - deform_at(v - vec3(0.0, 0.0, h.z)).xyz) / (2.0 * h.z);
	vec3 jtn = vec3(dot(ux, n), dot(uy, n), dot(uz, n));
	n = normalize(n - jtn);
	float c = clamp(d0.a, 0.0, 1.0);
	// Coarse part of the folds moves the silhouette too.
	v += d0.xyz + n * c * crumple_depth * (crumple_folds(v, d0.xyz) - 0.3);
	crumple = c;
}

// Fine folds as a bump (surface-gradient method, no tangents needed). Call in fragment():
// NORMAL = crumple_normal(VERTEX, NORMAL, rest_position, crumple_amount);
vec3 crumple_normal(vec3 view_pos, vec3 normal, vec3 rest, float crumple) {
	if (crumple < 0.002) return normal;
	float hgt = crumple * crumple_depth * 0.6 * crumple_folds_at(rest);
	vec3 dpdx = dFdx(view_pos);
	vec3 dpdy = dFdy(view_pos);
	float dhdx = dFdx(hgt);
	float dhdy = dFdy(hgt);
	vec3 r1 = cross(dpdy, normal);
	vec3 r2 = cross(normal, dpdx);
	float det = dot(dpdx, r1);
	if (abs(det) < 1e-12) return normal;
	vec3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
	return normalize(abs(det) * normal - grad);
}
