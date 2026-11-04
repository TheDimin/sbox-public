using System.Text.Json;

namespace Sandbox.Gltf;

internal static class GltfJson
{
	internal static JsonElement Required( JsonElement value, string property )
	{
		if ( value.ValueKind != JsonValueKind.Object || !value.TryGetProperty( property, out var result ) )
			throw new GltfException( $"Missing required property '{property}'." );
		return result;
	}

	internal static int Int( JsonElement value, string property, int? fallback = null )
	{
		if ( value.ValueKind == JsonValueKind.Object && !value.TryGetProperty( property, out _ ) && fallback.HasValue )
			return fallback.Value;
		return Integer( Required( value, property ), property );
	}

	internal static int Integer( JsonElement value, string context )
	{
		if ( value.ValueKind == JsonValueKind.Number )
		{
			if ( value.TryGetInt32( out var integer ) ) return integer;
			// Test integrality from the decimal token. A double would round a token
			// such as 1.0000000000000001 to 1 and silently accept an invalid count.
			var token = value.GetRawText().AsSpan();
			var exponentAt = token.IndexOfAny( 'e', 'E' );
			var mantissa = exponentAt < 0 ? token : token[..exponentAt];
			long exponent = 0;
			if ( exponentAt >= 0 && !long.TryParse( token[(exponentAt + 1)..], System.Globalization.NumberStyles.AllowLeadingSign,
				System.Globalization.CultureInfo.InvariantCulture, out exponent ) )
				exponent = token[exponentAt + 1] == '-' ? long.MinValue / 4 : long.MaxValue / 4;
			exponent = Math.Clamp( exponent, long.MinValue / 4, long.MaxValue / 4 );
			var dot = mantissa.IndexOf( '.' );
			var fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
			var first = -1;
			var significant = 0;
			var trailingZeros = 0;
			for ( var i = mantissa[0] == '-' ? 1 : 0; i < mantissa.Length; i++ )
			{
				if ( mantissa[i] == '.' ) continue;
				if ( first < 0 && mantissa[i] != '0' ) first = i;
				if ( first < 0 ) continue;
				significant++;
				trailingZeros = mantissa[i] == '0' ? trailingZeros + 1 : 0;
			}
			if ( first < 0 ) return 0;
			var digits = significant - trailingZeros;
			var scale = exponent - fractionDigits + trailingZeros;
			if ( scale >= 0 && scale <= 10 && digits + scale <= 10 )
			{
				long number = 0;
				for ( var i = first; digits > 0; i++ )
				{
					if ( mantissa[i] == '.' ) continue;
					number = number * 10 + mantissa[i] - '0';
					digits--;
				}
				while ( scale-- > 0 ) number *= 10;
				if ( mantissa[0] == '-' ) number = -number;
				if ( number >= int.MinValue && number <= int.MaxValue ) return (int)number;
			}
		}
		throw new GltfException( $"'{context}' must be an integer within the supported array range." );
	}

	internal static string String( JsonElement value, string property, string fallback = null )
	{
		if ( value.ValueKind == JsonValueKind.Object && !value.TryGetProperty( property, out _ ) && fallback is not null )
			return fallback;
		var item = Required( value, property );
		if ( item.ValueKind != JsonValueKind.String ) throw new GltfException( $"'{property}' must be a string." );
		return item.GetString();
	}

	internal static bool Bool( JsonElement value, string property, bool fallback = false )
	{
		if ( value.ValueKind != JsonValueKind.Object ) throw new GltfException( "Expected a JSON object." );
		if ( !value.TryGetProperty( property, out var item ) ) return fallback;
		if ( item.ValueKind is not (JsonValueKind.True or JsonValueKind.False) )
			throw new GltfException( $"'{property}' must be a boolean." );
		return item.GetBoolean();
	}
}
