using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf;

/// <summary>A group of mesh vertex attributes that compile compression can store in fewer bits.</summary>
public enum GltfAttributeGroup { Position, Normal, Tangent, TexCoord, Color, Weights }

/// <summary>
/// How the compiled file stores an attribute group. The size is a maximum: data that the source already stores
/// in the same or fewer bits stays as it is, so compression never makes data larger.
/// </summary>
public enum GltfAttributeStorage { Source, Int16, Int8 }

/// <summary>Compile compression for a glTF file: one storage per attribute group, with overrides per primitive.</summary>
public sealed class GltfCompressionSettings
{
	/// <summary>The storage of each group for every primitive. A missing group is <see cref="GltfAttributeStorage.Source"/>.</summary>
	public Dictionary<GltfAttributeGroup, GltfAttributeStorage> Global { get; } = new();

	/// <summary>Storage that replaces <see cref="Global"/> for one primitive, by mesh index and primitive index.</summary>
	public Dictionary<(int Mesh, int Primitive), Dictionary<GltfAttributeGroup, GltfAttributeStorage>> Primitives { get; } = new();

	/// <summary>Store indices in 16 bits and joint indices in 8 bits where every value fits. This loses no data.</summary>
	public bool CompactIntegers { get; set; }

	/// <summary>The storage that applies to one group of one primitive.</summary>
	public GltfAttributeStorage Get( int mesh, int primitive, GltfAttributeGroup group ) =>
		Primitives.TryGetValue( (mesh, primitive), out var overrides ) && overrides.TryGetValue( group, out var storage ) ? storage : Global.GetValueOrDefault( group );

	/// <summary>True when no setting changes the compiled data.</summary>
	public bool IsEmpty => !CompactIntegers && Global.Values.All( x => x == GltfAttributeStorage.Source ) && Primitives.Values.All( x => x.Values.All( y => y == GltfAttributeStorage.Source ) );

	public JsonObject ToJson()
	{
		static JsonObject Groups( Dictionary<GltfAttributeGroup, GltfAttributeStorage> groups )
		{
			var json = new JsonObject();
			foreach ( var (group, storage) in groups.OrderBy( x => x.Key ) ) json[Name( group )] = Name( storage );
			return json;
		}
		var root = new JsonObject();
		if ( CompactIntegers ) root["compactIntegers"] = true;
		if ( Global.Count > 0 ) root["global"] = Groups( Global );
		var primitives = new JsonObject();
		foreach ( var (key, groups) in Primitives.OrderBy( x => x.Key ) )
			if ( groups.Count > 0 ) primitives[$"{key.Mesh}/{key.Primitive}"] = Groups( groups );
		if ( primitives.Count > 0 ) root["primitives"] = primitives;
		return root;
	}

	/// <summary>Read settings written by <see cref="ToJson"/>. Unknown names are ignored.</summary>
	public static GltfCompressionSettings FromJson( JsonNode node )
	{
		static void ReadGroups( JsonNode json, Dictionary<GltfAttributeGroup, GltfAttributeStorage> into )
		{
			if ( json is not JsonObject groups ) return;
			foreach ( var (name, value) in groups )
				if ( Enum.TryParse<GltfAttributeGroup>( name, true, out var group ) && value is JsonValue text && text.TryGetValue<string>( out var storageName )
					&& Enum.TryParse<GltfAttributeStorage>( storageName, true, out var storage ) )
					into[group] = storage;
		}
		var settings = new GltfCompressionSettings();
		if ( node is not JsonObject root ) return settings;
		settings.CompactIntegers = root["compactIntegers"] is JsonValue compact && compact.TryGetValue<bool>( out var on ) && on;
		ReadGroups( root["global"], settings.Global );
		if ( root["primitives"] is JsonObject primitives )
			foreach ( var (key, value) in primitives )
			{
				var parts = key.Split( '/' );
				if ( parts.Length != 2 || !int.TryParse( parts[0], out var mesh ) || !int.TryParse( parts[1], out var primitive ) ) continue;
				var groups = new Dictionary<GltfAttributeGroup, GltfAttributeStorage>();
				ReadGroups( value, groups );
				if ( groups.Count > 0 ) settings.Primitives[(mesh, primitive)] = groups;
			}
		return settings;
	}

