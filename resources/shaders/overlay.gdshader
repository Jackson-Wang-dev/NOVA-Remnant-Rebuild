shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix;

// Ports Nova1's Overlay.shaderproto (clamped passthrough of the object's own texture; Premul variant
// only in Nova1 - sprite-only, never cam). The original's vertex shader bypassed the normal MVP
// transform to render directly in clip space (a Unity full-screen-blit trick); that has no Sprite3D
// equivalent in Godot, so this just keeps the normal spatial vertex pipeline and ports only the
// fragment logic (clamp UV to avoid texture bleed at the edges).
uniform sampler2D _MainTex : source_color, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, clamp(UV, 0.0, 1.0));
	ALBEDO = col.rgb;
	ALPHA = col.a;
}
