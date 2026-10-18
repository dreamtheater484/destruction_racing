// Full-screen effects (plan §8.4, §11), on a CanvasLayer above the 3D view and below the HUD:
// - a chromatic split on big hits (a frame or two) and a radial blur pulse from the hit;
// - a cool, darkened vignette during slow motion;
// - the film look (the FILM LOOK option): a soft vignette and a fine grain, always on.
shader_type canvas_item;

uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform float chroma = 0.0;      // 0..1
uniform float slowmo = 0.0;      // 0..1
uniform vec2 impact_dir = vec2(1.0, 0.0);
uniform float pulse = 0.0;       // 0..1, radial blur
uniform vec2 pulse_centre = vec2(0.5, 0.5);
uniform float film = 0.0;        // 0 or 1
uniform float time_s = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 from_centre = uv - 0.5;
	float r2 = dot(from_centre, from_centre);
	vec3 col;
	if (pulse > 0.001) {
		// Zoom blur towards the hit: 10 taps, stronger further from it.
		vec2 d = (uv - pulse_centre) * pulse * 0.045;
		col = vec3(0.0);
		for (int i = 0; i < 10; i++) col += texture(screen, uv - d * float(i) / 9.0).rgb;
		col /= 10.0;
	} else {
		col = texture(screen, uv).rgb;
	}
	if (chroma > 0.001) {
		vec2 off = (impact_dir * 0.6 + from_centre) * chroma * 0.018 * (0.4 + r2 * 2.0);
		col = vec3(texture(screen, uv + off).r, col.g, texture(screen, uv - off).b);
		col += vec3(1.0, 0.95, 0.9) * chroma * 0.06;
	}
	if (slowmo > 0.001) {
		float grey = dot(col, vec3(0.299, 0.587, 0.114));
		col = mix(col, vec3(grey) * vec3(0.92, 0.97, 1.05), slowmo * 0.45);
		col *= 1.0 - slowmo * smoothstep(0.12, 0.55, r2) * 0.55;
	}
	if (film > 0.5) {
		// A soft vignette (wider than tall, like a lens) and a fine grain that is strongest in the mid-tones.
		vec2 v = from_centre * vec2(1.0, 1.25);
		col *= 1.0 - smoothstep(0.16, 0.62, dot(v, v)) * 0.2;
		float lum = dot(col, vec3(0.299, 0.587, 0.114));
		float n = hash(floor(FRAGCOORD.xy) + vec2(fract(time_s * 7.1) * 911.0, fract(time_s * 3.7) * 577.0)) - 0.5;
		col += n * 0.035 * (1.0 - abs(lum - 0.5) * 1.4);
	}
	COLOR = vec4(col, 1.0);
}
