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
	public static readonly GltfElementKind Variant = new( "extensions/KHR_materials_variants/variants", "Variants", "Variant", "style" );

	/// <summary>Every kind, in hierarchy order.</summary>
	public static IReadOnlyList<GltfElementKind> All { get; } = [Scene, Node, Mesh, Material, Texture, Image, Sampler, Animation, Skin, Camera, Light, Variant];

	public static GltfElementKind Find( string path ) => All.FirstOrDefault( x => x.Path == path );

	public override string ToString() => Path;
}

/// <summary>
/// A reference to one element of an open glTF. Instances are stable for the life of
/// the document, so the hierarchy and selection can hold them.
/// </summary>
public sealed class GltfElement : IGltfSelectable
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

	/// <summary>For a node, the index of its mesh, or null.</summary>
	public int? MeshIndex => Kind == GltfElementKind.Node && Json["mesh"] is JsonValue value && value.TryGetValue<int>( out var index ) ? index : null;

	/// <summary>For a node, true if its mesh has morph targets.</summary>
	public bool HasMorphTargets => MeshIndex is { } mesh && Document.GetElements( GltfElementKind.Mesh ) is var meshes && mesh >= 0 && mesh < meshes.Count
		&& meshes[mesh].Json["primitives"] is JsonArray primitives && primitives.Any( x => x?["targets"] is JsonArray { Count: > 0 } );

	/// <summary>For a mesh, the material index of each primitive, or -1 where a primitive has none.</summary>
	public IReadOnlyList<int> PrimitiveMaterials => Kind == GltfElementKind.Mesh && Json["primitives"] is JsonArray primitives
		? primitives.Select( x => x?["material"] is JsonValue value && value.TryGetValue<int>( out var index ) ? index : -1 ).ToArray()
		: [];

	/// <summary>The current JSON of this element as text. Views use it to see that the element changed.</summary>
	public string JsonText => Json.ToJsonString();

	/// <summary>The current (edited) JSON of this element. Change it only through <see cref="GltfEditorSession"/>.</summary>
	internal JsonObject Json => Document.GetJson( this );

	public override string ToString() => $"{Kind.Path}/{Index} ({Name})";
}

/// <summary>
/// The editable model of one glTF asset: the authored source plus the overrides.
/// It holds JSON only. It does not load buffers or images.
/// </summary>
public sealed partial class GltfAuthoringDocument
{
	JsonObject source;
	readonly JsonObject root;
	byte[] sourceBytes;
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

	/// <summary>The joint nodes of a skin, in joint order.</summary>
	public IReadOnlyList<GltfElement> GetJointNodes( GltfElement skin ) => skin.Kind == GltfElementKind.Skin ? References( skin, "joints", GltfElementKind.Node ).ToList() : [];

	/// <summary>Nodes that draw a mesh with this skin.</summary>
	public IEnumerable<GltfElement> GetSkinnedNodes( GltfElement skin ) => GetElements( GltfElementKind.Node )
		.Where( x => x.MeshIndex.HasValue && x.Json["skin"] is JsonValue v && v.TryGetValue<int>( out var i ) && i == skin.Index );

	/// <summary>The variants (KHR_materials_variants) that the primitives of a mesh map, in variant order.</summary>
	public IReadOnlyList<GltfElement> GetMeshVariants( GltfElement mesh )
	{
		var variants = GetElements( GltfElementKind.Variant );
		var used = new SortedSet<int>();
		foreach ( var mapping in Mappings( mesh ) )
			if ( mapping.Mapping["variants"] is JsonArray list )
				foreach ( var item in list )
					if ( item is JsonValue v && v.TryGetValue<int>( out var i ) && i >= 0 && i < variants.Count ) used.Add( i );
		return used.Select( i => variants[i] ).ToList();
	}

	/// <summary>The material a primitive uses in a variant, or -1 when the variant does not change that primitive.</summary>
	public int GetVariantMaterial( GltfElement mesh, int primitive, GltfElement variant )
	{
		foreach ( var mapping in Mappings( mesh ) )
			if ( mapping.Primitive == primitive && mapping.Mapping["variants"] is JsonArray list && list.Any( x => x is JsonValue v && v.TryGetValue<int>( out var i ) && i == variant.Index ) )
				return mapping.Mapping["material"] is JsonValue m && m.TryGetValue<int>( out var material ) ? material : -1;
		return -1;
	}

