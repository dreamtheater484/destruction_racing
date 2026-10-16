// Crossroads Speedway ground: asphalt (track and pit lanes), concrete, dirt and grass from the surface map
// (splat: R asphalt, G concrete, B dirt, A pit lane; grass is what is left), world-space planar mapping with the
// normal maps applied in the terrain's own frame, so the banked turns shade correctly.
shader_type spatial;

uniform sampler2D splat : filter_linear, repeat_disable;
// x0, z0, width, depth of the splat in world metres
uniform vec4 splat_rect = vec4(-212.0, -124.0, 424.0, 248.0);
uniform sampler2D asphalt_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D asphalt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D asphalt_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D concrete_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D concrete_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D concrete_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float asphalt_scale = 0.16;
uniform float concrete_scale = 0.22;
uniform float dirt_scale = 0.2;
uniform vec3 asphalt_tint : source_color = vec3(0.62, 0.62, 0.64);
uniform vec3 pit_tint : source_color = vec3(0.78, 0.77, 0.75);
uniform vec3 concrete_tint : source_color = vec3(0.82, 0.81, 0.78);
uniform vec3 dirt_tint : source_color = vec3(0.70, 0.62, 0.52);
uniform vec3 grass_a : source_color = vec3(0.20, 0.30, 0.10);
uniform vec3 grass_b : source_color = vec3(0.34, 0.40, 0.16);
uniform vec3 grass_dry : source_color = vec3(0.47, 0.44, 0.27);
// The rubbered racing groove: a darker band down the middle of the asphalt (from the splat's soft edge).
uniform float groove_strength : hint_range(0.0, 1.0) = 0.35;

varying vec3 world_pos;
varying vec3 world_normal;

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

vec3 unpack(vec2 rg) {
	// Normal maps are imported RG-compressed (blue dropped): rebuild z from x and y.
	vec2 xy = rg * 2.0 - 1.0;
	return vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy))));
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 w = world_pos.xz;
	vec4 sp = texture(splat, (w - splat_rect.xy) / splat_rect.zw);
	float asphalt = sp.r, concrete = sp.g, dirt = sp.b, pit = sp.a;
	float grass = clamp(1.0 - asphalt - concrete - dirt, 0.0, 1.0);

	vec2 ua = w * asphalt_scale, uc = w * concrete_scale, ud = w * dirt_scale;
	vec3 a_alb = texture(asphalt_albedo, ua).rgb * mix(asphalt_tint, pit_tint, pit);
	vec3 c_alb = texture(concrete_albedo, uc).rgb * concrete_tint;
	vec3 d_alb = texture(dirt_albedo, ud).rgb * dirt_tint;
	vec3 a_orm = texture(asphalt_orm, ua).rgb, c_orm = texture(concrete_orm, uc).rgb, d_orm = texture(dirt_orm, ud).rgb;
	vec3 a_n = unpack(texture(asphalt_normal, ua).rg), c_n = unpack(texture(concrete_normal, uc).rg), d_n = unpack(texture(dirt_normal, ud).rg);

	// Grass: two greens and dry patches in large blobs, fine speckle, a soft procedural bump.
	float patches = fbm2(w * 0.03);
	float dry = smoothstep(0.55, 0.72, fbm2(w * 0.012 + 7.0));
	vec3 g_alb = mix(grass_a, grass_b, smoothstep(0.35, 0.65, patches));
	g_alb = mix(g_alb, grass_dry, dry * 0.7);
	g_alb *= 0.82 + 0.3 * n2(w * 9.0) * n2(w * 23.0 + 3.0);
	vec3 g_n = normalize(vec3((n2(w * 14.0 + vec2(0.07, 0.0)) - n2(w * 14.0)) * -3.0, (n2(w * 14.0 + vec2(0.0, 0.07)) - n2(w * 14.0)) * -3.0, 1.0));

	// Wear: the asphalt is darker and smoother where the cars run (the middle, away from the soft edges), with
	// faint oil drops and patches of repair.
	float core = smoothstep(0.85, 1.0, asphalt) * (1.0 - pit);
	float wear = core * groove_strength * (0.6 + 0.4 * fbm2(w * 0.2));
	a_alb *= 1.0 - 0.35 * wear;
	a_alb *= 0.9 + 0.2 * fbm2(w * 0.07 + 2.0);
	float oil = smoothstep(0.78, 0.86, n2(w * 1.3)) * core * 0.5;
	a_alb = mix(a_alb, vec3(0.02), oil);

	float total = asphalt + concrete + dirt + grass + 1e-4;
	vec3 albedo = (a_alb * asphalt + c_alb * concrete + d_alb * dirt + g_alb * grass) / total;
	vec3 orm = (a_orm * asphalt + c_orm * concrete + d_orm * dirt + vec3(1.0, 0.92, 0.0) * grass) / total;
	vec3 nt = normalize((a_n * asphalt + c_n * concrete + d_n * dirt + g_n * grass) / total);
	float rough = mix(orm.g, 0.55, wear);
	rough = mix(rough, 0.2, oil);

	// Tangent frame from the terrain normal: tangent ~ +X, bitangent ~ +Z (the planar mapping's axes).
	vec3 N = normalize(world_normal);
	vec3 T = normalize(vec3(1.0, 0.0, 0.0) - N * N.x);
	vec3 B = cross(T, N);
	vec3 wn = normalize(T * nt.x + B * nt.y + N * nt.z);
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	ALBEDO = albedo;
	ROUGHNESS = rough;
	AO = orm.r;
	SPECULAR = mix(0.5, 0.25, grass);
}
