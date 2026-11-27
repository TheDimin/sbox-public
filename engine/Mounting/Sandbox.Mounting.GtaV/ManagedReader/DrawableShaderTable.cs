using System;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

internal static partial class DrawableShaderTable
{
	internal readonly record struct Parameter( uint Enhanced, bool Constant, NumericsVector4 Default );

	internal readonly ref struct Shader
	{
		const int ParameterSize = 7;
		readonly ReadOnlySpan<uint> parameters, presets;
		internal bool HasLegacyParameters { get; }

		internal Shader( ReadOnlySpan<uint> row )
		{
			HasLegacyParameters = row[3] != 0;
			parameters = Parameters.Slice( (int)row[1] * ParameterSize, (int)row[2] * ParameterSize );
			presets = Presets.Slice( (int)row[4] * 2, (int)row[5] * 2 );
		}

		internal uint Preset( byte bucket )
		{
			for ( int at = 0; at < presets.Length; at += 2 )
				if ( presets[at] == bucket ) return presets[at + 1];
			return 0;
		}

		internal bool TryGetParameter( uint hash, out Parameter parameter )
		{
			for ( int at = 0; at < parameters.Length; at += ParameterSize )
			{
				var row = parameters[at..];
				if ( row[0] != hash ) continue;
				parameter = new( row[1], row[2] != 0, new NumericsVector4(
					BitConverter.UInt32BitsToSingle( row[3] ), BitConverter.UInt32BitsToSingle( row[4] ),
					BitConverter.UInt32BitsToSingle( row[5] ), BitConverter.UInt32BitsToSingle( row[6] ) ) );
				return true;
			}
			parameter = default;
			return false;
		}
	}

	internal static Shader Find( uint hash )
	{
		const int stride = 6;
		var data = Shaders;
		int low = 0, high = data.Length / stride;
		while ( low < high )
		{
			int middle = low + (high - low) / 2;
			var row = data.Slice( stride * middle, stride );
			if ( row[0] == hash ) return new Shader( row );
			if ( row[0] < hash ) low = middle + 1;
			else high = middle;
		}
		return default;
	}
}
