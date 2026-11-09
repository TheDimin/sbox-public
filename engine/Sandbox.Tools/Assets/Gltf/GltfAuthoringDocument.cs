using System;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// One kind of glTF element: a top-level collection such as materials, or an
/// extension collection such as KHR_lights_punctual lights. This table is the
/// only list of kinds. The hierarchy groups and the facet lookup come from it.
/// </summary>
public sealed class GltfElementKind
{
	/// <summary>Slash path of the collection in the glTF root, such as "materials".</summary>
	public string Path { get; }
	public string Title { get; }
	public string Singular { get; }
	public string Icon { get; }

	GltfElementKind( string path, string title, string singular, string icon )
	{
		Path = path;
		Title = title;
		Singular = singular;
		Icon = icon;
	}

	public static readonly GltfElementKind Scene = new( "scenes", "Scenes", "Scene", "perm_media" );
	public static readonly GltfElementKind Node = new( "nodes", "Nodes", "Node", "account_tree" );
	public static readonly GltfElementKind Mesh = new( "meshes", "Meshes", "Mesh", "view_in_ar" );
	public static readonly GltfElementKind Material = new( "materials", "Materials", "Material", "palette" );
	public static readonly GltfElementKind Texture = new( "textures", "Textures", "Texture", "texture" );
	public static readonly GltfElementKind Image = new( "images", "Images", "Image", "image" );
	public static readonly GltfElementKind Sampler = new( "samplers", "Samplers", "Sampler", "tune" );
	public static readonly GltfElementKind Animation = new( "animations", "Animations", "Animation", "animation" );
	public static readonly GltfElementKind Skin = new( "skins", "Skins", "Skin", "accessibility" );
	public static readonly GltfElementKind Camera = new( "cameras", "Cameras", "Camera", "videocam" );
	public static readonly GltfElementKind Light = new( "extensions/KHR_lights_punctual/lights", "Lights", "Light", "lightbulb" );

	/// <summary>Every kind, in hierarchy order.</summary>
	public static IReadOnlyList<GltfElementKind> All { get; } = [Scene, Node, Mesh, Material, Texture, Image, Sampler, Animation, Skin, Camera, Light];

	public static GltfElementKind Find( string path ) => All.FirstOrDefault( x => x.Path == path );

	public override string ToString() => Path;
}

/// <summary>
/// A reference to one element of an open glTF. Instances are stable for the life of
/// the document, so the hierarchy and selection can hold them.
/// </summary>
public sealed class GltfElement
{
	public GltfAuthoringDocument Document { get; }
	public GltfElementKind Kind { get; }
	public int Index { get; }

	internal GltfElement( GltfAuthoringDocument document, GltfElementKind kind, int index )
	{
		Document = document;
		Kind = kind;
		Index = index;
	}

	/// <summary>Authored name, or a generated name if the element has none.</summary>
	public string Name => Json["name"] is JsonValue value && value.TryGetValue<string>( out var name ) && !string.IsNullOrWhiteSpace( name )
		? name
		: $"{Kind.Singular} {Index}";

	/// <summary>The current (edited) JSON of this element. Change it only through <see cref="GltfEditorSession"/>.</summary>
	internal JsonObject Json => Document.GetJson( this );

	public override string ToString() => $"{Kind.Path}/{Index} ({Name})";
}

/// <summary>
/// The editable model of one glTF asset: the authored source plus the overrides.
/// It holds JSON only. It does not load buffers or images.
/// </summary>
public sealed class GltfAuthoringDocument
{
	readonly JsonObject source;
	readonly JsonObject root;
	readonly byte[] sourceBytes;
	readonly string baseDirectory;
	readonly Dictionary<(GltfElementKind, int), GltfElement> elements = new();

	/// <summary>Increments on every change, so views can see that they are stale.</summary>
	public int Revision { get; private set; }

	GltfAuthoringDocument( JsonObject source, JsonObject edited, byte[] sourceBytes, string baseDirectory )
	{
		this.source = source;
		root = edited;
		this.sourceBytes = sourceBytes;
		this.baseDirectory = baseDirectory;
	}

