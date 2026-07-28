using System.Threading;

namespace Editor;

[DropObject( "sound", "sound", "sound_c" )]
partial class SoundDropObject : BaseDropObject
{
	SoundEvent sound;
	SoundFile soundFile;
	Asset asset;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		PackageStatus = "Loading Sound";
		sound = asset.LoadResource<SoundEvent>();
		soundFile = sound is null ? asset.LoadResource<SoundFile>() : null;
		PackageStatus = null;
	}

	public override void OnUpdate()
	{
		using var scope = Gizmo.Scope( "DropObject", traceTransform );

		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.Sprite( Vector3.Zero, 28f * Gizmo.Settings.GizmoScale, "materials/gizmo/sound.png" );

		if ( !string.IsNullOrWhiteSpace( PackageStatus ) )
		{
			Gizmo.Draw.Text( PackageStatus, new Transform( Vector3.Up * 16f ), "Inter", 14 * Application.DpiScale );
		}
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( sound is null && soundFile is not null )
		{
			PackageStatus = "Creating Sound Event";
			sound = await CreateSoundEvent();
			PackageStatus = null;
		}

		if ( sound is null )
			return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Sound Point" ).WithGameObjectCreations().Push() )
		{
			GameObject = new GameObject();
			GameObject.Name = sound.ResourceName;
			GameObject.WorldTransform = traceTransform;

			var component = GameObject.Components.GetOrCreate<SoundPointComponent>();
			component.SoundEvent = sound;

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( GameObject );
		}
	}

	async Task<SoundEvent> CreateSoundEvent()
	{
		var packageName = asset.Package?.FullIdent;
		var fileName = string.IsNullOrWhiteSpace( packageName )
			? System.IO.Path.GetFileNameWithoutExtension( asset.Name )
			: packageName;
		var relativePath = $"sounds/cloud/{fileName}.sound";
		var eventAsset = AssetSystem.FindByPath( relativePath );

		if ( eventAsset is null )
		{
			var absolutePath = System.IO.Path.Combine( Project.Current.GetAssetsPath(), relativePath );
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolutePath ) );
			eventAsset = AssetSystem.CreateResource( "sound", absolutePath );
			if ( eventAsset is null )
				return null;

			await eventAsset.CompileIfNeededAsync();
		}

		if ( !eventAsset.TryLoadResource<SoundEvent>( out var soundEvent ) )
			return null;

		soundEvent.Sounds = new List<SoundFile> { soundFile };
		eventAsset.SaveToDisk( soundEvent );
		return soundEvent;
	}
}
