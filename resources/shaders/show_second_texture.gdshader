shader_type canvas_item;

// Ports Nova1's ShowSecondTexture.shaderproto - unconditionally outputs _SubTex, ignoring whatever
// would otherwise be drawn (no _T, no _MainTex reference even in the original). cam-only in practice.
uniform sampler2D _SubTex : source_color, filter_linear;

void fragment() {
	COLOR = texture(_SubTex, SCREEN_UV);
	COLOR.a = 1.0;
}
