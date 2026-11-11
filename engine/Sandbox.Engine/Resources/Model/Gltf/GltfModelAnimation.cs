using Sandbox.Gltf;
using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Retains the source document and exact clip descriptions for a loaded glTF model.
/// The loader transfers document ownership only after successful model construction.
/// Instances own their playback clocks; no sampled native animation is synthesized.
/// </summary>
internal sealed class GltfModelAnimation : IDisposable
{
	internal readonly record struct Clip( int Index, string Name, float StartTime, float Duration );

	internal GltfDocument Document { get; }
	internal GltfModelRig Rig { get; }
	internal IReadOnlyList<Clip> Clips { get; }
	internal IReadOnlyList<string> SequenceNames { get; }
	private bool disposed;

	internal GltfModelAnimation( GltfDocument document, GltfModelRig rig = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		Document = document;
		Rig = rig;
		var clips = ReadClips( document );
		Clips = clips;
		SequenceNames = Array.AsReadOnly( clips.Select( clip => clip.Name ).ToArray() );
	}

	/// <summary>The validated clips of a document, with unique names. Borrows the document; takes no ownership.</summary>
	internal static IReadOnlyList<Clip> ReadClips( GltfDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		var clips = new List<Clip>();
		var names = new HashSet<string>( StringComparer.Ordinal );
		if ( document.Root.TryGetProperty( "animations", out var animations ) )
		{
			if ( animations.ValueKind != JsonValueKind.Array ) throw new GltfException( "glTF animations must be an array." );
			foreach ( var animation in animations.EnumerateArray() )
			{
				var index = clips.Count;
				if ( animation.ValueKind != JsonValueKind.Object ) throw new GltfException( "glTF animation must be an object." );
				var name = animation.TryGetProperty( "name", out var nameValue )
					? nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : throw new GltfException( "glTF animation name must be a string." )
					: null;
				if ( string.IsNullOrWhiteSpace( name ) ) name = $"animation_{index}";
				var baseName = name;
				for ( var suffix = 1; !names.Add( name ); suffix++ ) name = $"{baseName}_{suffix}";

				// Validate the original channels before any native model is registered.
				// Duration uses the original key times, including an authored leading delay.
				document.EvaluatePose( index, 0 );
				var timing = ReadTiming( document, animation );
				clips.Add( new Clip( index, name, timing.Start, timing.Duration ) );
			}
		}
		return Array.AsReadOnly( clips.ToArray() );
	}

	private static (float Start, float Duration) ReadTiming( GltfDocument document, JsonElement animation )
	{
		if ( !animation.TryGetProperty( "samplers", out var samplers ) || samplers.ValueKind != JsonValueKind.Array )
			throw new GltfException( "glTF animation samplers must be an array." );
		var duration = 0f;
		var start = float.PositiveInfinity;
		foreach ( var sampler in samplers.EnumerateArray() )
		{
			if ( sampler.ValueKind != JsonValueKind.Object || !sampler.TryGetProperty( "input", out var input ) || input.ValueKind != JsonValueKind.Number || !input.TryGetInt32( out var index ) || index < 0 )
				throw new GltfException( "glTF animation sampler input must reference an accessor." );
			var accessors = document.Root.GetProperty( "accessors" );
			if ( accessors.ValueKind != JsonValueKind.Array || index >= accessors.GetArrayLength() )
				throw new GltfException( "glTF animation sampler input accessor is out of range." );
			var accessor = accessors[index];
			if ( accessor.GetProperty( "componentType" ).GetInt32() != 5126 || accessor.GetProperty( "type" ).GetString() != "SCALAR" ||
				(accessor.TryGetProperty( "normalized", out var normalized ) && normalized.ValueKind != JsonValueKind.False) )
				throw new GltfException( "glTF animation input must be an unnormalized FLOAT SCALAR accessor." );
			var times = document.ReadFloatAccessor( index );
			var previous = -1f;
			foreach ( var time in times )
			{
				if ( !float.IsFinite( time ) || time < 0 || time <= previous )
					throw new GltfException( "glTF animation input times must be finite, nonnegative and strictly increasing." );
				previous = time;
				start = MathF.Min( start, time );
				duration = MathF.Max( duration, time );
			}
		}
		return (float.IsFinite( start ) ? start : 0, duration);
	}

	internal GltfPose EvaluatePose( int? animation, float time )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		return Document.EvaluatePose( animation, time );
	}

	internal GltfPose CreatePose( Func<int, System.Numerics.Matrix4x4?> localTransform )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		return Document.CreatePose( localTransform );
	}

	internal GltfPose EvaluatePoses( IReadOnlyList<int> animations, float time )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		return Document.EvaluatePoses( animations, time );
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		Document.Dispose();
	}
}
