shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's GrayWave.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Freq = 0.5;
uniform float _Size = 50.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);

	float n = nova_noise(_Freq * TIME);
	vec3 delta = col.rgb - n;
	col.rgb /= (1.0 + _Size * delta * delta);

	COLOR = col;
	COLOR.a = 1.0;
}
