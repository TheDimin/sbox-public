using System.Threading;

namespace Editor;

[DropObject( "model", "vmdl", "vmdl_c", "gltf", "glb", "glb_c" )]
partial class ModelDropObject : BaseDropObject
{
	Model model;
	string archetype;
	bool isGltf;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		archetype = asset.FindStringEditInfo( "model_archetype_id" );
		isGltf = asset.Path.EndsWith( ".gltf", StringComparison.OrdinalIgnoreCase )
			|| asset.Path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase )
			|| asset.Path.EndsWith( ".glb_c", StringComparison.OrdinalIgnoreCase );

		PackageStatus = "Loading Model";
		model = await Model.LoadAsync( asset.Path );
		PackageStatus = null;
		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Could not load model '{asset.Path}'." );

		Bounds = model.Bounds;
		PivotPosition = Bounds.ClosestPoint( Vector3.Down * 10000 );
	}

	public override void OnUpdate()
	{
		using var scope = Gizmo.Scope( "DropObject", traceTransform );

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( Bounds );

		Gizmo.Draw.Color = Color.White;

		// Gizmo.Draw.Model creates a native SceneModel. Managed glTF models have
		// no native model handle, so show their bounds while dragging instead.
		if ( model is not null && !isGltf )
		{
			var so = Gizmo.Draw.Model( model );
			if ( so.IsValid() )
			{
				so.Flags.CastShadows = true;
				so.UpdateToBindPose();
			}
		}

		if ( !string.IsNullOrWhiteSpace( PackageStatus ) )
		{
			Gizmo.Draw.Text( PackageStatus, new Transform( Bounds.Center ), "Inter", 14 * Application.DpiScale );

			Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
			Gizmo.Draw.Sprite( Bounds.Center + Vector3.Up * 12, 16, "materials/gizmo/downloads.png" );
		}
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( model is null )
			return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Model" ).WithGameObjectCreations().Push() )
		{
			GameObject = new GameObject( false );
			GameObject.Name = model.ResourceName;
			GameObject.WorldTransform = traceTransform;

			bool isProp = !isGltf && EditorPreferences.PlaceModelsAsProp && (model.Physics?.Parts.Count ?? 0) > 0;
			if ( isProp )
			{
				var prop = GameObject.Components.Create<Prop>();
				prop.Model = model;
				prop.IsStatic = archetype == "static_prop_model";
			}
			else if ( model.BoneCount > 0 || (isGltf && model.AnimationCount > 0) )
			{
				var renderer = GameObject.Components.Create<SkinnedModelRenderer>();
				renderer.Model = model;
			}
			else
			{
				var renderer = GameObject.Components.Create<ModelRenderer>();
				renderer.Model = model;
			}

			GameObject.Enabled = true;

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( GameObject );
		}
	}
}
