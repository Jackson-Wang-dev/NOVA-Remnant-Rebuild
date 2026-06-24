shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's BrokenTV.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Roll : hint_range(0.0, 1.0) = 0.07;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

float nova_broken_tv_power(float x) {
	x += 1.0;
	x *= x;
	x *= x;
	x -= 1.0;
	return x;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float powerT = nova_broken_tv_power(_T);

	vec2 ty = vec2(TIME, uv.y * 20.0);
	float xOffset = (nova_rand(ty) * 2.0 - 1.0) * 0.01;
	xOffset += (nova_noise(ty) * 2.0 - 1.0) * 0.05;
	float x = uv.x + xOffset * _T;

	float yOffset = (TIME + xOffset) * powerT;
	yOffset *= step(nova_noise(TIME), powerT * _Roll);
	float y = fract(uv.y + yOffset);

	// Chromatic aberration
	float rbOffset = xOffset * 0.1 * powerT;
	float r = texture(_MainTex, vec2(x - rbOffset, y)).r;
	float g = texture(_MainTex, vec2(x, y)).g;
	float b = texture(_MainTex, vec2(x + rbOffset, y)).b;
	float a = texture(_MainTex, vec2(x, y)).a;
	vec4 col = vec4(r, g, b, a);

	// Scan lines. Unity's _Time.x is time/20, unlike _Time.y (plain seconds, == Godot's TIME).
	col.rgb -= sin((uv.y + TIME / 20.0) * 500.0) * 0.1 * _T * col.a;

	// Snow noise
	col.rgb += (nova_rand(uv * 100.0 + TIME) * 2.0 - 1.0) * 0.04 * powerT * col.a;

	COLOR = col;
	COLOR.a = 1.0;
}
