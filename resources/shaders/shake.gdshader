shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Shake.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _XAmp = 1.0;
uniform float _YAmp = 1.0;
uniform float _Freq = 10.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec2 uv = SCREEN_UV + vec2(_XAmp, _YAmp) * nova_rand2(floor(_Freq * TIME)) * _T;
	COLOR = texture(_MainTex, uv);
	COLOR.a = 1.0;
}
