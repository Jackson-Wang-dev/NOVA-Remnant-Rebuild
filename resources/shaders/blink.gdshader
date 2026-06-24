shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Blink.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Mul = 1.0;
uniform float _Offset = 0.0;
uniform float _Amp = -0.5;
uniform float _Freq = 10.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);
	col.rgb *= _Mul;
	col.rgb += _Offset;

	float n = nova_noise(_Freq * TIME);
	col.rgb += _Amp * n * n * _T;
	col.rgb = clamp(col.rgb, 0.0, 1.0);

	COLOR = col;
	COLOR.a = 1.0;
}
