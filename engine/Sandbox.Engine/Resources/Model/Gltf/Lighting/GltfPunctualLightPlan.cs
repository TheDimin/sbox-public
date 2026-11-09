using System.Text.Json;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Lighting;

/// <summary>
/// Immutable bindings for KHR_lights_punctual in the explicitly selected scene.
/// This preserves physical source values; it does not assume that an engine
/// light's color, attenuation or finite radius implements candela or lux.
/// The resource retains the document until every renderer instance is released.
/// </summary>
internal sealed class GltfPunctualLightPlan
{
	internal const string Extension = "KHR_lights_punctual";
	private const float InchesPerMeter = 1f / .0254f;
	private readonly GltfDocument document;

	internal enum LightKind { Directional, Point, Spot }
	internal enum IntensityUnit { Candela, Lux }

	internal sealed record Binding( int Node, int Light, LightKind Kind, NVector LinearColor, float Intensity,
		float? RangeMeters, float? InnerConeRadians, float? OuterConeRadians )
	{
		internal IntensityUnit Unit => Kind == LightKind.Directional ? IntensityUnit.Lux : IntensityUnit.Candela;
		// Range and cones are properties of the light, never scaled by node or placement.
		internal double? RangeInches => (double?)RangeMeters / .0254;
		internal float? InnerConeDegrees => InnerConeRadians * (180f / MathF.PI);
		internal float? OuterConeDegrees => OuterConeRadians * (180f / MathF.PI);
	}

	internal sealed record State( Binding Binding, NMatrix SourceWorldTransform, NVector PositionInches,
		NVector EmissionDirection, bool HasEmissionDirection )
	{
		internal bool HasPosition => Binding.Kind != LightKind.Directional;
	}

	internal IReadOnlyList<Binding> Bindings { get; }
	internal bool UsesUnsupportedOptionalFallback { get; }

	private GltfPunctualLightPlan( GltfDocument document, Binding[] bindings, bool fallback )
	{
		this.document = document;
		Bindings = System.Array.AsReadOnly( bindings );
		UsesUnsupportedOptionalFallback = fallback;
	}

	internal static GltfPunctualLightPlan Create( GltfDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		return CreateForScene( document, document.GetDefaultScene(), allowUnselected: false );
	}

	/// <summary>Use the exact scene selected for geometry. An unselected resource has no active lights.</summary>
	internal static GltfPunctualLightPlan CreateForScene( GltfDocument document, int? sceneIndex, bool allowUnselected )
	{
		ArgumentNullException.ThrowIfNull( document );
		if ( !sceneIndex.HasValue )
		{
			if ( !allowUnselected ) throw new GltfException( "The asset has no selected scene for punctual lights." );
			return new( document, [], document.UnsupportedOptionalExtensions.Contains( Extension, StringComparer.Ordinal ) );
		}
		// Required unsupported extensions are already rejected by GltfReader.
		// An unsupported optional extension has the standards-defined no-light fallback.
		if ( document.UnsupportedOptionalExtensions.Contains( Extension, StringComparer.Ordinal ) )
			return new( document, [], true );

		var root = document.Root;
		var scene = Element( root, "scenes", sceneIndex.Value );
		var bindings = new List<Binding>();
		var pending = new Stack<int>();
		var seen = new HashSet<int>();
		if ( scene.TryGetProperty( "nodes", out var roots ) )
			foreach ( var node in Array( roots, "Scene nodes" ).EnumerateArray().Reverse() )
				pending.Push( Index( node, "Scene node" ) );
		while ( pending.TryPop( out var nodeIndex ) )
		{
			if ( !seen.Add( nodeIndex ) ) throw new GltfException( "A selected light scene node is referenced more than once." );
			var node = Element( root, "nodes", nodeIndex );
			if ( node.TryGetProperty( "children", out var children ) )
				foreach ( var child in Array( children, "Node children" ).EnumerateArray().Reverse() )
					pending.Push( Index( child, "Node child" ) );
			if ( !node.TryGetProperty( "extensions", out var extensions ) ) continue;
			Object( extensions, "Node extensions" );
			if ( !extensions.TryGetProperty( Extension, out var extension ) ) continue;
			Object( extension, Extension );
			if ( !root.TryGetProperty( "extensionsUsed", out var used ) || !used.EnumerateArray().Any( x => x.GetString() == Extension ) )
				throw new GltfException( $"A punctual light node must declare {Extension} in extensionsUsed." );
			if ( !extension.TryGetProperty( "light", out var reference ) ) throw new GltfException( "A punctual light node needs a light index." );
			var lightIndex = Index( reference, "Punctual light" );
			// The existing reader owns definition validation, defaults and units.
			var light = document.ReadLight( lightIndex );
			var kind = light.Type switch
			{
				"directional" => LightKind.Directional,
				"point" => LightKind.Point,
				"spot" => LightKind.Spot,
				_ => throw new GltfException( $"Unsupported punctual light type '{light.Type}'." )
			};
			bindings.Add( new( nodeIndex, lightIndex, kind, light.Color, light.Intensity, light.Range,
				kind == LightKind.Spot ? light.InnerConeAngle : null, kind == LightKind.Spot ? light.OuterConeAngle : null ) );
		}
		return new( document, bindings.ToArray(), false );
	}

