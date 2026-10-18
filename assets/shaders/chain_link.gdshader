// Galvanised chain-link fence: a diamond wire pattern from UV (metres), antialiased, fading to its average
// coverage in the distance so it never shimmers.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float cell = 0.065;
uniform float wire : hint_range(0.01, 0.3) = 0.07;
uniform vec3 colour : source_color = vec3(0.52, 0.53, 0.54);

void fragment() {
	vec2 p = UV / cell;
	float d = min(abs(fract(p.x + p.y) - 0.5), abs(fract(p.x - p.y) - 0.5));
	float fw = max(fwidth(p.x), fwidth(p.y)) * 1.2;
	float a = 1.0 - smoothstep(wire - fw, wire + fw, d);
	float far = clamp(4.0 * wire, 0.0, 1.0) * 0.8;
	a = mix(a, far, smoothstep(0.25, 0.7, fw));
	// Close to the lens (a chase camera by the fence) the wire is far out of focus: the mesh melts into its average
	// cover instead of filling the frame with diamonds.
	a = mix(far, a, smoothstep(0.8, 2.5, -VERTEX.z));
	ALBEDO = colour;
	METALLIC = 0.6;
	ROUGHNESS = 0.45;
	ALPHA = a;
}
