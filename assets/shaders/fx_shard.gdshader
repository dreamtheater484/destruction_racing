// Glass shards and paint chips: small tumbling flakes. Glass is clear with a hard sparkle; chips take the
// particle colour (the car's paint) and are opaque-looking. glass = 1 for glass, 0 for paint chips.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float glass = 1.0;

void fragment() {
	if (glass > 0.5) {
		ALBEDO = vec3(0.55, 0.62, 0.6);
		METALLIC = 0.0;
		ROUGHNESS = 0.04;
		SPECULAR = 1.0;
		ALPHA = 0.45 * COLOR.a;
		RIM = 0.4;
	} else {
		ALBEDO = COLOR.rgb;
		ROUGHNESS = 0.35;
		CLEARCOAT = 0.6;
		ALPHA_SCISSOR_THRESHOLD = 0.5;
		ALPHA = COLOR.a;
	}
}
