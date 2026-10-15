// Full-screen hit feedback (plan §11): a chromatic split on big hits (a frame or two) and a cool, darkened
// vignette during slow motion. Drawn on a CanvasLayer above the 3D view and below the HUD.
shader_type canvas_item;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float chroma = 0.0;      // 0..1
uniform float slowmo = 0.0;      // 0..1
uniform vec2 impact_dir = vec2(1.0, 0.0);

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 from_centre = uv - 0.5;
	float r2 = dot(from_centre, from_centre);
	vec3 col;
	if (chroma > 0.001) {
		vec2 off = (impact_dir * 0.6 + from_centre) * chroma * 0.018 * (0.4 + r2 * 2.0);
		col = vec3(texture(screen, uv + off).r, texture(screen, uv).g, texture(screen, uv - off).b);
		col += vec3(1.0, 0.95, 0.9) * chroma * 0.06;
	} else {
		col = texture(screen, uv).rgb;
	}
	if (slowmo > 0.001) {
		float grey = dot(col, vec3(0.299, 0.587, 0.114));
		col = mix(col, vec3(grey) * vec3(0.92, 0.97, 1.05), slowmo * 0.45);
		col *= 1.0 - slowmo * smoothstep(0.12, 0.55, r2) * 0.55;
	}
	COLOR = vec4(col, 1.0);
}
