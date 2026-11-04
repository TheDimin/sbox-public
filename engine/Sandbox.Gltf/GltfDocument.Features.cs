using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private static float FeatureNumber( JsonElement value, string property, float? fallback = null )
	{
		if ( value.ValueKind != JsonValueKind.Object ) throw new GltfException( "Expected a JSON object." );
		if ( !value.TryGetProperty( property, out var number ) )
		{
			if ( fallback.HasValue ) return fallback.Value;
			throw new GltfException( $"Missing required property '{property}'." );
		}
		if ( number.ValueKind != JsonValueKind.Number || !number.TryGetSingle( out var result ) || !float.IsFinite( result ) )
			throw new GltfException( $"'{property}' must be a finite number." );
		return result;
	}

	private static Vector3 FeatureColor( JsonElement value )
	{
		if ( !value.TryGetProperty( "color", out var color ) ) return Vector3.One;
		if ( color.ValueKind != JsonValueKind.Array || color.GetArrayLength() != 3 )
			throw new GltfException( "Light color must have three components." );
		var result = new float[3];
		for ( var i = 0; i < result.Length; i++ )
		{
			if ( color[i].ValueKind != JsonValueKind.Number || !color[i].TryGetSingle( out result[i] ) || !float.IsFinite( result[i] ) || result[i] < 0 || result[i] > 1 )
				throw new GltfException( "Light color components must be finite and in [0, 1]." );
		}
		return new Vector3( result[0], result[1], result[2] );
	}

	/// <summary>Reads the glTF camera definition without creating an engine camera.</summary>
	public GltfCamera ReadCamera( int index )
	{
		var camera = SceneObject( ElementAt( "cameras", index ), "camera" );
		var type = GltfJson.String( camera, "type" );
		if ( type == "perspective" )
		{
			var perspective = SceneObject( GltfJson.Required( camera, "perspective" ), "perspective camera" );
			var yfov = FeatureNumber( perspective, "yfov" );
			var znear = FeatureNumber( perspective, "znear" );
			float? aspect = perspective.TryGetProperty( "aspectRatio", out _ ) ? FeatureNumber( perspective, "aspectRatio" ) : null;
			float? zfar = perspective.TryGetProperty( "zfar", out _ ) ? FeatureNumber( perspective, "zfar" ) : null;
			if ( yfov <= 0 || znear <= 0 || aspect <= 0 || zfar <= znear )
				throw new GltfException( "Invalid perspective camera parameters." );
			return new GltfCamera { Type = type, YFov = yfov, ZNear = znear, ZFar = zfar, AspectRatio = aspect };
		}
		if ( type == "orthographic" )
		{
			var orthographic = SceneObject( GltfJson.Required( camera, "orthographic" ), "orthographic camera" );
			var xmag = FeatureNumber( orthographic, "xmag" );
			var ymag = FeatureNumber( orthographic, "ymag" );
			var znear = FeatureNumber( orthographic, "znear" );
			var zfar = FeatureNumber( orthographic, "zfar" );
			if ( xmag == 0 || ymag == 0 || znear < 0 || zfar <= znear )
				throw new GltfException( "Invalid orthographic camera parameters." );
			return new GltfCamera { Type = type, XMagnitude = xmag, YMagnitude = ymag, ZNear = znear, ZFar = zfar };
		}
		throw new GltfException( $"Unsupported camera type '{type}'." );
	}

	/// <summary>Reads one selected KHR_lights_punctual definition in glTF units.</summary>
	public GltfLight ReadLight( int index )
	{
		if ( !SupportsExtension( "KHR_lights_punctual" ) )
			throw new GltfException( "KHR_lights_punctual is not supported by this reader." );
		var extensions = SceneObject( GltfJson.Required( Root, "extensions" ), "extensions" );
		var lightsExtension = SceneObject( GltfJson.Required( extensions, "KHR_lights_punctual" ), "KHR_lights_punctual" );
		var lights = GltfJson.Required( lightsExtension, "lights" );
		if ( lights.ValueKind != JsonValueKind.Array || (uint)index >= (uint)lights.GetArrayLength() )
			throw new GltfException( "Punctual light index is out of range." );
		var light = SceneObject( lights[index], "light" );
		var type = GltfJson.String( light, "type" );
		if ( type is not ("directional" or "point" or "spot") )
			throw new GltfException( $"Unsupported punctual light type '{type}'." );
		var color = FeatureColor( light );
		var intensity = FeatureNumber( light, "intensity", 1 );
		float? range = light.TryGetProperty( "range", out _ ) ? FeatureNumber( light, "range" ) : null;
		if ( intensity < 0 || range <= 0 || (type == "directional" && range.HasValue) )
			throw new GltfException( "Invalid punctual light intensity or range." );
		var inner = 0f;
		var outer = MathF.PI / 4;
		if ( type == "spot" )
		{
			var spot = SceneObject( GltfJson.Required( light, "spot" ), "spot light" );
			inner = FeatureNumber( spot, "innerConeAngle", 0 );
			outer = FeatureNumber( spot, "outerConeAngle", MathF.PI / 4 );
			if ( inner < 0 || inner >= outer || outer > MathF.PI / 2 )
				throw new GltfException( "Invalid spot light cone angles." );
		}
		return new GltfLight { Type = type, Color = color, Intensity = intensity, Range = range, InnerConeAngle = inner, OuterConeAngle = outer };
	}

	/// <summary>Returns instance-local glTF transforms; an empty array means no selected instancing extension.</summary>
	public Matrix4x4[] ReadInstances( int node )
	{
		var nodeValue = SceneObject( ElementAt( "nodes", node ), "node" );
		if ( !SupportsExtension( "EXT_mesh_gpu_instancing" ) || !nodeValue.TryGetProperty( "extensions", out var extensions ) )
			return [];
		SceneObject( extensions, "node extensions" );
		if ( !extensions.TryGetProperty( "EXT_mesh_gpu_instancing", out var instanceExtension ) ) return [];
		SceneObject( instanceExtension, "EXT_mesh_gpu_instancing" );
		var meshIndex = SceneIndex( GltfJson.Required( nodeValue, "mesh" ), "instanced mesh" );
		_ = SceneObject( ElementAt( "meshes", meshIndex ), "instanced mesh" );
		var attributes = SceneObject( GltfJson.Required( instanceExtension, "attributes" ), "instance attributes" );
		float[] translation = null;
		float[] rotation = null;
		float[] scale = null;
		var count = -1;
		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var attribute in attributes.EnumerateObject() )
		{
			if ( attribute.Name.Length == 0 || !names.Add( attribute.Name ) )
				throw new GltfException( "Instance attribute names must be nonempty and unique." );
			var accessorIndex = SceneIndex( attribute.Value, "instance accessor" );
			var layout = Accessor( accessorIndex );
			if ( count != -1 && layout.Count != count ) throw new GltfException( "Instance attribute counts must match." );
			count = layout.Count;
			switch ( attribute.Name )
			{
				case "TRANSLATION":
				case "SCALE":
					if ( layout.Rows != 3 || layout.Columns != 1 || layout.ComponentType != 5126 || layout.Normalized )
						throw new GltfException( $"{attribute.Name} must be a floating-point VEC3." );
					if ( attribute.Name == "TRANSLATION" ) translation = ReadFloatAccessor( accessorIndex );
					else scale = ReadFloatAccessor( accessorIndex );
					break;
				case "ROTATION":
					if ( layout.Rows != 4 || layout.Columns != 1 ||
						!(layout.ComponentType == 5126 && !layout.Normalized || (layout.ComponentType is 5120 or 5122) && layout.Normalized) )
						throw new GltfException( "ROTATION must be a float or signed normalized VEC4." );
					rotation = ReadFloatAccessor( accessorIndex );
					break;
				default:
					// Unrecognized and custom attributes still contribute their count.
					break;
			}
		}
		if ( count < 0 ) throw new GltfException( "Instance attributes must not be empty." );
		var result = new Matrix4x4[count];
		for ( var i = 0; i < count; i++ )
		{
			var position = translation is null ? Vector3.Zero : new Vector3( translation[i * 3], translation[i * 3 + 1], translation[i * 3 + 2] );
			var stretch = scale is null ? Vector3.One : new Vector3( scale[i * 3], scale[i * 3 + 1], scale[i * 3 + 2] );
			var orientation = Quaternion.Identity;
			if ( rotation is not null )
			{
				var x = (double)rotation[i * 4];
				var y = (double)rotation[i * 4 + 1];
				var z = (double)rotation[i * 4 + 2];
				var w = (double)rotation[i * 4 + 3];
				var length = Math.Sqrt( x * x + y * y + z * z + w * w );
				if ( length == 0 || !double.IsFinite( length ) ) throw new GltfException( "Instance rotation has zero or invalid length." );
				orientation = new Quaternion( (float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length) );
			}
			result[i] = Matrix4x4.CreateScale( stretch ) * Matrix4x4.CreateFromQuaternion( orientation ) * Matrix4x4.CreateTranslation( position );
		}
		return result;
	}
}
