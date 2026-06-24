shader_type canvas_item;

// Ports Nova1's BarrelHyper.shaderproto (cheaper linear variant of Barrel, no asin). cam-only
// post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Sigma = 0.2;
uniform float _Aspect = 1.77777778;
uniform float _Scale = 1.0;
uniform float _Chroma : hint_range(0.0, 1.0) = 0.0;
uniform float _Offset = 0.0;
uniform vec4 _BackColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

vec2 nova_barrel_hyper_convert_uv(vec2 uv, float sigma, float aspect, float scale) {
	uv -= 0.5;
	uv /= scale;
	float r = sqrt(uv.x * uv.x * aspect * aspect + uv.y * uv.y);
	float ratio = 1.0 - r * sigma;
	uv *= ratio;
	uv += 0.5;
	return uv;
}

vec4 nova_barrel_hyper_get_color(vec2 uv, float sigma) {
	uv = nova_barrel_hyper_convert_uv(uv, sigma, _Aspect, _Scale);
	if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) {
		return texture(_MainTex, uv);
	} else {
		return _BackColor;
	}
}

void fragment() {
	float sigma = _Sigma * _T;

	vec4 col;
	if (_Chroma > 0.0) {
		float r = nova_barrel_hyper_get_color(SCREEN_UV, sigma * (1.0 - _Chroma)).r;
		vec2 ga = nova_barrel_hyper_get_color(SCREEN_UV, sigma).ga;
		float b = nova_barrel_hyper_get_color(SCREEN_UV, sigma * (1.0 + _Chroma)).b;
		col = vec4(r, ga.x, b, ga.y);
	} else {
		col = nova_barrel_hyper_get_color(SCREEN_UV, sigma);
	}

	col.rgb += _Offset * _T;
	col.rgb = clamp(col.rgb, 0.0, 1.0);
	COLOR = col;
	COLOR.a = 1.0;
}
