shader_type canvas_item;

// Ports Nova1's Kaleido.shaderproto. cam-only post-process layer. cosSinTime in the original is
// computed in the vertex stage but doesn't depend on vertex position, so it's computed directly in
// fragment() here.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Repeat = 8.0;
uniform float _Freq = 1.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);

	float t = _Freq * TIME / _Repeat;
	vec2 cosSinTime = vec2(cos(t), sin(t));

	vec2 uv = SCREEN_UV - 0.5;
	int repeatCount = int(_Repeat);
	for (int i = 0; i < repeatCount; i++) {
		uv = abs(uv) - 0.25;
		uv *= sign(uv + uv.yx);
		uv = cosSinTime.x * uv + cosSinTime.y * uv.yx * vec2(1.0, -1.0);
	}
	uv += 0.5;
	vec4 col2 = texture(_MainTex, uv);

	COLOR = mix(col, col2, _T);
	COLOR.a = 1.0;
}
