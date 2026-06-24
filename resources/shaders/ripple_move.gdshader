shader_type canvas_item;

#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's RippleMove.shaderproto. cam-only post-process layer. tex1.x/tex1.y in the original
// are computed in the vertex stage but are constants (depend only on uniforms, not vertex position),
// so they're just computed directly in fragment() here - no varying needed.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Aspect = 1.77777778;
uniform float _Amp = 0.01;
uniform float _RFreq = 50.0;
uniform float _TFreq = 3.0;
uniform float _BlurSize = 1.0;
uniform float _Width = 0.1;
uniform float _Fade = 0.1;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	float rMax = 0.5 * sqrt(1.0 + _Aspect * _Aspect);
	float fadeFactor = clamp(_T / _Fade, 0.0, 1.0) * clamp((1.0 - _T) / _Fade, 0.0, 1.0);

	vec2 uv = SCREEN_UV - 0.5;
	uv.x *= _Aspect;
	float r = length(uv);
	r = max(r, 1e-3);
	float dr = sin(_RFreq * r + _TFreq * TIME);

	float t = r / rMax;
	float dt = (_T - t) / _Width;
	dr /= 1.0 + dt * dt;
	dr *= fadeFactor;

	dr = dr * dr * dr;
	uv *= (r + min(r, _Amp) * dr) / r;
	uv.x /= _Aspect;
	uv += 0.5;

	vec4 col;
	if (_BlurSize > 1e-3) {
		col = nova_tex2DGaussianBlur(_MainTex, SCREEN_PIXEL_SIZE, uv, _BlurSize * abs(dr));
	} else {
		col = texture(_MainTex, uv);
	}
	COLOR = col;
	COLOR.a = 1.0;
}
