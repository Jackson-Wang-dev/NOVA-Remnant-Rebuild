shader_type canvas_item;

#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's FadeRadialBlur.shaderproto. cam-only post-process layer; transition-shaped
// (blurs from the current screen into _SubTex). Per M8 scope decision, _SubTex is a static
// texture path only, not a live camera capture.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 1.0;
uniform float _Zoom = 0.5;
uniform float _Dir : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D _SubTex : source_color, filter_linear;
uniform vec4 _SubColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	vec2 uvShift = SCREEN_UV - 0.5;
	vec2 uvZoom = uvShift * (1.0 - _Zoom * _T * _Dir) + 0.5;
	vec4 col = nova_tex2DMotionBlur(_MainTex, SCREEN_PIXEL_SIZE, uvZoom, uvShift * _Size * _T);
	vec2 uvZoom2 = uvShift * (1.0 - _Zoom * (1.0 - _T) * (1.0 - _Dir)) + 0.5;
	vec4 col2 = nova_tex2DMotionBlurSub(_SubTex, SCREEN_PIXEL_SIZE, uvZoom2, uvShift * _Size * (1.0 - _T)) * _SubColor;
	COLOR = mix(col, col2, _T);
	COLOR.a = 1.0;
}