	IEnumerable<(int Primitive, JsonObject Mapping)> Mappings( GltfElement mesh )
	{
		if ( mesh.Kind != GltfElementKind.Mesh || mesh.Json["primitives"] is not JsonArray primitives ) yield break;
		for ( var p = 0; p < primitives.Count; p++ )
			if ( primitives[p]?["extensions"]?["KHR_materials_variants"]?["mappings"] is JsonArray mappings )
				foreach ( var mapping in mappings.OfType<JsonObject>() ) yield return (p, mapping);
	}

	/// <summary>The sampling settings of a texture, from its sampler.</summary>
	public GltfTextureSampling GetTextureSampling( GltfElement texture ) =>
		texture.Json["sampler"] is JsonValue v && v.TryGetValue<int>( out var i ) ? GltfTextureSampling.Read( GltfOverrides.Collection( root, "samplers" )?[i] as JsonObject ) : GltfTextureSampling.Default;

	/// <summary>The image a texture shows, or null.</summary>
	public GltfElement GetTextureImage( GltfElement texture ) => texture.Kind == GltfElementKind.Texture ? GetImage( texture ) : null;

	/// <summary>The index of a sampler with these settings, adding one when none matches.</summary>
	internal int FindOrAddSampler( GltfTextureSampling sampling )
	{
		var samplers = GltfOverrides.Collection( root, "samplers" );
		for ( var i = 0; i < (samplers?.Count ?? 0); i++ )
			if ( samplers[i] is JsonObject sampler && sampling.Matches( sampler ) ) return i;
		return AddElement( GltfElementKind.Sampler, sampling.ToJson() ).Index;
	}

	/// <summary>The index of an image with this URI, adding one when none has it.</summary>
	internal int FindOrAddImage( string uri, string name )
	{
		var images = GltfOverrides.Collection( root, "images" );
		for ( var i = 0; i < (images?.Count ?? 0); i++ )
			if ( images[i]?["uri"] is JsonValue v && v.TryGetValue<string>( out var existing ) && existing == uri ) return i;
		return AddElement( GltfElementKind.Image, new JsonObject { ["name"] = name, ["uri"] = uri } ).Index;
	}

	/// <summary>A copy of the whole document, for undo of changes that add elements.</summary>
	internal JsonObject SnapshotRoot() => root.DeepClone().AsObject();

	/// <summary>Replace the whole document with a snapshot. Element references stay valid by kind and index.</summary>
	internal void RestoreRoot( JsonObject snapshot )
	{
		foreach ( var key in root.Select( x => x.Key ).ToList() ) root.Remove( key );
		foreach ( var (key, value) in snapshot ) root[key] = value?.DeepClone();
		Revision++;
	}

	/// <summary>Number of elements of one kind.</summary>
	public int Count( GltfElementKind kind ) => GltfOverrides.Collection( root, kind.Path )?.Count ?? 0;

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

	/// <summary>The image element behind an image or texture element, or null.</summary>
	public GltfElement GetImage( GltfElement element )
	{
		if ( element.Kind == GltfElementKind.Image ) return element;
		if ( element.Kind != GltfElementKind.Texture ) return null;
		// The loader reads the image of a supported texture extension first; show the same one.
		var source = TextureSourceSlot( element.Json ) is { } slot ? slot["source"] : element.Json["source"];
		if ( source is not JsonValue value || !value.TryGetValue<int>( out var index ) ) return null;
		var images = GetElements( GltfElementKind.Image );
		return index >= 0 && index < images.Count ? images[index] : null;
	}

	/// <summary>
	/// The texture extension object that names the image the loader uses (KHR_texture_basisu or EXT_texture_webp), or
	/// null when the core <c>source</c> does.
	/// </summary>
	internal static JsonObject TextureSourceSlot( JsonObject texture ) =>
		texture?["extensions"] is JsonObject extensions
			? extensions["KHR_texture_basisu"] as JsonObject ?? extensions["EXT_texture_webp"] as JsonObject
			: null;