	static string Name<T>( T value ) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName( value.ToString() );
}

/// <summary>What one compression pass changed.</summary>
/// <param name="SourceBytes">The bytes of the accessors that the pass rewrote, before it ran.</param>
/// <param name="CompressedBytes">The bytes of the same accessors after it ran.</param>
public readonly record struct GltfCompressionResult( long SourceBytes, long CompressedBytes, int Accessors );

/// <summary>
/// Stores mesh accessor data in fewer bits, with KHR_mesh_quantization where core glTF does not allow the layout.
/// It runs on a compiled GLB that has one embedded buffer, and keeps every element index the same.
/// </summary>
public static class GltfCompression
{
	const int Byte = 5120, UnsignedByte = 5121, Short = 5122, UnsignedShort = 5123, UnsignedInt = 5125, Float = 5126;
	const string Quantization = "KHR_mesh_quantization";

	/// <summary>
	/// Compress <paramref name="glb"/>. Data that a setting cannot store (such as texture coordinates outside -1..1) stays
	/// as it is and adds a warning. Returns the input when nothing changes.
	/// </summary>
	public static byte[] Apply( byte[] glb, GltfCompressionSettings settings, IReadOnlySet<string> supportedExtensions, ICollection<string> warnings, out GltfCompressionResult result )
	{
		ArgumentNullException.ThrowIfNull( glb );
		ArgumentNullException.ThrowIfNull( settings );
		result = default;
		if ( settings.IsEmpty ) return glb;
		using var document = GltfReader.Read( glb, null, supportedExtensions );
		var root = JsonNode.Parse( document.Root.GetRawText() )?.AsObject() ?? throw new GltfException( "glTF root must be an object." );
		if ( Refusal( root, document, supportedExtensions ) is { } reason )
		{
			warnings?.Add( $"Compression was not applied: {reason}" );
			return glb;
		}
		var pass = new Pass( document, root, settings, warnings );
		pass.Run();
		if ( pass.Rewritten == 0 ) return glb;
		result = new GltfCompressionResult( pass.SourceBytes, pass.CompressedBytes, pass.Rewritten );
		return pass.Write();
	}

	static string Refusal( JsonObject root, GltfDocument document, IReadOnlySet<string> supportedExtensions )
	{
		if ( document.CompilerBufferCount != 1 ) return "the file must have exactly one buffer. Turn on Consolidate Geometry Buffers.";
		if ( root["buffers"]?[0]?["uri"] is not null ) return "the buffer must be embedded in the GLB.";
		if ( supportedExtensions is not null && !supportedExtensions.Contains( Quantization ) ) return $"the loader does not support {Quantization}.";
		// Element indices stay the same, but buffer views move. Extensions that can name a buffer view stop the pass.
		return FindUnmovableExtension( root ) is { } name ? $"the extension {name} can refer to buffer data." : null;
	}

	static string FindUnmovableExtension( JsonNode node )
	{
		switch ( node )
		{
			case JsonArray array:
				foreach ( var item in array ) if ( FindUnmovableExtension( item ) is { } found ) return found;
				break;
			case JsonObject json:
				foreach ( var (name, value) in json )
				{
					if ( name == "extensions" && value is JsonObject extensions )
					{
						foreach ( var (extension, payload) in extensions )
						{
							if ( extension == "KHR_draco_mesh_compression" || !GltfCompiler.MovableExtensions.Contains( extension ) ) return extension;
							if ( FindUnmovableExtension( payload ) is { } nested ) return nested;
						}
					}
					else if ( FindUnmovableExtension( value ) is { } found ) return found;
				}
				break;
		}
		return null;
	}

	static int ComponentSize( int componentType ) => componentType switch { Byte or UnsignedByte => 1, Short or UnsignedShort => 2, _ => 4 };