	/// <summary>Load a document from source bytes (.gltf or .glb) and its saved overrides.</summary>
	public static GltfAuthoringDocument Load( byte[] sourceBytes, JsonObject overrides, List<string> warnings = null, string baseDirectory = null )
	{
		var source = GltfOverrides.ReadJson( sourceBytes );
		var edited = source.DeepClone().AsObject();
		if ( overrides is not null ) GltfOverrides.ApplyToRoot( edited, overrides, warnings );
		return new GltfAuthoringDocument( source, edited, sourceBytes, baseDirectory );
	}

	/// <summary>All elements of one kind, in source order.</summary>
	public IReadOnlyList<GltfElement> GetElements( GltfElementKind kind )
	{
		var count = GltfOverrides.Collection( root, kind.Path )?.Count ?? 0;
		var list = new GltfElement[count];
		for ( var i = 0; i < count; i++ ) list[i] = Get( kind, i );
		return list;
	}

	/// <summary>Hierarchy children: the root nodes of a scene, or the child nodes of a node. Other kinds have none.</summary>
	public IEnumerable<GltfElement> GetChildren( GltfElement element ) =>
		element.Kind == GltfElementKind.Scene ? References( element, "nodes", GltfElementKind.Node )
		: element.Kind == GltfElementKind.Node ? References( element, "children", GltfElementKind.Node )
		: [];

	/// <summary>True if <paramref name="target"/> is below <paramref name="ancestor"/> in the hierarchy.</summary>
	public bool IsDescendant( GltfElement ancestor, GltfElement target )
	{
		var seen = new HashSet<GltfElement>();
		var stack = new Stack<GltfElement>( GetChildren( ancestor ) );
		while ( stack.TryPop( out var current ) )
		{
			if ( current == target ) return true;
			if ( seen.Add( current ) )
				foreach ( var child in GetChildren( current ) ) stack.Push( child );
		}
		return false;
	}

	/// <summary>Nodes that no scene reaches. They still exist in the file, so the hierarchy must show them.</summary>
	public IEnumerable<GltfElement> GetUnreachableNodes()
	{
		var reached = new HashSet<GltfElement>();
		var stack = new Stack<GltfElement>( GetElements( GltfElementKind.Scene ).SelectMany( GetChildren ) );
		while ( stack.TryPop( out var node ) )
			if ( reached.Add( node ) )
				foreach ( var child in GetChildren( node ) ) stack.Push( child );
		return GetElements( GltfElementKind.Node ).Where( x => !reached.Contains( x ) );
	}

	/// <summary>The overrides that change the source into the current document. Null if nothing changed.</summary>
	public JsonObject CreateOverrides()
	{
		var overrides = new JsonObject();
		foreach ( var kind in GltfElementKind.All )
		{
			var before = GltfOverrides.Collection( source, kind.Path );
			var after = GltfOverrides.Collection( root, kind.Path );
			if ( before is null || after is null ) continue;
			var patches = new JsonObject();
			for ( var i = 0; i < Math.Min( before.Count, after.Count ); i++ )
				if ( before[i] is JsonObject a && after[i] is JsonObject b && GltfOverrides.CreatePatch( a, b ) is { } patch )
					patches[i.ToString()] = patch;
			if ( patches.Count > 0 ) overrides[kind.Path] = patches;
		}
		return overrides.Count > 0 ? overrides : null;
	}