	/// <summary>
	/// Evaluate the same pose and full affine engine placement used by geometry.
	/// Null pose means the authored rest pose. Directions use transformed local -Z,
	/// not a normal matrix. A collapsed axis is explicitly undefined, remains finite,
	/// and can recover on a later pose; point lights never require an orientation.
	/// </summary>
	internal IReadOnlyList<State> Evaluate( GltfPose pose, NMatrix placement )
	{
		// Ownership validation deliberately permits an already sampled pose to be
		// inspected after disposal. This asset plan still borrows a live document.
		_ = document.Root;
		if ( pose is not null ) document.ValidatePoseOwnership( pose );
		RequireAffine( placement, "Light placement" );
		var states = new State[Bindings.Count];
		for ( var i = 0; i < states.Length; i++ )
		{
			var binding = Bindings[i];
			var sourceWorld = pose is null ? document.GetWorldTransform( binding.Node ) : pose.GetWorldTransform( binding.Node );
			RequireAffine( sourceWorld, "Light node world transform" );
			// Directional lights have no position. In particular, do not overflow
			// conversion of an irrelevant authored translation and reject the light.
			var position = binding.Kind == LightKind.Directional ? NVector.Zero
				: NVector.Transform( ToEngine( new( sourceWorld.M41, sourceWorld.M42, sourceWorld.M43 ) ) * InchesPerMeter, placement );
			if ( !Finite( position ) ) throw new GltfException( "Punctual light position exceeds the finite engine coordinate domain." );
			var direction = NVector.Zero;
			var hasDirection = false;
			if ( binding.Kind != LightKind.Point )
			{
				var sourceDirection = new NVector( -sourceWorld.M31, -sourceWorld.M32, -sourceWorld.M33 );
				var transformed = NVector.TransformNormal( ToEngine( sourceDirection ), placement );
				if ( !Finite( transformed ) ) throw new GltfException( "Punctual light direction exceeds the finite engine coordinate domain." );
				// Double length avoids squaring a tiny/large finite float to zero/infinity.
				var length = Math.Sqrt( (double)transformed.X * transformed.X + (double)transformed.Y * transformed.Y + (double)transformed.Z * transformed.Z );
				if ( length > 0 )
				{
					direction = new( (float)(transformed.X / length), (float)(transformed.Y / length), (float)(transformed.Z / length) );
					hasDirection = true;
				}
			}
			states[i] = new( binding, sourceWorld, position, direction, hasDirection );
		}
		return System.Array.AsReadOnly( states );
	}

	private static NVector ToEngine( NVector source ) => new( source.Z, source.X, source.Y );
	private static bool Finite( NVector value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

	private static void RequireAffine( NMatrix matrix, string label )
	{
		if ( !Finite( new( matrix.M11, matrix.M12, matrix.M13 ) ) || !Finite( new( matrix.M21, matrix.M22, matrix.M23 ) )
			|| !Finite( new( matrix.M31, matrix.M32, matrix.M33 ) ) || !Finite( new( matrix.M41, matrix.M42, matrix.M43 ) )
			|| matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 )
			throw new GltfException( $"{label} must be a finite affine matrix." );
	}

	private static int Index( JsonElement value, string label ) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32( out var index ) && index >= 0
		? index : throw new GltfException( $"{label} index must be a nonnegative integer." );
	private static JsonElement Object( JsonElement value, string label ) => value.ValueKind == JsonValueKind.Object
		? value : throw new GltfException( $"{label} must be an object." );
	private static JsonElement Array( JsonElement value, string label ) => value.ValueKind == JsonValueKind.Array
		? value : throw new GltfException( $"{label} must be an array." );
	private static JsonElement Element( JsonElement root, string name, int index )
	{
		if ( !root.TryGetProperty( name, out var list ) || list.ValueKind != JsonValueKind.Array || (uint)index >= (uint)list.GetArrayLength() )
			throw new GltfException( $"Punctual light {name} index is out of range." );
		return Object( list[index], name );
	}
}
