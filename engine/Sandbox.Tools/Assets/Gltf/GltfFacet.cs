using System;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor;

/// <summary>
/// Declares a <see cref="GltfFacet"/>: which element kind it belongs to and where its data is.
/// </summary>
/// <param name="kind">Collection path of the element kind, such as "materials". See <see cref="GltfElementKind.Path"/>.</param>
/// <param name="path">
/// Slash path of the facet object in the element. An empty path is the element itself, which is always
/// present and cannot be removed. A path such as "extensions/KHR_materials_clearcoat" makes an extension;
/// the import adds it to extensionsUsed, and removes it when the user removes the facet.
/// </param>
[AttributeUsage( AttributeTargets.Class, Inherited = false )]
public sealed class GltfFacetForAttribute( string kind, string path ) : Attribute
{
	public string Kind { get; } = kind;
	public string Path { get; } = path ?? "";
}

/// <summary>
/// One group of properties on a glTF element. The inspector shows each facet as one sheet,
/// like a component. Users add and remove facets like components.
/// <para>
/// To support a new glTF extension, declare a class with <see cref="GltfFacetForAttribute"/> and
/// public properties. The base class maps each property to the JSON key of the same name in camelCase
/// (or <see cref="JsonPropertyNameAttribute"/>). It keeps JSON keys that it does not know.
/// Supported property types: bool, int, float, string, enums (written as their name, or by
/// <see cref="JsonPropertyNameAttribute"/> on the member), Vector2, Vector3, Rotation (xyzw), Color (rgba, or rgb with <see cref="GltfRgbAttribute"/>), int[] and float[].
/// Override <see cref="Read"/> and <see cref="Write"/> only when the data does not fit this mapping.
/// </para>
/// </summary>
public abstract class GltfFacet
{
	/// <summary>The element this facet was read from.</summary>
	[Hide, JsonIgnore]
	public GltfElement Element { get; internal set; }

	internal string Path { get; set; }

	/// <summary>True if this facet is a glTF extension.</summary>
	[Hide, JsonIgnore]
	public bool IsExtension => Path.StartsWith( "extensions/", StringComparison.Ordinal );

	/// <summary>True if the user can remove this facet. The element itself is not removable.</summary>
	[Hide, JsonIgnore]
	public bool IsRemovable => Path.Length > 0;

	/// <summary>Fill the properties from the facet object.</summary>
	protected internal virtual void Read( JsonObject target )
	{
		foreach ( var property in Properties( GetType() ) )
			if ( target[KeyOf( property )] is { } node && GltfValueCodec.TryDecode( node, property.PropertyType, out var value ) )
				property.SetValue( this, value );
	}

	/// <summary>Write one property to the facet object. A null <paramref name="propertyName"/> writes all properties.</summary>
	protected internal virtual void Write( JsonObject target, string propertyName )
	{
		foreach ( var property in Properties( GetType() ) )
		{
			if ( propertyName is not null && property.Name != propertyName ) continue;
			var encoded = GltfValueCodec.Encode( property.GetValue( this ), property.GetCustomAttribute<GltfRgbAttribute>() is not null );
			if ( encoded is null ) target.Remove( KeyOf( property ) );
			else target[KeyOf( property )] = encoded;
		}
	}

	/// <summary>Find the facet object in the element JSON. With <paramref name="create"/>, make missing objects.</summary>
	internal JsonObject Resolve( JsonObject element, bool create )
	{
		var node = element;
		if ( Path.Length == 0 ) return node;
		foreach ( var part in Path.Split( '/' ) )
		{
			if ( node[part] is JsonObject next ) { node = next; continue; }
			if ( !create ) return null;
			node[part] = next = new JsonObject();
			node = next;
		}
		return node;
	}

	/// <summary>Remove the facet object, and parent objects that become empty.</summary>
	internal void RemoveFrom( JsonObject element )
	{
		var parts = Path.Split( '/' );
		var chain = new List<JsonObject> { element };
		for ( var i = 0; i < parts.Length - 1; i++ )
		{
			if ( chain[^1][parts[i]] is not JsonObject next ) return;
			chain.Add( next );
		}
		chain[^1].Remove( parts[^1] );
		for ( var i = chain.Count - 1; i > 0 && chain[i].Count == 0; i-- )
			chain[i - 1].Remove( parts[i - 1] );
	}

	static IEnumerable<PropertyInfo> Properties( Type type ) => type
		.GetProperties( BindingFlags.Instance | BindingFlags.Public )
		.Where( x => x.CanRead && x.CanWrite && x.DeclaringType != typeof( GltfFacet ) && x.GetCustomAttribute<JsonIgnoreAttribute>() is null );

	static string KeyOf( PropertyInfo property ) =>
		property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? char.ToLowerInvariant( property.Name[0] ) + property.Name[1..];
}

/// <summary>Write a <see cref="Color"/> facet property as three floats (rgb), such as emissiveFactor.</summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class GltfRgbAttribute : Attribute { }

