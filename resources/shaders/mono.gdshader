shader_type canvas_item;

// Ports Nova1's Mono.shaderproto. cam-only in real scripts (full-screen black/white flashback
// filter, see ch2.txt etc.) - hence canvas_item + hint_screen_texture rather than a Sprite3D-targeted
// spatial shader. _T=0 means no effect, _T=1 means full grayscale.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform vec4 _ColorMul : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _ColorAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);
	float gray = dot(col.rgb, vec3(0.299, 0.587, 0.114));

	vec4 mono = vec4(vec3(gray), col.a);
	mono *= _ColorMul;
	mono += _ColorAdd;

	COLOR = mix(col, mono, _T);
	// Full-screen post layer - always fully opaque, regardless of whatever alpha hint_screen_texture's
	// backbuffer copy happens to carry (it's not reliably 1.0, which silently made this layer render
	// near-fully-transparent - see porting-guide.md M8 decision log bugfix entry).
	COLOR.a = 1.0;
}
