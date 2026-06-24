shader_type canvas_item;

// Ports Nova1's Overglow.shaderproto. cam-only post-process layer; no Blur.cginc involved (it's a
// zoomed-duplicate-blend bloom, not a real blur kernel).
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Zoom = 0.5;
uniform float _Mul = 0.5;
uniform float _CenterX = 0.5;
uniform float _CenterY = 0.5;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec2 center = vec2(_CenterX, _CenterY);
	vec2 uv2 = (SCREEN_UV - center) * (1.0 - _Zoom * _T) + center;
	vec4 col = texture(_MainTex, SCREEN_UV);
	vec4 col2 = texture(_MainTex, uv2);
	col.rgb += col2.rgb * _Mul * (1.0 - _T);
	col.rgb = clamp(col.rgb, 0.0, 1.0);
	COLOR = col;
	COLOR.a = 1.0;
}
