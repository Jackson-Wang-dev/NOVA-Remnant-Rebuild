using Godot;

namespace Nova;

/// <summary>
/// Shared "render a pose's layered parts into an offscreen SubViewport and flatten to one texture"
/// step, used by both CompositeSpriteController (the on-screen full-body sprite) and AvatarController
/// (an independent headshot crop that may show a different/no pose than whatever the on-screen body is
/// currently doing). See CompositeSpriteController's doc comment for why flattening happens at all.
/// </summary>
public static class PoseCompositor
{
	public const float PixelSize = 0.01f;
	private const char PoseSeparator = '+';

	// Small margin so a part's edge antialiasing isn't clipped by the capture viewport bounds.
	private const float MarginScale = 1.05f;

	/// <param name="Viewport">Caller owns this - add it to the tree, and QueueFree it when done.</param>
	/// <param name="Texture">Live ViewportTexture; valid as long as Viewport is in the tree.</param>
	/// <param name="Bounds">Tight world-space bounding box of the parts (no margin).</param>
	/// <param name="MarginPixels">How far Bounds's top-left corner sits inside Texture's top-left
	/// corner, in pixels - callers mapping a Bounds-relative pixel rect (e.g. an avatar crop window
	/// authored against the tight bbox) onto actual Texture pixel space must add this offset first.</param>
	public readonly record struct Result(SubViewport Viewport, Texture2D Texture, Rect2 Bounds, Vector2 MarginPixels);

	public static Result Build(Node parent, string imageFolder, string pose, Godot.Collections.Dictionary offsets)
	{
		var parts = pose.Split(PoseSeparator);
		var textures = new Texture2D[parts.Length];
		var partOffsets = new Vector2[parts.Length];
		var bounds = new Rect2();
		for (var i = 0; i < parts.Length; i++)
		{
			var texture = GD.Load<Texture2D>($"{Assets.ResourceRoot}standings/{imageFolder}/{parts[i]}.png");
			var offset = GetOffset(offsets, parts[i]);
			var size = new Vector2(texture.GetWidth(), texture.GetHeight()) * PixelSize;
			var rect = new Rect2(offset - size / 2f, size);
			bounds = i == 0 ? rect : bounds.Merge(rect);
			textures[i] = texture;
			partOffsets[i] = offset;
		}

		var captureSize = bounds.Size * MarginScale;
		var captureCenter = bounds.GetCenter();
		var texSize = new Vector2I(
			Mathf.Max(1, Mathf.CeilToInt(captureSize.X / PixelSize)),
			Mathf.Max(1, Mathf.CeilToInt(captureSize.Y / PixelSize)));
		var marginPixels = (captureSize - bounds.Size) / 2f / PixelSize;

		var viewport = new SubViewport
		{
			Size = texSize,
			TransparentBg = true,
			// Without this, a SubViewport shares its parent's World3D by default - meaning every part
			// sprite built in here (and in every other character's/pose's private capture viewport)
			// would also be directly visible to the MAIN scene camera and to each other's cameras,
			// since "what a camera renders" is governed by World3D membership, not by node-tree
			// parentage under a particular Viewport. That produced a ghost/cascade artifact the first
			// time this was wired up: raw, not-yet-merged parts leaking straight into the main view.
			OwnWorld3D = true,
		};
		parent.AddChild(viewport);

		// Looks straight down -Z (Camera3D's default forward) at the parts from in front; parts are
		// isolated inside this private viewport, so simple per-part RenderPriority below is enough -
		// there is no other character's geometry in here to contend with.
		viewport.AddChild(new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			KeepAspect = Camera3D.KeepAspectEnum.Height,
			Size = captureSize.Y,
			Position = new Vector3(captureCenter.X, captureCenter.Y, 10),
		});

		for (var i = 0; i < parts.Length; i++)
		{
			viewport.AddChild(new Sprite3D
			{
				Texture = textures[i],
				PixelSize = PixelSize,
				Position = new Vector3(partOffsets[i].X, partOffsets[i].Y, 0),
				RenderPriority = i,
			});
		}

		return new Result(viewport, viewport.GetTexture(), bounds, marginPixels);
	}

	private static Vector2 GetOffset(Godot.Collections.Dictionary offsets, string part)
	{
		if (offsets.ContainsKey(part))
		{
			var arr = offsets[part].AsGodotArray();
			return new Vector2((float)arr[0].AsDouble(), (float)arr[1].AsDouble());
		}
		return Vector2.Zero;
	}
}