	/// <summary>
	/// The encoded bytes (PNG, JPEG ...) of an image element, or null if they cannot be read.
	/// Reads from a data URI, a file next to the source, or a buffer view.
	/// </summary>
	public byte[] ReadImageBytes( GltfElement image )
	{
		if ( image.Kind != GltfElementKind.Image ) return null;
		try
		{
			var json = image.Json;
			if ( json["uri"] is JsonValue uriValue && uriValue.TryGetValue<string>( out var uri ) )
			{
				if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
				{
					var comma = uri.IndexOf( ',' );
					return comma < 0 ? null : Convert.FromBase64String( uri[(comma + 1)..] );
				}
				if ( baseDirectory is null ) return null;
				var path = System.IO.Path.Combine( baseDirectory, Uri.UnescapeDataString( uri ) );
				return System.IO.File.Exists( path ) ? System.IO.File.ReadAllBytes( path ) : null;
			}
			if ( json["bufferView"] is JsonValue viewValue && viewValue.TryGetValue<int>( out var viewIndex ) )
				return ReadBufferView( viewIndex );
		}
		catch ( Exception e ) when ( e is FormatException or System.IO.IOException or InvalidCastException or ArgumentException )
		{
			return null;
		}
		return null;
	}

	byte[] ReadBufferView( int viewIndex )
	{
		var view = GltfOverrides.Collection( root, "bufferViews" )?[viewIndex] as JsonObject;
		if ( view is null ) return null;
		var bufferIndex = view["buffer"]?.GetValue<int>() ?? 0;
		var offset = view["byteOffset"]?.GetValue<int>() ?? 0;
		var length = view["byteLength"]?.GetValue<int>() ?? 0;
		var buffer = GltfOverrides.Collection( root, "buffers" )?[bufferIndex] as JsonObject;
		byte[] data;
		if ( buffer?["uri"] is JsonValue uriValue && uriValue.TryGetValue<string>( out var uri ) )
		{
			if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) ) data = Convert.FromBase64String( uri[(uri.IndexOf( ',' ) + 1)..] );
			else if ( baseDirectory is not null && System.IO.File.Exists( System.IO.Path.Combine( baseDirectory, Uri.UnescapeDataString( uri ) ) ) )
				data = System.IO.File.ReadAllBytes( System.IO.Path.Combine( baseDirectory, Uri.UnescapeDataString( uri ) ) );
			else return null;
		}
		else data = GltfOverrides.ReadGlbBinChunk( sourceBytes );
		if ( data is null || offset < 0 || length <= 0 || (long)offset + length > data.Length ) return null;
		return data.AsSpan( offset, length ).ToArray();
	}

	internal JsonObject RootForRead => root;

	/// <summary>The node that lists <paramref name="node"/> as a child, or null for a root node.</summary>
	internal GltfElement GetParent( GltfElement node ) =>
		GetElements( GltfElementKind.Node ).FirstOrDefault( x => GetChildren( x ).Contains( node ) );

	internal JsonObject GetJson( GltfElement element ) =>
		GltfOverrides.Collection( root, element.Kind.Path )?[element.Index] as JsonObject ?? new JsonObject();

	/// <summary>Change one element. Returns snapshots before and after, for undo.</summary>
	internal (JsonObject Before, JsonObject After) Mutate( GltfElement element, Action<JsonObject> mutate )
	{
		var json = GetJson( element );
		var before = json.DeepClone().AsObject();
		mutate( json );
		Revision++;
		return (before, json.DeepClone().AsObject());
	}

	/// <summary>Replace the JSON of one element with a snapshot. Used by undo and redo.</summary>
	internal void Restore( GltfElement element, JsonObject snapshot )
	{
		var collection = GltfOverrides.Collection( root, element.Kind.Path );
		collection[element.Index] = snapshot.DeepClone();
		Revision++;
	}

	GltfElement Get( GltfElementKind kind, int index )
	{
		if ( !elements.TryGetValue( (kind, index), out var element ) )
			elements[(kind, index)] = element = new GltfElement( this, kind, index );
		return element;
	}

	IEnumerable<GltfElement> References( GltfElement owner, string key, GltfElementKind kind )
	{
		if ( owner.Json[key] is not JsonArray list ) yield break;
		var count = GltfOverrides.Collection( root, kind.Path )?.Count ?? 0;
		foreach ( var item in list )
			if ( item is JsonValue value && value.TryGetValue<int>( out var index ) && index >= 0 && index < count )
				yield return Get( kind, index );
	}
}
