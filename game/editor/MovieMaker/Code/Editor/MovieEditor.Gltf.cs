using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Selecting an animation in the glTF editor opens it here. Its keys become keyframes on LocalPosition,
/// LocalRotation and LocalScale tracks of the node GameObjects, in the keyframe editor: the timeline plays them,
/// selecting a bone selects its track, and moving a bone at the playhead sets a keyframe. The glTF editor writes
/// the tracks back into the animation when the file is saved or the animation is deselected.
/// </summary>
partial class MovieEditor
{
	private static SkinnedModelRenderer? _gltfImported;

	[EditorEvent.Frame]
	private static void UpdateGltfAnimation()
	{
		if ( SceneEditorSession.Active is not GltfEditorSession { AnimationPreview: { IsValid: true } renderer, PreviewAnimation: { } animation } gltf )
		{
			_gltfImported = null;
			return;
		}

		if ( renderer == _gltfImported )
		{
			SyncGltfSelection( gltf );
			return;
		}

		if ( _editors.Select( x => x.Key ).FirstOrDefault( x => x.IsValid() ) is not { } editor )
		{
			// Opening the dock makes an editor. The import happens on a later frame.
			EditorWindow.DockManager.SetDockState( "Movie Maker", true );
			return;
		}

		_gltfImported = renderer;
		EditorWindow.DockManager.RaiseDock( "Movie Maker" );

		try
		{
			editor.ImportGltfAnimation( gltf, renderer, animation );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not open the animation '{animation.Name}' in Movie Maker: {e.Message}" );
		}
	}

	private static TrackView? _gltfLastTrack;

	/// <summary>Selecting a track in the timeline selects its bone or node in the glTF scene.</summary>
	private static void SyncGltfSelection( GltfEditorSession gltf )
	{
		var session = ActiveSessions.FirstOrDefault( x => x.Player.Scene == gltf.Scene );
		var track = session?.TrackList.LastSelected;
		if ( track == _gltfLastTrack ) return;
		_gltfLastTrack = track;

		if ( track?.Target.GetTargetGameObject() is not { } go || gltf.GetElement( go ) is null ) return;
		if ( gltf.Selection.Count == 1 && gltf.Selection.Contains( go ) ) return;
		gltf.Selection.Set( go );
	}

	private void ImportGltfAnimation( GltfEditorSession gltf, SkinnedModelRenderer renderer, GltfElement animation )
	{
		using ( renderer.Scene.Push() )
		{
			var player = renderer.GameObject.Components.GetOrCreate<MoviePlayer>();
			player.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved;
			player.Resource ??= new EmbeddedMovieResource();
			Switch( player );
		}

		if ( Session is not { } session ) return;

		var keys = gltf.GetAnimationKeys( animation );
		if ( keys.Count == 0 ) return;

		using ( session.History.Push( $"Import Animation ({animation.Name})" ) )
		{
			foreach ( var (target, path, times, values, interpolation) in keys )
			{
				var mode = interpolation switch { "STEP" => KeyframeInterpolation.Step, "CUBICSPLINE" => KeyframeInterpolation.Cubic, _ => KeyframeInterpolation.Linear };
				switch ( path )
				{
					case "translation": AddKeyframes<Vector3>( session, target, nameof( GameObject.LocalPosition ), times, values, mode ); break;
					case "rotation": AddKeyframes<Rotation>( session, target, nameof( GameObject.LocalRotation ), times, values, mode ); break;
					default: AddKeyframes<Vector3>( session, target, nameof( GameObject.LocalScale ), times, values, mode ); break;
				}
			}
		}

		session.SetEditMode<KeyframeEditMode>();
		session.TrackList.Update();
		session.ClipModified();
		session.PlayheadTime = MovieTime.Zero;
		session.IsLooping = true;
		session.IsPlaying = true;
	}

	/// <summary>Replace the contents of a property track with one block of keyframes.</summary>
	private static void AddKeyframes<T>( Session session, GameObject target, string property, float[] times, object[] values, KeyframeInterpolation interpolation )
	{
		if ( session.GetOrCreateTrack( target, property ) is not ProjectPropertyTrack<T> track ) return;

		var keyframes = times.Select( ( t, i ) => new Keyframe<T>( MovieTime.FromSeconds( t ), (T)values[i], interpolation ) )
			.DistinctBy( x => x.Time )
			.ToImmutableArray();
		if ( keyframes.Length == 0 ) return;

		// A channel with one key still needs a block with a length.
		var end = keyframes.Length > 1 ? keyframes[^1].Time : keyframes[0].Time + MovieTime.FromFrames( 1, session.Project.SampleRate );
		var timeRange = new MovieTimeRange( keyframes[0].Time, end );
		track.SetBlocks( [new PropertyBlock<T>( new KeyframeSignal<T>( keyframes ), timeRange )] );
	}
}
