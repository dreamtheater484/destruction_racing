// Arena floor: packed sand with dry-mud patches and the circular ruts that 20 cars wear into a bowl.
// World-space planar mapping (no UVs needed); normal maps are applied in world space.
shader_type spatial;

uniform sampler2D sand_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float sand_scale = 0.32;
uniform float mud_scale = 0.22;
uniform vec3 sand_tint : source_color = vec3(0.78, 0.72, 0.64);
uniform vec3 mud_tint : source_color = vec3(0.72, 0.68, 0.62);
uniform vec2 arena_axes = vec2(55.0, 38.0);
uniform float rut_strength : hint_range(0.0, 1.0) = 0.6;

varying vec3 world_pos;

float h12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h12(i), h12(i + vec2(1, 0)), f.x), mix(h12(i + vec2(0, 1)), h12(i + vec2(1, 1)), f.x), f.y);
}

float fbm2(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 5; i++) { s += a * n2(p); p = p * 2.07 + 13.1; a *= 0.5; }
	return s;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 w = world_pos.xz;
	// Large mud patches and mid-scale variation.
	float patches = fbm2(w * 0.035) * 0.7 + fbm2(w * 0.16) * 0.3;
	float mud = smoothstep(0.46, 0.62, patches);
	// Circular ruts: concentric with the arena, broken up, strongest in the busy middle band.
	float e = length(w / arena_axes);
	float wobble = fbm2(w * 0.08) * 7.0;
	float rut = pow(0.5 + 0.5 * sin(e * 160.0 + wobble), 6.0);
	rut *= smoothstep(0.25, 0.55, e) * (1.0 - smoothstep(0.88, 0.98, e)) * smoothstep(0.35, 0.6, fbm2(w * 0.05 + 4.0));
	rut *= rut_strength;

	vec2 us = w * sand_scale;
	vec2 um = w * mud_scale;
	vec3 albedo = mix(texture(sand_albedo, us).rgb * sand_tint, texture(mud_albedo, um).rgb * mud_tint, mud);
	vec3 orm = mix(texture(sand_orm, us).rgb, texture(mud_orm, um).rgb, mud);
	// Normal maps are imported RG-compressed (blue dropped): rebuild z from x and y.
	vec2 nxy = mix(texture(sand_normal, us).rg, texture(mud_normal, um).rg, mud) * 2.0 - 1.0;
	vec3 nt = vec3(nxy, sqrt(max(0.0, 1.0 - dot(nxy, nxy))));
	// Ruts: darker, smoother (packed), slightly sunken.
	albedo *= 1.0 - 0.28 * rut;
	float rough = mix(orm.g, 0.6, rut * 0.6);
	nt.xy *= 1.0 - 0.6 * rut;
	// Planar map on the XZ plane: tangent +X, bitangent +Z, normal +Y.
	vec3 wn = normalize(vec3(nt.x, nt.z, nt.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	ALBEDO = albedo * (0.92 + 0.16 * n2(w * 1.7));
	ROUGHNESS = rough;
	AO = orm.r;
	SPECULAR = 0.4;
}
