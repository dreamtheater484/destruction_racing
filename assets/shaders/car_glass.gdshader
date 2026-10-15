// Tinted car glass: see-through face-on, mirror-like at grazing angles (Fresnel), dirt film and cracks.
shader_type spatial;
render_mode cull_disabled, blend_mix, depth_draw_opaque, specular_schlick_ggx;

#include "res://assets/shaders/car_deform.gdshaderinc"

uniform vec3 tint : source_color = vec3(0.07, 0.085, 0.085);
uniform float min_opacity : hint_range(0.0, 1.0) = 0.58;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.03;
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
uniform float shatter : hint_range(0.0, 1.0) = 0.0;     // 0 intact .. 1 crazed white
// Per window (GeometryInstance3D.SetInstanceShaderParameter): cracks from hits, and a pane knocked out. Slots 2-3
// (car_surface.gdshader has 0-1): the panes share their mesh with a rubber seal surface.
instance uniform float pane_crack : instance_index(2) = 0.0;
instance uniform float pane_gone : instance_index(3) = 0.0;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

// Distance to the nearest cell edge of a Voronoi pattern (crack network).
float cracks(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 mr = vec2(0.0);
	float md = 8.0;
	for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec2 g = vec2(float(x), float(y));
		vec2 o = vec2(hash12(i + g), hash12(i + g + 17.0));
		vec2 r = g + o - f;
		float d = dot(r, r);
		if (d < md) { md = d; mr = r; }
	}
	md = 8.0;
	for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec2 g = vec2(float(x), float(y));
		vec2 o = vec2(hash12(i + g), hash12(i + g + 17.0));
		vec2 r = g + o - f;
		if (dot(mr - r, mr - r) > 0.00001) md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
	}
	return md;
}

varying vec3 obj_pos;

void vertex() {
	obj_pos = VERTEX;
	vec3 rp;
	float cr;
	car_deform(VERTEX, NORMAL, rp, cr);
	rest_position = rp;
	crumple_amount = cr;
}

void fragment() {
	if (pane_gone > 0.5) discard;
	float nv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float fres = pow(1.0 - nv, 4.0);
	vec3 c = tint;
	float a = mix(min_opacity, 1.0, fres);
	float crack = 0.0;
	float shatter_now = max(max(shatter, pane_crack), smoothstep(0.3, 0.65, crumple_amount));   // hits, or where the frame folded
	if (shatter_now > 0.0) {
		vec2 q = obj_pos.xz * 24.0 + obj_pos.yy * 24.0;
		float e = min(cracks(q), cracks(q * 2.7 + 5.0) * 1.6);
		crack = (1.0 - smoothstep(0.0, 0.015 + 0.035 * shatter_now, e)) * smoothstep(0.0, 0.3, shatter_now);
		c = mix(c, vec3(0.62), crack * 0.7);
		a = max(a, mix(0.0, 0.95, crack) + shatter_now * 0.3);
	}
	c = mix(c, vec3(0.32, 0.28, 0.22), dirt * 0.6);
	a = max(a, dirt * 0.5);
	ALBEDO = c;
	ALPHA = a;
	ROUGHNESS = mix(roughness_value, 0.6, max(dirt * 0.7, crack));
	METALLIC = 0.0;
	SPECULAR = 0.5;
}
