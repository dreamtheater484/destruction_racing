// Generic car part (trim, bumpers, rubber, lenses, interior, chrome) that dents with the body.
shader_type spatial;
render_mode cull_back;

#include "res://assets/shaders/car_deform.gdshaderinc"

uniform vec3 albedo : source_color = vec3(0.03);
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float clearcoat : hint_range(0.0, 1.0) = 0.0;
uniform float clearcoat_roughness : hint_range(0.0, 1.0) = 0.05;
uniform vec3 emission : source_color = vec3(0.0);
uniform float emission_energy = 0.0;
uniform float scuff : hint_range(0.0, 1.0) = 0.0;     // plastic greys out where it crumples
uniform int lens_pattern = 0;                         // 0 none, 1 fluted headlamp glass, 2 prismatic lamp lens
uniform float lens_pitch = 0.007;
// Per lamp (SetInstanceShaderParameter): a smashed lamp goes dark and its lens crazes.
instance uniform float lamp_broken = 0.0;
// Per lamp: 0 keeps a lamp dark while its lens material glows (a reversing lamp when not reversing).
instance uniform float lamp_lit = 1.0;

// Lens optics as a bump: flutes (vertical cylinders) or a prism grid, faded out before they alias.
float lens_height(vec3 p) {
	if (lens_pattern == 1) {
		float u = p.x / lens_pitch;
		float v = p.y / (lens_pitch * 3.0);
		return 0.0005 * (abs(fract(u) - 0.5) * 2.0) + 0.0002 * sin(v * 6.2831);
	}
	vec2 q = vec2(p.x, p.y) / lens_pitch;
	return 0.0004 * (abs(fract(q.x) - 0.5) + abs(fract(q.y) - 0.5));
}

void vertex() {
	vec3 rp;
	float cr;
	car_deform(VERTEX, NORMAL, rp, cr);
	rest_position = rp;
	crumple_amount = cr;
}

void fragment() {
	NORMAL = crumple_normal(VERTEX, NORMAL, rest_position, crumple_amount);
	if (lens_pattern > 0) {
		float fade = 1.0 - smoothstep(0.25, 0.8, length(fwidth(rest_position)) / lens_pitch);
		if (fade > 0.0) {
			float hgt = lens_height(rest_position) * fade;
			vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
			vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
			float det = dot(dpdx, r1);
			if (abs(det) > 1e-12) {
				vec3 grad = sign(det) * (dFdx(hgt) * r1 + dFdy(hgt) * r2);
				NORMAL = normalize(abs(det) * NORMAL - grad);
			}
		}
	}
	float c = crumple_amount * scuff * smoothstep(0.45, 0.9, crumple_folds_at(rest_position));
	ALBEDO = mix(albedo, vec3(0.35, 0.34, 0.33), c);
	ROUGHNESS = mix(roughness, 0.85, c);
	METALLIC = metallic;
	CLEARCOAT = clearcoat * (1.0 - c);
	CLEARCOAT_ROUGHNESS = clearcoat_roughness;
	if (lamp_broken > 0.0 && lens_pattern > 0) {
		vec3 q = rest_position * 90.0;
		float craze = step(0.86, fract(sin(dot(floor(q.xy + q.z), vec2(12.9898, 78.233))) * 43758.5453));
		ALBEDO = mix(ALBEDO, ALBEDO * 0.25 + vec3(0.12), lamp_broken * (0.6 + 0.4 * craze));
		ROUGHNESS = mix(ROUGHNESS, 0.7, lamp_broken);
	}
	EMISSION = emission * emission_energy * (1.0 - crumple_amount) * (1.0 - lamp_broken) * lamp_lit;
}
