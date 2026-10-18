#[compute]
#version 450

// Motion blur (plan §8.4), run by MotionBlurEffect after the transparent pass, before tonemapping and glow.
// Each pixel is averaged along its own motion (Godot's velocity buffer: the per-frame move in screen UV), centred
// on the pixel. A sample only counts in proportion to how fast it moves itself, so a fast background doesn't pick up
// the colour of the car the camera follows (no halo round the car), and a still car stays sharp.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D source_color;
layout(set = 0, binding = 1) uniform sampler2D velocity_tex;
layout(rgba16f, set = 0, binding = 2) uniform restrict writeonly image2D out_color;

layout(push_constant, std430) uniform Params {
	vec2 size;
	float scale;    // the shutter: blur = the per-frame move × scale
	float max_len;  // the longest blur, in UV
} p;

void main() {
	ivec2 pos = ivec2(gl_GlobalInvocationID.xy);
	if (pos.x >= int(p.size.x) || pos.y >= int(p.size.y)) return;
	vec2 uv = (vec2(pos) + 0.5) / p.size;
	vec4 base = textureLod(source_color, uv, 0.0);
	vec2 v = textureLod(velocity_tex, uv, 0.0).xy * p.scale;
	float len = length(v);
	if (len > p.max_len) v *= p.max_len / len;
	float pixels = length(v * p.size);
	if (pixels < 1.0) {
		imageStore(out_color, pos, base);
		return;
	}
	int n = clamp(int(pixels * 0.5), 4, 16);
	// Interleaved gradient noise: jitters the taps per pixel, so few taps don't show as steps.
	float jitter = fract(52.9829189 * fract(dot(vec2(pos), vec2(0.06711056, 0.00583715))));
	float own = max(length(v), 1e-6);
	vec4 sum = base;
	float weight = 1.0;
	for (int i = 0; i < n; i++) {
		float t = (float(i) + jitter) / float(n) - 0.5;
		vec2 suv = clamp(uv + v * t, vec2(0.0), vec2(1.0));
		float w = clamp(length(textureLod(velocity_tex, suv, 0.0).xy * p.scale) / own, 0.0, 1.0);
		sum += textureLod(source_color, suv, 0.0) * w;
		weight += w;
	}
	imageStore(out_color, pos, sum / weight);
}
