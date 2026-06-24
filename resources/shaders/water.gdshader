shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Water.shaderproto. cam-only post-process layer. uvEffect in the original is computed
// in the vertex stage but is just a linear function of uv (no other per-vertex data), so it's computed
// directly in fragment() here.
const float NOVA_PI = 3.1415927;

uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 10.0;
uniform float _Aspect = 1.77777778;
uniform float _Freq = 50.0;
uniform float _Distort = 5.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

float nova_water(vec2 uv) {
	const float n = 3.1;
	float h = 0.0;
	for (float i = 1.0; i <= n; i += 0.7) {
		vec2 p = uv * i * i;
		p.y += _Freq / i * TIME;
		h += cos(2.0 * NOVA_PI * nova_noise(p) * _T) / (i * i * i);
	}
	return h;
}

vec2 nova_water_normal(vec2 uv) {
	vec2 e = vec2(1e-3, 0.0);
	float h0 = nova_water(uv);
	float h1 = nova_water(uv + e);
	float h2 = nova_water(uv + e.yx);
	return vec2(h1 - h0, h2 - h0);
}

void fragment() {
	vec2 uvEffect = vec2(SCREEN_UV.x * _Size * _Aspect, SCREEN_UV.y * _Size);
	vec2 uvDelta = _Distort * nova_water_normal(uvEffect);
	COLOR = texture(_MainTex, SCREEN_UV + uvDelta);
	COLOR.a = 1.0;
}
