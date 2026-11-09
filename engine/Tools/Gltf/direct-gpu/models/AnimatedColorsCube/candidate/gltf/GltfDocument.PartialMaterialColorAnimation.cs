using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
    // Deliberately only a partial optional-extension implementation. Required
    // KHR_animation_pointer is still rejected by normal capability validation.
    private void TrySampleMaterialColor( JsonElement target, JsonElement channel, JsonElement samplers, float time, ref Dictionary<int, Vector4> updates )
    {
        if ( !target.TryGetProperty( "extensions", out var extensions ) || extensions.ValueKind != JsonValueKind.Object ||
            !extensions.TryGetProperty( "KHR_animation_pointer", out var extension ) ) return;
        if ( GltfJson.String( target, "path" ) != "pointer" || extension.ValueKind != JsonValueKind.Object )
            throw new GltfException( "Invalid animation pointer target." );
        var pointer = GltfJson.String( extension, "pointer" );
        var parts = pointer.Split( '/' );
        if ( parts.Length != 5 || parts[0] != "" || parts[1] != "materials" || parts[3] != "pbrMetallicRoughness" || parts[4] != "baseColorFactor" ) return;
        // JSON Pointer array indices are canonical decimal, without signs or leading zeros.
        if ( parts[2].Length == 0 || (parts[2].Length > 1 && parts[2][0] == '0') ||
            !int.TryParse( parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var material ) )
            throw new GltfException( "Invalid animated material index." );
        var definition = ElementAt( "materials", material );
        if ( !definition.TryGetProperty( "pbrMetallicRoughness", out var pbr ) || pbr.ValueKind != JsonValueKind.Object )
            throw new GltfException( "Animated baseColorFactor requires a defined pbrMetallicRoughness object." );
        var samplerIndex = GltfJson.Int( channel, "sampler" );
        if ( (uint)samplerIndex >= (uint)samplers.GetArrayLength() ) throw new GltfException( "Animation sampler index is out of range." );
        var sampler = SceneObject( samplers[samplerIndex], "animation sampler" );
        var inputIndex = GltfJson.Int( sampler, "input" ); var outputIndex = GltfJson.Int( sampler, "output" );
        var input = Accessor( inputIndex ); var output = Accessor( outputIndex );
        if ( GltfJson.String( input.Json, "type" ) != "SCALAR" || input.ComponentType != 5126 || input.Normalized )
            throw new GltfException( "Animation input must be a FLOAT SCALAR accessor." );
        if ( GltfJson.String( output.Json, "type" ) != "VEC4" ) throw new GltfException( "Animated baseColorFactor requires a VEC4 accessor." );
        ValidateNonVertexAccessor( output, "Animation output" );
        var interpolation = GltfJson.String( sampler, "interpolation", "LINEAR" );
        var outputValues = ReadFloatAccessor( outputIndex );
        var times = ReadAnimationInput( input, inputIndex );
        var values = GltfAnimationSampler.Sample( times, outputValues, 4, interpolation, time );
        // Only vertex values represent the constrained property. Cubic in/out
        // tangents may be signed and larger than one. The pointer spec does not
        // specify clamping of interpolated overshoot; preserve the sampled value.
        var stride = interpolation == "CUBICSPLINE" ? 12 : 4;
        var offset = interpolation == "CUBICSPLINE" ? 4 : 0;
        for ( var key = 0; key < times.Length; key++ )
            for ( var c = 0; c < 4; c++ )
                if ( outputValues[key * stride + offset + c] is < 0 or > 1 ) throw new GltfException( "Animated baseColorFactor key must be in [0,1]." );
        updates ??= new();
        if ( !updates.TryAdd( material, new Vector4( values[0], values[1], values[2], values[3] ) ) )
            throw new GltfException( "An animation cannot target the same material colour twice." );
    }
}
