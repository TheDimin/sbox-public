using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public enum SamplerFilter
{
	Aniso,
	Bilinear,
	Trilinear,
	Point,
}

public enum SamplerAddress
{
	Wrap,
	Mirror,
	Clamp,
	Border,
	Mirror_Once,
}

public struct Sampler
{
	/// <summary>
	/// Smooth or Pixelated filtering
	/// </summary>
	public SamplerFilter Filter { get; set; }

	/// <summary>
	/// Horizontal wrapping, repeating or stretched
	/// </summary>
	public SamplerAddress AddressU { get; set; }

	/// <summary>
	/// Vertical wrapping, repeating or stretched
	/// </summary>
	public SamplerAddress AddressV { get; set; }

	/// <summary>
	/// Optional render attribute containing the runtime sampler state. When
	/// omitted, filtering and addressing use this sampler's static settings.
	/// Names must be ASCII identifiers, starting with a letter or underscore.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string Attribute
	{
		readonly get => attribute;
		set => attribute = string.IsNullOrEmpty( value ) ? null : value;
	}

	private string attribute;

	internal readonly void ValidateAttribute()
	{
		if ( attribute is null ) return;

		if ( !IsAttributeStart( attribute[0] ) )
			throw new ArgumentException( "Sampler attribute names must be ASCII identifiers.", nameof( Attribute ) );

		foreach ( var character in attribute )
		{
			if ( !IsAttributeStart( character ) && character is not (>= '0' and <= '9') )
				throw new ArgumentException( "Sampler attribute names must be ASCII identifiers.", nameof( Attribute ) );
		}
	}

	private static bool IsAttributeStart( char value ) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
}
