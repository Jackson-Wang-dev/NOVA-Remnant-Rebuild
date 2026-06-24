shader_type canvas_item;

// Ports Nova1's FlipGrid.shaderproto. cam-only post-process layer; page-flip grid transition between
// _MainTex and _SubTex. gridInfo in the original is computed in the vertex stage but only depends on
// uniforms, so it's computed directly in fragment() here. Both samplers are only ever read via the
// texture() builtin (never passed into a shared helper function), so this doesn't run into the
// "differing hint" compile restriction documented on fade_radial_blur.gdshader.
const float NOVA_PI = 3.1415927;

uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _GridSize = 64.0;
uniform float _FlipDuration : hint_range(0.0, 1.0) = 0.5;
uniform vec4 _BackColor : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 _SubColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D _SubTex : source_color, filter_linear;

float nova_flip_grid_smooth_step_in(float x) {
	return x * x * (2.0 - x);
}

void fragment() {
	vec2 texSize = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 gridIndexCount = texSize / _GridSize;
	float maxDiagNum = round(gridIndexCount.x) + round(gridIndexCount.y);
	float flipStartInterval = (1.0 - _FlipDuration) / maxDiagNum;
	float halfDuration = _FlipDuration / 2.0;

	vec2 pixelPos = SCREEN_UV * texSize / _GridSize;
	vec2 gridIndex = floor(pixelPos);
	float gridDiagIndex = gridIndex.x + gridIndex.y;
	float flipStartTime = gridDiagIndex * flipStartInterval;
	float flipEndTime = flipStartTime + _FlipDuration;

	vec4 col = _BackColor;

	if (flipStartTime > _T) {
		// Transition has not arrived here yet
		col = texture(_MainTex, SCREEN_UV);
	} else if (flipEndTime < _T) {
		// Transition finished
		col = texture(_SubTex, SCREEN_UV) * _SubColor;
	} else {
		vec2 p = fract(pixelPos);
		vec2 intersect = vec2(1.0 + p.x - p.y, 1.0 - p.x + p.y) / 2.0;
		float deltaTime = _T - flipStartTime;
		float theta;
		bool useSub;
		if (deltaTime < halfDuration) {
			theta = NOVA_PI / 2.0 * nova_flip_grid_smooth_step_in(deltaTime / halfDuration);
			useSub = false;
		} else {
			theta = NOVA_PI / 2.0 * nova_flip_grid_smooth_step_in((_FlipDuration - deltaTime) / halfDuration);
			useSub = true;
		}
		float dis = (1.0 - p.x - p.y) * 0.5 / (cos(theta) + 0.001);
		vec2 q = intersect - dis;
		if (q.x >= 0.0 && q.y >= 0.0 && q.x <= 1.0 && q.y <= 1.0) {
			vec2 realUV = (gridIndex + q) * _GridSize * SCREEN_PIXEL_SIZE;
			col = useSub ? texture(_SubTex, realUV) * _SubColor : texture(_MainTex, realUV);
		}
		// else: out of the grid cell, leave col at _BackColor (matches the original's early "return col"
		// before col was ever reassigned away from _BackColor)
	}

	COLOR = col;
	COLOR.a = 1.0;
}