/// <summary>Finds facet types and creates facet views for an element.</summary>
public static class GltfFacets
{
	/// <summary>Facet types declared for an element kind.</summary>
	public static IEnumerable<Type> TypesFor( GltfElementKind kind ) => EditorTypeLibrary
		.GetTypesWithAttribute<GltfFacetForAttribute>()
		.Where( x => x.Attribute.Kind == kind.Path && x.Type.TargetType.IsAssignableTo( typeof( GltfFacet ) ) && !x.Type.TargetType.IsAbstract )
		.Select( x => x.Type.TargetType )
		.OrderBy( x => x.GetCustomAttribute<GltfFacetForAttribute>().Path.Length == 0 ? 0 : 1 )
		.ThenBy( x => x.GetCustomAttribute<GltfFacetForAttribute>().Path, StringComparer.Ordinal );

	/// <summary>Facets that the element has now, read from its current JSON.</summary>
	public static IEnumerable<GltfFacet> GetPresent( GltfElement element )
	{
		foreach ( var type in TypesFor( element.Kind ) )
		{
			var facet = Create( type, element );
			if ( facet.Resolve( element.Json, false ) is { } target )
			{
				facet.Read( target );
				yield return facet;
			}
		}
	}

	/// <summary>Facet types that the user can add to the element.</summary>
	public static IEnumerable<Type> GetAddable( GltfElement element ) =>
		TypesFor( element.Kind ).Where( type => Create( type, element ).Resolve( element.Json, false ) is null );

	/// <summary>Create a facet with default values that is not yet part of the element.</summary>
	internal static GltfFacet Create( Type type, GltfElement element )
	{
		var attribute = type.GetCustomAttribute<GltfFacetForAttribute>() ?? throw new ArgumentException( $"{type} has no {nameof( GltfFacetForAttribute )}." );
		var facet = (GltfFacet)Activator.CreateInstance( type );
		facet.Element = element;
		facet.Path = attribute.Path;
		return facet;
	}
}

/// <summary>Maps C# property values to glTF JSON values and back.</summary>
internal static class GltfValueCodec
{
	public static JsonNode Encode( object value, bool rgb = false ) => value switch
	{
		null => null,
		bool b => JsonValue.Create( b ),
		int i => JsonValue.Create( i ),
		float f => JsonValue.Create( f ),
		double d => JsonValue.Create( d ),
		string s => string.IsNullOrEmpty( s ) ? null : JsonValue.Create( s ),
		Enum e => JsonValue.Create( EnumName( e ) ),
		Vector2 v => Array( v.x, v.y ),
		Vector3 v => Array( v.x, v.y, v.z ),
		Rotation r => Array( r.x, r.y, r.z, r.w ),
		Color c when rgb => Array( c.r, c.g, c.b ),
		Color c => Array( c.r, c.g, c.b, c.a ),
		int[] ints => ints.Length == 0 ? null : new JsonArray( ints.Select( x => (JsonNode)JsonValue.Create( x ) ).ToArray() ),
		float[] floats => floats.Length == 0 ? null : Array( floats ),
		_ => throw new NotSupportedException( $"glTF facet property type {value.GetType()} is not supported. Override Read and Write." ),
	};

	public static bool TryDecode( JsonNode node, Type type, out object value )
	{
		value = null;
		try
		{
			if ( type == typeof( bool ) ) value = node.GetValue<bool>();
			else if ( type == typeof( int ) ) value = node.GetValue<int>();
			else if ( type == typeof( float ) ) value = node.GetValue<float>();
			else if ( type == typeof( double ) ) value = node.GetValue<double>();
			else if ( type == typeof( string ) ) value = node.GetValue<string>();
			else if ( type.IsEnum ) value = ParseEnum( type, node.GetValue<string>() );
			else if ( type == typeof( int[] ) && node is JsonArray ints ) value = ints.Select( x => x.GetValue<int>() ).ToArray();
			else if ( type == typeof( float[] ) && node is JsonArray floats ) value = floats.Select( x => x.GetValue<float>() ).ToArray();
			else if ( node is JsonArray a )
			{
				var f = a.Select( x => x.GetValue<float>() ).ToArray();
				if ( type == typeof( Vector2 ) && f.Length == 2 ) value = new Vector2( f[0], f[1] );
				else if ( type == typeof( Vector3 ) && f.Length == 3 ) value = new Vector3( f[0], f[1], f[2] );
				else if ( type == typeof( Rotation ) && f.Length == 4 ) value = new Rotation( f[0], f[1], f[2], f[3] );
				else if ( type == typeof( Color ) && f.Length is 3 or 4 ) value = new Color( f[0], f[1], f[2], f.Length == 4 ? f[3] : 1 );
			}
		}
		catch ( Exception e ) when ( e is FormatException or InvalidOperationException or ArgumentException )
		{
			value = null;
		}
		return value is not null;
	}

	static JsonArray Array( params float[] values ) => new( values.Select( x => (JsonNode)JsonValue.Create( x ) ).ToArray() );

	static string EnumName( Enum value ) =>
		value.GetType().GetField( value.ToString() )?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? value.ToString();

	static object ParseEnum( Type type, string name )
	{
		foreach ( var field in type.GetFields( BindingFlags.Public | BindingFlags.Static ) )
			if ( (field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? field.Name) == name ) return field.GetValue( null );
		return null;
	}
}
