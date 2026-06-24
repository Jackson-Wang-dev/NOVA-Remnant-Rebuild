shader_type canvas_item;

// Ports Nova1's Color.shaderproto. cam-only in real scripts (scenario_hyperbolic_bloom.txt's color
// tint, ch4.txt's _ColorMul=3 reveal). _T=0 means no effect, _T=1 means full _ColorMul/_ColorAdd tint.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform vec4 _ColorMul : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _ColorAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);
	vec4 col2 = col * _ColorMul + _ColorAdd;
	COLOR = mix(col, col2, _T);
	// Full-screen post layer - always fully opaque, see mono.gdshader for why.
	COLOR.a = 1.0;
}
