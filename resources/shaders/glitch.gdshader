shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Glitch.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

float nova_glitch_block_noise(vec2 uv, float freqX, float freqY, float freqT) {
	float a = floor(nova_noise(TIME * freqT) * 100.0);
	a = floor(nova_noise(uv.y * freqY + a) * 100.0);
	a = floor(nova_noise(uv.x * freqX + a) * 100.0);
	a = floor(nova_noise(uv.y * freqY + a) * 100.0);
	a = nova_rand(a);
	return a;
}

float nova_glitch_power(float x) {
	x += 1.0;
	x *= x;
	x *= x;
	x -= 1.0;
	return x;
}

float nova_glitch_block_offset_pass(vec2 uv, float freqX, float freqY, float freqT, float threshold, float value) {
	float blockOffsetOn = nova_glitch_block_noise(uv, freqX, freqY, freqT);
	if (blockOffsetOn < threshold) {
		return value;
	}
	return 0.0;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float powerT = nova_glitch_power(_T);

	float uvOffsetOn = nova_glitch_block_noise(uv, 0.2, 0.2, 0.05);
	vec2 uvOffset = vec2(0.0, 0.0);
	if (uvOffsetOn < _T) {
		uvOffset.x = (nova_rand(uvOffsetOn) * 2.0 - 1.0) * 0.1;
		uvOffset.y = (nova_rand(uvOffset.x) * 2.0 - 1.0) * 0.1;
	}
	vec2 uv2 = uv + uvOffset;

	float rbOffsetOn = nova_glitch_block_noise(uv, 0.1, 0.1, 0.5);
	vec2 rbOffset = vec2(0.0, 0.0);
	if (rbOffsetOn < 0.2 * _T) {
		rbOffset.x = (nova_rand(rbOffsetOn) * 2.0 - 1.0) * 0.1;
		rbOffset.y = (nova_rand(rbOffset.x) * 2.0 - 1.0) * 0.1;
	}
	float r = texture(_MainTex, uv2 - rbOffset).r;
	float g = texture(_MainTex, uv2).g;
	float b = texture(_MainTex, uv2 + rbOffset).b;
	float a = texture(_MainTex, uv2).a;
	vec4 col = vec4(r, g, b, a);

	float blockOffset = 0.0;
	blockOffset += nova_glitch_block_offset_pass(uv, 0.5, 5.0, 0.1, 0.06 * powerT, -0.5);
	blockOffset += nova_glitch_block_offset_pass(uv, 2.0, 4.0, 0.1, 0.002 * powerT, 1.0);
	col.rgb += blockOffset;
	col.rgb = clamp(col.rgb, 0.0, 1.0);

	COLOR = col;
	COLOR.a = 1.0;
}