	/// <summary>
	/// Make a texture use an image. A KTX2 image goes in KHR_texture_basisu; any other image goes in the core source,
	/// and an extension that named another image is removed, because the loader would still read it first.
	/// </summary>
	internal void SetTextureSource( JsonObject texture, int image )
	{
		var images = GltfOverrides.Collection( root, "images" );
		var ktx = images?[image] is JsonObject json && json["mimeType"]?.GetValue<string>() == "image/ktx2";
		var extensions = texture["extensions"] as JsonObject;
		if ( ktx )
		{
			extensions ??= (JsonObject)(texture["extensions"] = new JsonObject());
			extensions["KHR_texture_basisu"] = new JsonObject { ["source"] = image };
			extensions.Remove( "EXT_texture_webp" );
			return;
		}
		texture["source"] = image;
		extensions?.Remove( "KHR_texture_basisu" );
		extensions?.Remove( "EXT_texture_webp" );
		if ( extensions is { Count: 0 } ) texture.Remove( "extensions" );
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

	/// <summary>Index of the scene the file shows by default, or -1 if it has no scenes.</summary>
	public int DefaultSceneIndex
	{
		get
		{
			var count = GltfOverrides.Collection( root, "scenes" )?.Count ?? 0;
			if ( count == 0 ) return -1;
			return root["scene"] is JsonValue value && value.TryGetValue<int>( out var index ) && index >= 0 && index < count ? index : 0;
		}
	}

	/// <summary>The edited document as the bytes of a .gltf or .glb, in the same container as the source.</summary>
	public byte[] CreateSourceBytes()
	{
		var output = root.DeepClone().AsObject();
		GltfOverrides.SyncExtensionsUsed( output, GltfOverrides.ExtensionNamesPresent( source ) );
		return GltfOverrides.WriteJson( sourceBytes, output );
	}

	/// <summary>The saved file now holds the current document. Later changes compare against it.</summary>
	internal void MarkSaved( byte[] savedBytes )
	{
		sourceBytes = savedBytes;
		source = root.DeepClone().AsObject();
	}

	/// <summary>Append an element to a collection, creating the collection if it is missing.</summary>
	internal GltfElement AddElement( GltfElementKind kind, JsonObject json )
	{
		JsonNode parent = root;
		var parts = kind.Path.Split( '/' );
		for ( var i = 0; i < parts.Length - 1; i++ )
		{
			if ( parent[parts[i]] is not JsonObject next ) parent[parts[i]] = next = new JsonObject();
			parent = next;
		}
		if ( parent[parts[^1]] is not JsonArray collection ) parent[parts[^1]] = collection = new JsonArray();
		collection.Add( json );
		Revision++;
		return Get( kind, collection.Count - 1 );
	}

	/// <summary>Remove the last element of a collection. Used to undo <see cref="AddElement"/>.</summary>
	internal void RemoveLastElement( GltfElementKind kind )
	{
		var collection = GltfOverrides.Collection( root, kind.Path );
		if ( collection is not { Count: > 0 } ) return;
		collection.RemoveAt( collection.Count - 1 );
		Revision++;
	}

	/// <summary>
	/// The edited document as glTF bytes that show one mesh alone at the origin. The copy keeps only that
	/// mesh, so the loader does not prepare the rest of the model; buffers, materials and textures stay.
	/// </summary>
	internal byte[] CreateMeshPreviewSource( int meshIndex )
	{
		var edited = IsolatedCopy();
		var meshes = edited["meshes"] as JsonArray;
		var mesh = meshes?[meshIndex]?.DeepClone() as JsonObject ?? throw new ArgumentOutOfRangeException( nameof( meshIndex ) );
		return WriteIsolated( edited, mesh );
	}

	/// <summary>
	/// The edited document as glTF bytes that show every mesh alone at the origin: node i shows mesh i. One compile of
	/// it gives the preview model of every mesh.
	/// </summary>
	/// <param name="generated">More preview nodes after the meshes, in order: a mesh and the <c>sbox_lod</c> settings that reduce it.</param>
	internal byte[] CreateMeshesPreviewSource( IReadOnlyList<(int Mesh, JsonObject Lod)> generated = null )
	{
		var edited = IsolatedCopy();
		var all = edited["meshes"] as JsonArray;
		var meshes = all?.Select( x => x.DeepClone() as JsonObject ).ToArray() ?? [];
		if ( generated is not { Count: > 0 } ) return WriteIsolated( edited, meshes );
		// The extra nodes reuse the mesh of their level; only their extras ask the import to reduce it.
		var extra = generated.Select( x => all[x.Mesh].DeepClone() as JsonObject ).ToArray();
		var bytes = WriteIsolated( edited, [.. meshes, .. extra] );
		var written = GltfOverrides.ReadJson( bytes );
		var nodes = (JsonArray)written["nodes"];
		for ( var i = 0; i < generated.Count; i++ ) nodes[meshes.Length + i]["extras"] = new JsonObject { ["sbox_lod"] = generated[i].Lod.DeepClone() };
		return GltfOverrides.WriteJson( sourceBytes, written );
	}

	/// <summary>The edited document as glTF bytes that show one material on a sphere, for thumbnails.</summary>
	internal byte[] CreateMaterialSphereSource( int materialIndex )
	{
		var edited = IsolatedCopy();
		var mesh = GltfSphere.Append( edited, materialIndex );
		return WriteIsolated( edited, mesh );
	}

	JsonObject IsolatedCopy() => GltfOverrides.ReadJson( CreateSourceBytes() );

	byte[] WriteIsolated( JsonObject edited, params JsonObject[] meshes )
	{
		// Node data (skins, animations, instancing, lights) refers to nodes that the copy no longer has.
		var before = GltfOverrides.ExtensionNamesPresent( edited );
		edited.Remove( "animations" );
		edited.Remove( "skins" );
		edited["meshes"] = new JsonArray( meshes.Select( x => (JsonNode)x ).ToArray() );
		edited["nodes"] = new JsonArray( meshes.Select( ( _, i ) => (JsonNode)new JsonObject { ["mesh"] = i } ).ToArray() );
		edited["scenes"] = new JsonArray( new JsonObject { ["nodes"] = new JsonArray( meshes.Select( ( _, i ) => (JsonNode)JsonValue.Create( i ) ).ToArray() ) } );
		edited["scene"] = 0;
		GltfOverrides.SyncExtensionsUsed( edited, before );
		return GltfOverrides.WriteJson( sourceBytes, edited );
	}

	/// <summary>
	/// The edited document as glTF bytes that show the weights of one joint on every mesh the skin deforms:
	/// an unlit heat map from black (no influence) through blue and green to red (full weight). The meshes show
	/// in their bind space, without skinning, so the colors stay on the vertices they belong to.
	/// </summary>
	internal byte[] CreateJointWeightSource( GltfElement skin, int joint )
	{
		var edited = IsolatedCopy();
		var materials = edited["materials"] as JsonArray;
		if ( materials is null ) edited["materials"] = materials = new JsonArray();
		materials.Add( new JsonObject
		{
			["name"] = "Joint Weights",
			["pbrMetallicRoughness"] = new JsonObject { ["metallicFactor"] = 0 },
			["extensions"] = new JsonObject { ["KHR_materials_unlit"] = new JsonObject() },
		} );
		var heatMaterial = materials.Count - 1;

		var meshes = new List<JsonObject>();
		var colors = new List<float>();
		var colorAccessors = new List<(JsonObject Primitive, int Start, int Count)>();
		var bakedPositions = new List<(JsonObject Primitive, float[] Positions)>();
		foreach ( var node in GetSkinnedNodes( skin ) )
		{
			if ( edited["meshes"]?[node.MeshIndex.Value] is not JsonObject sourceMesh || sourceMesh["primitives"] is not JsonArray primitives ) continue;
			var heatPrimitives = new JsonArray();
			foreach ( var source in primitives.OfType<JsonObject>() )
			{
				if ( source["attributes"] is not JsonObject attributes || attributes["POSITION"] is not JsonValue positionValue ) continue;
				var vertexCount = edited["accessors"]?[positionValue.GetValue<int>()]?["count"]?.GetValue<int>() ?? 0;
				var weights = new float[vertexCount];
				for ( var set = 0; attributes[$"JOINTS_{set}"] is JsonValue jointsValue && attributes[$"WEIGHTS_{set}"] is JsonValue weightsValue; set++ )
				{
					var joints = GltfAccessors.Read( edited, ReadBufferView, jointsValue.GetValue<int>(), out var jointComponents );
					var values = GltfAccessors.Read( edited, ReadBufferView, weightsValue.GetValue<int>(), out var weightComponents );
					if ( joints is null || values is null || jointComponents != 4 || weightComponents != 4 ) break;
					for ( var v = 0; v < vertexCount && v * 4 + 3 < joints.Length; v++ )
						for ( var c = 0; c < 4; c++ )
							if ( (int)joints[v * 4 + c] == joint ) weights[v] += values[v * 4 + c];
				}

				// Bake the rest pose: each vertex moved by its joints' rest matrices, like the GPU would skin it.
				var positions = GltfAccessors.Read( edited, ReadBufferView, positionValue.GetValue<int>(), out var positionComponents );
				var baked = positions is not null && positionComponents == 3 ? BakeRestPose( edited, skin, attributes, positions, vertexCount ) : null;

				var primitive = new JsonObject
				{
					["attributes"] = new JsonObject(),
					["material"] = heatMaterial,
				};
				if ( baked is not null ) bakedPositions.Add( (primitive, baked) );
				else primitive["attributes"]["POSITION"] = positionValue.GetValue<int>();
				if ( source["indices"] is JsonValue indices ) primitive["indices"] = indices.GetValue<int>();
				if ( source["mode"] is JsonValue mode ) primitive["mode"] = mode.GetValue<int>();
				colorAccessors.Add( (primitive, colors.Count / 4, vertexCount) );
				foreach ( var weight in weights ) colors.AddRange( Heat( weight ) );
				heatPrimitives.Add( primitive );
			}
			if ( heatPrimitives.Count > 0 ) meshes.Add( new JsonObject { ["name"] = node.Name, ["primitives"] = heatPrimitives } );
		}

		if ( colors.Count > 0 )
		{
			var data = new byte[colors.Count * 4];
			Buffer.BlockCopy( colors.ToArray(), 0, data, 0, data.Length );
			var buffers = edited["buffers"] as JsonArray ?? (JsonArray)(edited["buffers"] = new JsonArray());
			buffers.Add( new JsonObject { ["byteLength"] = data.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( data ) } );
			var views = edited["bufferViews"] as JsonArray ?? (JsonArray)(edited["bufferViews"] = new JsonArray());
			var accessors = edited["accessors"] as JsonArray ?? (JsonArray)(edited["accessors"] = new JsonArray());
			foreach ( var (primitive, start, count) in colorAccessors )
			{
				views.Add( new JsonObject { ["buffer"] = buffers.Count - 1, ["byteOffset"] = start * 16, ["byteLength"] = Math.Max( count, 1 ) * 16, ["target"] = 34962 } );
				accessors.Add( new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["count"] = count, ["type"] = "VEC4" } );
				primitive["attributes"]["COLOR_0"] = accessors.Count - 1;
			}
		}

		foreach ( var (primitive, positions) in bakedPositions )
		{
			var data = new byte[positions.Length * 4];
			Buffer.BlockCopy( positions, 0, data, 0, data.Length );
			var buffers = (JsonArray)edited["buffers"];
			buffers.Add( new JsonObject { ["byteLength"] = data.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( data ) } );
			var views = (JsonArray)edited["bufferViews"];
			views.Add( new JsonObject { ["buffer"] = buffers.Count - 1, ["byteLength"] = Math.Max( data.Length, 4 ), ["target"] = 34962 } );
			var min = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
			var max = new float[] { float.MinValue, float.MinValue, float.MinValue };
			for ( var i = 0; i < positions.Length; i++ ) { min[i % 3] = Math.Min( min[i % 3], positions[i] ); max[i % 3] = Math.Max( max[i % 3], positions[i] ); }
			var accessors = (JsonArray)edited["accessors"];
			accessors.Add( new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["count"] = positions.Length / 3, ["type"] = "VEC3", ["min"] = new JsonArray( min[0], min[1], min[2] ), ["max"] = new JsonArray( max[0], max[1], max[2] ) } );
			primitive["attributes"]["POSITION"] = accessors.Count - 1;
		}
		return WriteIsolated( edited, meshes.ToArray() );
	}

	/// <summary>
	/// Positions skinned by the rest pose of the skin: joint world matrix times inverse bind matrix, weighted.
	/// Returns null when the skin data cannot be read.
	/// </summary>
	float[] BakeRestPose( JsonObject edited, GltfElement skin, JsonObject attributes, float[] positions, int vertexCount )
	{
		var joints = GetJointNodes( skin );
		var inverseBind = new System.Numerics.Matrix4x4[joints.Count];
		for ( var j = 0; j < joints.Count; j++ ) inverseBind[j] = System.Numerics.Matrix4x4.Identity;
		if ( skin.Json["inverseBindMatrices"] is JsonValue ibm && ibm.TryGetValue<int>( out var ibmIndex ) )
		{
			var values = GltfAccessors.Read( edited, ReadBufferView, ibmIndex, out var components );
			if ( values is null || components != 16 ) return null;
			for ( var j = 0; j < joints.Count && j * 16 + 15 < values.Length; j++ )
				inverseBind[j] = new System.Numerics.Matrix4x4(
					values[j * 16], values[j * 16 + 1], values[j * 16 + 2], values[j * 16 + 3],
					values[j * 16 + 4], values[j * 16 + 5], values[j * 16 + 6], values[j * 16 + 7],
					values[j * 16 + 8], values[j * 16 + 9], values[j * 16 + 10], values[j * 16 + 11],
					values[j * 16 + 12], values[j * 16 + 13], values[j * 16 + 14], values[j * 16 + 15] );
		}
		// Row-vector form: a glTF column-major array read in order is the transposed matrix that System.Numerics uses.
		var worlds = GltfSpatial.WorldMatrices( this );
		var skinning = joints.Select( ( node, j ) => inverseBind[j] * (worlds.TryGetValue( node, out var w ) ? w : System.Numerics.Matrix4x4.Identity) ).ToArray();

		var output = new float[vertexCount * 3];
		var total = new float[vertexCount];
		for ( var set = 0; attributes[$"JOINTS_{set}"] is JsonValue jointsValue && attributes[$"WEIGHTS_{set}"] is JsonValue weightsValue; set++ )
		{
			var jointData = GltfAccessors.Read( edited, ReadBufferView, jointsValue.GetValue<int>(), out var jc );
			var weightData = GltfAccessors.Read( edited, ReadBufferView, weightsValue.GetValue<int>(), out var wc );
			if ( jointData is null || weightData is null || jc != 4 || wc != 4 ) return null;
			for ( var v = 0; v < vertexCount; v++ )
			{
				var p = new System.Numerics.Vector3( positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2] );
				for ( var c = 0; c < 4; c++ )
				{
					var weight = weightData[v * 4 + c];
					var joint = (int)jointData[v * 4 + c];
					if ( weight <= 0 || joint < 0 || joint >= skinning.Length ) continue;
					var moved = System.Numerics.Vector3.Transform( p, skinning[joint] );
					output[v * 3] += moved.X * weight; output[v * 3 + 1] += moved.Y * weight; output[v * 3 + 2] += moved.Z * weight;
					total[v] += weight;
				}
			}
		}
		for ( var v = 0; v < vertexCount; v++ )
		{
			if ( total[v] > 0 ) { output[v * 3] /= total[v]; output[v * 3 + 1] /= total[v]; output[v * 3 + 2] /= total[v]; }
			else { output[v * 3] = positions[v * 3]; output[v * 3 + 1] = positions[v * 3 + 1]; output[v * 3 + 2] = positions[v * 3 + 2]; }
		}
		return output;
	}

	/// <summary>Linear RGBA for a joint weight: black at 0, then blue, green, yellow, red at 1.</summary>
	static float[] Heat( float weight )
	{
		if ( weight <= 0.001f ) return [0.02f, 0.02f, 0.03f, 1];
		var t = Math.Clamp( weight, 0, 1 );
		var r = Math.Clamp( 2 * t - 0.5f, 0, 1 );
		var g = t < 0.5f ? 2 * t : 2 - 2 * t;
		var b = Math.Clamp( 1 - 2 * t, 0, 1 );
		return [r, g, b, 1];
	}

	// An element can outlive its JSON when undo removes added elements; it then reads as empty.
	internal JsonObject GetJson( GltfElement element ) =>
		GltfOverrides.Collection( root, element.Kind.Path ) is { } collection && element.Index < collection.Count
			? collection[element.Index] as JsonObject ?? new JsonObject()
			: new JsonObject();

	/// <summary>Change one element. Returns snapshots before and after, for undo.</summary>
	internal (JsonObject Before, JsonObject After) Mutate( GltfElement element, Action<JsonObject> mutate, bool bumpRevision = true )
	{
		var json = GetJson( element );
		var before = json.DeepClone().AsObject();
		mutate( json );
		if ( bumpRevision ) Revision++;
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
