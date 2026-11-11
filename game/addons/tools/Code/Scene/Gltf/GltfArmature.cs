namespace Editor;

/// <summary>How the scene view shows the names of glTF joints. The default bone gizmo always names a hovered bone.</summary>
public enum GltfBoneNames
{
	[Icon( "ads_click" )] Hover,
	[Icon( "label" )] Always,
}

/// <summary>
/// Scene view settings for the armature of glTF files. Joints are bone GameObjects, so the default bone gizmo
/// draws them and shows a name on hover. These settings hide the armature or show every name.
/// </summary>
public static class GltfArmature
{
	public static bool ShowArmature
	{
		get => EditorCookie.Get( "gltf.armature.show", true );
		set => EditorCookie.Set( "gltf.armature.show", value );
	}

	public static GltfBoneNames BoneNames
	{
		get => EditorCookie.Get( "gltf.armature.names", GltfBoneNames.Hover );
		set => EditorCookie.Set( "gltf.armature.names", value );
	}

	/// <summary>Apply the settings to the joints of a session and draw what the default bone gizmo does not.</summary>
	public static void Draw( GltfEditorSession session )
	{
		var show = ShowArmature;
		foreach ( var go in session.JointObjects )
		{
			if ( !go.IsValid() ) continue;
			// The default bone gizmo draws a GameObject with the bone flag. Without it the joint is a plain node.
			go.Flags = show ? go.Flags | GameObjectFlags.Bone : go.Flags & ~GameObjectFlags.Bone;
		}

		if ( !show || BoneNames != GltfBoneNames.Always || !Gizmo.Settings.GizmosEnabled ) return;
		using var scope = Gizmo.Scope( "gltf-bone-names" );
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = Color.White.WithAlpha( 0.8f );
		foreach ( var go in session.JointObjects )
		{
			if ( !go.IsValid() || !go.Active ) continue;
			var position = go.WorldPosition + Gizmo.CameraTransform.Up * 0.8f;
			Gizmo.Draw.Text( go.Name, new Transform( position ), flags: TextFlag.Center );
		}
	}
}