	static int Components( string type ) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT2" => 4, "MAT3" => 9, "MAT4" => 16, _ => 0 };

	/// <summary>The bytes one element takes in a vertex buffer, padded to four as glTF requires.</summary>
	static int VertexStride( int componentType, int components ) => (ComponentSize( componentType ) * components + 3) & ~3;

	static GltfAttributeGroup? Group( string semantic ) => semantic switch
	{
		"POSITION" => GltfAttributeGroup.Position,
		"NORMAL" => GltfAttributeGroup.Normal,
		"TANGENT" => GltfAttributeGroup.Tangent,
		_ when semantic.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) => GltfAttributeGroup.TexCoord,
		_ when semantic.StartsWith( "COLOR_", StringComparison.Ordinal ) => GltfAttributeGroup.Color,
		_ when semantic.StartsWith( "WEIGHTS_", StringComparison.Ordinal ) => GltfAttributeGroup.Weights,
		_ => null,
	};

	static int Bits( GltfAttributeStorage storage ) => storage switch { GltfAttributeStorage.Int16 => 16, GltfAttributeStorage.Int8 => 8, _ => 32 };

	sealed class Pass( GltfDocument document, JsonObject root, GltfCompressionSettings settings, ICollection<string> warnings )
	{
		readonly JsonArray accessors = root["accessors"] as JsonArray ?? new JsonArray();
		readonly JsonArray views = root["bufferViews"] as JsonArray ?? new JsonArray();
		readonly Dictionary<int, byte[]> newViewData = new();
		readonly HashSet<string> reported = new();
		bool needsQuantization;

		public int Rewritten;
		public long SourceBytes, CompressedBytes;

		readonly record struct Use( int Mesh, int Primitive, string Semantic );

		public void Run()
		{
			var uses = new Dictionary<int, List<Use>>();
			var locked = new HashSet<int>();
			var indices = new HashSet<int>();
			var meshes = root["meshes"] as JsonArray ?? new JsonArray();
			for ( var m = 0; m < meshes.Count; m++ )
			{
				if ( meshes[m]?["primitives"] is not JsonArray primitives ) continue;
				for ( var p = 0; p < primitives.Count; p++ )
				{
					if ( primitives[p] is not JsonObject primitive ) continue;
					if ( primitive["attributes"] is JsonObject attributes )
						foreach ( var (semantic, value) in attributes )
							if ( Index( value ) is { } accessor ) Add( uses, accessor, new Use( m, p, semantic ) );
					if ( Index( primitive["indices"] ) is { } index ) indices.Add( index );
					// Morph target deltas must match the precision of their base attributes, so both keep their layout.
					if ( primitive["targets"] is JsonArray targets )
						foreach ( var target in targets.OfType<JsonObject>() )
							foreach ( var (semantic, value) in target )
							{
								if ( Index( value ) is { } delta ) locked.Add( delta );
								if ( Index( primitive["attributes"]?[semantic] ) is { } baseAccessor ) locked.Add( baseAccessor );
							}
				}
			}
			foreach ( var animation in (root["animations"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() )
				foreach ( var sampler in (animation["samplers"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() )
				{
					if ( Index( sampler["input"] ) is { } input ) locked.Add( input );
					if ( Index( sampler["output"] ) is { } output ) locked.Add( output );
				}
			foreach ( var skin in (root["skins"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() )
				if ( Index( skin["inverseBindMatrices"] ) is { } matrices ) locked.Add( matrices );
			foreach ( var node in (root["nodes"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() )
				if ( node["extensions"]?["EXT_mesh_gpu_instancing"]?["attributes"] is JsonObject instancing )
					foreach ( var (_, value) in instancing ) if ( Index( value ) is { } accessor ) locked.Add( accessor );
			foreach ( var index in indices ) if ( uses.ContainsKey( index ) ) locked.Add( index );
			foreach ( var index in uses.Keys ) if ( accessors[index]?["sparse"] is not null ) locked.Add( index );

			CompressPositions( meshes, uses, locked );
			foreach ( var (accessor, accessorUses) in uses )
				if ( !locked.Contains( accessor ) ) CompressAttribute( accessor, accessorUses, meshes );
			if ( settings.CompactIntegers )
				foreach ( var index in indices )
					if ( !locked.Contains( index ) ) CompactIndices( index );

			if ( needsQuantization )
			{
				Declare( "extensionsUsed" );
				Declare( "extensionsRequired" );
			}
		}

		static void Add( Dictionary<int, List<Use>> uses, int accessor, Use use )
		{
			if ( !uses.TryGetValue( accessor, out var list ) ) uses[accessor] = list = new List<Use>();
			list.Add( use );
		}

		static int? Index( JsonNode node ) => node is JsonValue value && value.TryGetValue<int>( out var index ) ? index : null;

		void Declare( string list )
		{
			if ( root[list] is not JsonArray names ) root[list] = names = new JsonArray();
			if ( !names.Any( x => x?.GetValue<string>() == Quantization ) ) names.Add( Quantization );
		}

		void Warn( string message )
		{
			if ( reported.Add( message ) ) warnings?.Add( message );
		}

		string MeshName( JsonArray meshes, int mesh ) => meshes[mesh]?["name"]?.GetValue<string>() is { Length: > 0 } name ? $"mesh '{name}'" : $"mesh {mesh}";

		/// <summary>
		/// Positions are stored as 16-bit integers around the centre of the mesh, with one scale for every axis so normals keep
		/// their direction. A new child node under each node of the mesh holds that offset and scale, so the model does not move.
		/// </summary>
		void CompressPositions( JsonArray meshes, Dictionary<int, List<Use>> uses, HashSet<int> locked )
		{
			var nodes = root["nodes"] as JsonArray ?? new JsonArray();
			var lodAlternates = new HashSet<int>();
			foreach ( var node in nodes.OfType<JsonObject>() )
				if ( node["extensions"]?["MSFT_lod"]?["ids"] is JsonArray ids )
					foreach ( var id in ids ) if ( Index( id ) is { } alternate ) lodAlternates.Add( alternate );

			for ( var m = 0; m < meshes.Count; m++ )
			{
				if ( meshes[m]?["primitives"] is not JsonArray primitives || primitives.Count == 0 ) continue;
				if ( Enumerable.Range( 0, primitives.Count ).All( p => settings.Get( m, p, GltfAttributeGroup.Position ) == GltfAttributeStorage.Source ) ) continue;
				string skip = null;
				if ( Enumerable.Range( 0, primitives.Count ).Any( p => settings.Get( m, p, GltfAttributeGroup.Position ) == GltfAttributeStorage.Source ) )
					skip = "some of its primitives keep source positions, and all primitives of a mesh share one node scale";
				var positions = primitives.Select( x => Index( x?["attributes"]?["POSITION"] ) ).ToList();
				if ( skip is null && positions.Any( x => x is null ) ) skip = "a primitive has no positions";
				var accessorSet = positions.Where( x => x is not null ).Select( x => x.Value ).Distinct().ToList();
				if ( skip is null && accessorSet.Any( locked.Contains ) ) skip = "it has morph targets, or its positions are shared with other data";
				if ( skip is null && accessorSet.Any( a => uses[a].Any( u => u.Mesh != m || u.Semantic != "POSITION" ) ) ) skip = "its positions are shared with another mesh";
				if ( skip is null && accessorSet.Any( a => accessors[a]?["componentType"]?.GetValue<int>() != Float ) ) skip = "its positions are already stored as integers";
				var owners = new List<int>();
				for ( var n = 0; n < nodes.Count; n++ ) if ( Index( nodes[n]?["mesh"] ) == m ) owners.Add( n );
				if ( skip is null && owners.Count == 0 ) skip = "no node uses it";
				if ( skip is null && owners.Any( n => nodes[n]?["skin"] is not null ) ) skip = "it is skinned, and a skinned mesh ignores its node transform";
				if ( skip is null && owners.Any( n => nodes[n]?["extensions"]?["EXT_mesh_gpu_instancing"] is not null ) ) skip = "it is instanced";
				if ( skip is null && owners.Any( n => nodes[n]?["extensions"]?["MSFT_lod"] is not null || lodAlternates.Contains( n ) ) ) skip = "it is part of an MSFT_lod chain";
				if ( skip is not null )
				{
					Warn( $"Positions of {MeshName( meshes, m )} stay as they are: {skip}." );
					continue;
				}

				var data = accessorSet.ToDictionary( x => x, document.ReadFloatAccessor );
				var min = new Vector3( float.MaxValue );
				var max = new Vector3( float.MinValue );
				foreach ( var values in data.Values )
					for ( var i = 0; i + 2 < values.Length; i += 3 )
					{
						var v = new Vector3( values[i], values[i + 1], values[i + 2] );
						min = Vector3.Min( min, v );
						max = Vector3.Max( max, v );
					}
				var center = (min + max) * 0.5f;
				var half = MathF.Max( MathF.Max( max.X - min.X, max.Y - min.Y ), max.Z - min.Z ) * 0.5f;
				var scale = half > 0 && float.IsFinite( half ) ? half / 32767f : 1f;
				foreach ( var (accessor, values) in data )
				{
					var count = values.Length / 3;
					var stride = VertexStride( Short, 3 );
					var bytes = new byte[count * stride];
					var qMin = new int[] { int.MaxValue, int.MaxValue, int.MaxValue };
					var qMax = new int[] { int.MinValue, int.MinValue, int.MinValue };
					for ( var i = 0; i < count; i++ )
						for ( var c = 0; c < 3; c++ )
						{
							var origin = c == 0 ? center.X : c == 1 ? center.Y : center.Z;
							var q = (int)Math.Clamp( MathF.Round( (values[i * 3 + c] - origin) / scale ), -32767, 32767 );
							BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( i * stride + c * 2 ), (short)q );
							qMin[c] = Math.Min( qMin[c], q );
							qMax[c] = Math.Max( qMax[c], q );
						}
					Replace( accessor, Short, false, stride, bytes, 34962 );
					accessors[accessor]!["min"] = new JsonArray( qMin.Select( x => (JsonNode)x ).ToArray() );
					accessors[accessor]!["max"] = new JsonArray( qMax.Select( x => (JsonNode)x ).ToArray() );
				}
				foreach ( var owner in owners )
				{
					var node = nodes[owner]!.AsObject();
					node.Remove( "mesh" );
					var name = node["name"]?.GetValue<string>();
					nodes.Add( new JsonObject
					{
						["name"] = string.IsNullOrEmpty( name ) ? "mesh" : $"{name} mesh",
						["mesh"] = m,
						["translation"] = new JsonArray( center.X, center.Y, center.Z ),
						["scale"] = new JsonArray( scale, scale, scale ),
					} );
					if ( node["children"] is not JsonArray children ) node["children"] = children = new JsonArray();
					children.Add( nodes.Count - 1 );
				}
				foreach ( var accessor in accessorSet ) locked.Add( accessor );
				needsQuantization = true;
			}
		}

		void CompressAttribute( int accessor, List<Use> uses, JsonArray meshes )
		{
			var groups = uses.Select( x => Group( x.Semantic ) ).Distinct().ToList();
			var json = accessors[accessor]!.AsObject();
			var componentType = json["componentType"]?.GetValue<int>() ?? 0;
			var components = Components( json["type"]?.GetValue<string>() );

			if ( settings.CompactIntegers && uses.All( x => x.Semantic.StartsWith( "JOINTS_", StringComparison.Ordinal ) ) && componentType == UnsignedShort )
			{
				var joints = document.ReadFloatAccessor( accessor );
				if ( joints.All( x => x < 256 ) )
				{
					var stride = VertexStride( UnsignedByte, components );
					var bytes = new byte[joints.Length / components * stride];
					for ( var i = 0; i < joints.Length; i++ ) bytes[i / components * stride + i % components] = (byte)joints[i];
					Replace( accessor, UnsignedByte, false, stride, bytes, 34962 );
				}
				return;
			}
			if ( groups.Count != 1 || groups[0] is not { } group || group == GltfAttributeGroup.Position ) return;
			// One accessor can serve several primitives. It keeps the most precise storage that any of them asks for.
			var bits = uses.Max( x => Bits( settings.Get( x.Mesh, x.Primitive, group ) ) );
			if ( bits == 32 || bits >= ComponentSize( componentType ) * 8 ) return;
			var size = bits / 8;
			var values = document.ReadFloatAccessor( accessor );
			var where = $"{uses[0].Semantic} of {MeshName( meshes, uses[0].Mesh )} primitive {uses[0].Primitive}";

			bool signed;
			switch ( group )
			{
				case GltfAttributeGroup.Normal or GltfAttributeGroup.Tangent:
					signed = true;
					needsQuantization = true;
					break;
				case GltfAttributeGroup.TexCoord:
					var low = values.Length > 0 ? values.Min() : 0;
					var high = values.Length > 0 ? values.Max() : 0;
					if ( low >= 0 && high <= 1 ) signed = false;
					else if ( low >= -1 && high <= 1 ) { signed = true; needsQuantization = true; }
					else
					{
						Warn( $"{where} stays as it is: its values ({low:0.###} to {high:0.###}) are outside -1 to 1." );
						return;
					}
					break;
				case GltfAttributeGroup.Weights:
					foreach ( var use in uses )
						if ( meshes[use.Mesh]?["primitives"]?[use.Primitive]?["attributes"] is JsonObject attributes && attributes.Count( x => x.Key.StartsWith( "WEIGHTS_", StringComparison.Ordinal ) ) > 1 )
						{
							Warn( $"{where} stays as it is: primitives with more than four influences per vertex keep their weights." );
							return;
						}
					signed = false;
					break;
				default:
					signed = false;
					break;
			}

			var type = size == 2 ? (signed ? Short : UnsignedShort) : (signed ? Byte : UnsignedByte);
			var limit = size == 2 ? (signed ? 32767 : 65535) : (signed ? 127 : 255);
			var quantized = group == GltfAttributeGroup.Weights ? QuantizeWeights( values, components, limit )
				: values.Select( x => (int)MathF.Round( Math.Clamp( x, signed ? -1f : 0f, 1f ) * limit ) ).ToArray();
			var elementStride = VertexStride( type, components );
			var data = new byte[quantized.Length / components * elementStride];
			for ( var i = 0; i < quantized.Length; i++ )
			{
				var at = i / components * elementStride + i % components * size;
				if ( size == 2 ) BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( at ), unchecked((ushort)quantized[i]) );
				else data[at] = unchecked((byte)quantized[i]);
			}
			Replace( accessor, type, true, elementStride, data, 34962 );
		}

		/// <summary>Weights of each vertex sum to exactly <paramref name="limit"/>, as the skinning upload requires.</summary>
		static int[] QuantizeWeights( float[] values, int components, int limit )
		{
			var result = new int[values.Length];
			var remainders = new (float Fraction, int Index)[components];
			for ( var v = 0; v + components <= values.Length; v += components )
			{
				var sum = 0f;
				for ( var c = 0; c < components; c++ ) sum += MathF.Max( values[v + c], 0 );
				if ( sum <= 0 ) continue;
				var total = 0;
				for ( var c = 0; c < components; c++ )
				{
					var exact = MathF.Max( values[v + c], 0 ) / sum * limit;
					result[v + c] = (int)MathF.Floor( exact );
					total += result[v + c];
					remainders[c] = (exact - result[v + c], c);
				}
				// Give the rounding error to the influences that lost the most. A zero weight stays zero, because its
				// joint index can repeat another one, and a repeated joint with a nonzero weight is not valid.
				var nonzero = remainders.Where( x => values[v + x.Index] > 0 ).OrderByDescending( x => x.Fraction ).Select( x => x.Index ).ToArray();
				for ( var i = 0; total < limit; i++, total++ ) result[v + nonzero[i % nonzero.Length]]++;
			}
			return result;
		}

		void CompactIndices( int accessor )
		{
			var json = accessors[accessor]!.AsObject();
			if ( json["componentType"]?.GetValue<int>() != UnsignedInt ) return;
			var values = document.ReadIndexAccessor( accessor );
			if ( values.Length == 0 || values.Max() >= 65535 ) return;
			var bytes = new byte[(values.Length * 2 + 3) & ~3];
			for ( var i = 0; i < values.Length; i++ ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( i * 2 ), (ushort)values[i] );
			Replace( accessor, UnsignedShort, false, 0, bytes, 34963 );
		}

		/// <summary>Point an accessor at new data in its own buffer view. The old view is removed when nothing else uses it.</summary>
		void Replace( int accessor, int componentType, bool normalized, int stride, byte[] data, int target )
		{
			var json = accessors[accessor]!.AsObject();
			var oldType = json["componentType"]!.GetValue<int>();
			var components = Components( json["type"]!.GetValue<string>() );
			var count = json["count"]!.GetValue<int>();
			SourceBytes += (long)count * (target == 34963 ? ComponentSize( oldType ) : VertexStride( oldType, components ));
			CompressedBytes += (long)count * (target == 34963 ? ComponentSize( componentType ) : stride);
			Rewritten++;

			var view = new JsonObject { ["buffer"] = 0, ["byteLength"] = data.Length, ["target"] = target };
			if ( stride > 0 ) view["byteStride"] = stride;
			views.Add( view );
			newViewData[views.Count - 1] = data;
			json["bufferView"] = views.Count - 1;
			json.Remove( "byteOffset" );
			json["componentType"] = componentType;
			if ( normalized ) json["normalized"] = true;
			else json.Remove( "normalized" );
			// Bounds are optional for every attribute except positions, which set their own.
			json.Remove( "min" );
			json.Remove( "max" );
		}

		/// <summary>Write the GLB with only the buffer views that something still uses.</summary>
		public byte[] Write()
		{
			root["bufferViews"] = views;
			var original = document.CompilerBuffer( 0 );
			var referenced = new SortedSet<int>();
			void Collect( JsonNode node )
			{
				if ( Index( node?["bufferView"] ) is { } view ) referenced.Add( view );
			}
			foreach ( var accessor in accessors.OfType<JsonObject>() )
			{
				Collect( accessor );
				Collect( accessor["sparse"]?["indices"] );
				Collect( accessor["sparse"]?["values"] );
			}
			foreach ( var image in (root["images"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() ) Collect( image );

			var remap = new Dictionary<int, int>();
			var kept = new JsonArray();
			using var binary = new MemoryStream();
			foreach ( var index in referenced )
			{
				var view = views[index]!.AsObject();
				var bytes = newViewData.TryGetValue( index, out var fresh ) ? fresh
					: original.Slice( view["byteOffset"]?.GetValue<int>() ?? 0, view["byteLength"]!.GetValue<int>() ).ToArray();
				while ( binary.Length % 4 != 0 ) binary.WriteByte( 0 );
				var copy = view.DeepClone().AsObject();
				copy["buffer"] = 0;
				copy["byteOffset"] = binary.Length;
				binary.Write( bytes );
				remap[index] = kept.Count;
				kept.Add( copy );
			}
			void Remap( JsonNode node )
			{
				if ( node is JsonObject json && Index( json["bufferView"] ) is { } view ) json["bufferView"] = remap[view];
			}
			foreach ( var accessor in accessors.OfType<JsonObject>() )
			{
				Remap( accessor );
				Remap( accessor["sparse"]?["indices"] );
				Remap( accessor["sparse"]?["values"] );
			}
			foreach ( var image in (root["images"] as JsonArray ?? new JsonArray()).OfType<JsonObject>() ) Remap( image );
			root["bufferViews"] = kept;
			while ( binary.Length % 4 != 0 ) binary.WriteByte( 0 );
			root["buffers"]![0]!["byteLength"] = binary.Length;
			return GltfCompiler.WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), binary.ToArray(), true );
		}
	}
}
