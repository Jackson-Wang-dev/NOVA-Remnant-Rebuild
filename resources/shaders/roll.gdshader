shader_type canvas_item;

// Ports Nova1's Roll.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _XSpeed = 0.0;
uniform float _YSpeed = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec2 uv = SCREEN_UV + vec2(_XSpeed, _YSpeed) * TIME * _T;
	COLOR = texture(_MainTex, fract(uv));
	COLOR.a = 1.0;
}
