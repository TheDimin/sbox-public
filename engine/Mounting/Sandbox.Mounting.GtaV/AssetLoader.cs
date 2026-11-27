using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Mounting;
using Sandbox.ModelEditor.Nodes;

namespace GtaV;

internal sealed partial class AssetLoader( Entry entry ) : ResourceLoader<GtaMount>
{
	const int MissingDecalColor = 0x400;

	BinaryReader prepared;
	PreparedModelData? preparedModelData;
	WeakReference<Model> preparedModel;
	object retainedResource;
	LinkedListNode<(long Bytes, Action Evict)> retention;
	internal Model PreparedModel => retainedResource as Model ??
		(preparedModel is not null && preparedModel.TryGetTarget( out var model ) ? model : null);
	protected override bool UseWeakResourceCache => entry.Kind is Kind.Model or Kind.DictionaryModel or Kind.Collision or Kind.Texture;
	internal Entry SourceEntry => entry;
	internal BinaryReader ReadPacket()
	{
		using var scope = new StreamMeasurements.Stage( "packet_read" );
		return Host.Read( entry );
	}

	internal PreparedModelData ReadModel()
	{
		using var scope = new StreamMeasurements.Stage( "packet_read" );
		return Host.ReadModel( entry );
	}

	internal HashSet<AssetLoader> ThumbnailTextures { get; set; }
	WeakReference<Texture> fullTexture;
	internal bool TextureReady => fullTexture is not null && fullTexture.TryGetTarget( out var texture ) && texture.IsValid();

	WeakReference<byte[]> previewSource;
	int sourceOffset, sourceLength;

	internal System.Threading.Tasks.Task<BinaryReader> ReadPreviewSource()
	{
		if ( previewSource is not null && previewSource.TryGetTarget( out var bytes ) )
			return System.Threading.Tasks.Task.FromResult( new BinaryReader( new MemoryStream( bytes, sourceOffset, sourceLength, false, true ) ) );
		return null;
	}

	internal System.Threading.Tasks.Task<BinaryReader> ReadDetail() => ReadPreviewSource() ?? Host.Textures.Read( entry );

	WeakReference<Texture> previewTexture;
	Texture Preview => retainedResource as Texture is { } cached && !TextureReady ? cached :
		previewTexture is not null && previewTexture.TryGetTarget( out var texture ) ? texture : null;
	int resolution;
	internal int Resolution => resolution;
	internal bool HasStreamingTexture => TextureReady || Preview.IsValid();
	internal int NextStage( int target ) => Math.Min( target, resolution * 2 );
	internal bool NeedsDetail( int target ) => Preview.IsValid() && resolution < target;
	internal readonly HashSet<AssetLoader> PreviewDependencies = new();
	readonly List<(WeakReference<Material> Material, string Parameter)> bindings = new();

	internal void UploadStage( BinaryReader packet, int target )
	{
		if ( TextureReady || (Preview.IsValid() && resolution >= target) ) return;
		using var scope = new StreamMeasurements.Stage( "texture_stage" );
		var texture = TextureMipPreview.Create( packet, target, out var size, Path );
		if ( !texture.IsValid() ) { UploadTexture( packet ); return; }
		if ( packet.BaseStream is MemoryStream memory && memory.TryGetBuffer( out var segment ) )
		{
			previewSource = new( segment.Array );
			sourceOffset = segment.Offset;
			sourceLength = segment.Count;
		}
		previewTexture = new( texture );
		resolution = size;
		Retain( texture, (long)texture.Width * texture.Height * 6 );
		Publish( texture );
	}

	void Retain( object resource, long estimatedBytes )
	{
		var budget = Host.IdleBudget;
		if ( retention is not null ) budget.Remove( retention );
		retainedResource = resource;
		retention = budget.Add( estimatedBytes, () =>
		{
			retention = null;
			retainedResource = null;
		} );
		budget.Trim();
	}

	void Publish( Texture texture )
	{
		for ( int i = bindings.Count - 1; i >= 0; i-- )
		{
			var binding = bindings[i];
			if ( binding.Material.TryGetTarget( out var material ) && material.IsValid() )
				material.Set( binding.Parameter, texture );
			else bindings.RemoveAt( i );
		}
	}

	// Each model owns its refinement demand even when several models draw with the same material.
	void TrackSharedSurface( TextureBinding binding )
	{
		if ( binding.Path is null || Host.GetByPath( binding.Path ) is not AssetLoader asset || asset.TextureReady ) return;
		if ( asset.HasStreamingTexture ) PreviewDependencies.Add( asset );
		else ThumbnailTextures?.Add( asset );
	}

	void ApplySurface( TextureBinding binding, Material material, string parameter )
	{
		if ( binding.Path is not null && Host.GetByPath( binding.Path ) is AssetLoader asset && !asset.TextureReady )
		{
			if ( asset.Preview is not { IsValid: true } preview )
			{
				ThumbnailTextures?.Add( asset );
				Host.BindTexture( asset, material, parameter == "g_tColor", surface: true, parameter: parameter );
				return;
			}
			material.Set( parameter, preview );
			asset.bindings.Add( (new( material ), parameter) );
			PreviewDependencies.Add( asset );
			return;
		}
		binding.Apply( material, parameter, Path );
	}
	internal void UploadTexture( BinaryReader packet )
	{
		if ( TextureReady ) return;
		prepared = packet;
		try { Texture.Load( Path ); }
		finally { prepared = null; }
	}

	internal Queue<AssetLoader> ExternalTextures( PreparedModelData data, bool colorOnly )
	{
#if GTAV_BOREALIS_GEOMETRY
		if ( data.Drawable is { } drawable ) return ExternalTextures( drawable.Geometry, colorOnly );
#endif
		return ExternalTextures( data.Packet, colorOnly );
	}

	Queue<AssetLoader> ExternalTextures( BinaryReader packet, bool colorOnly )
	{
		var lookup = Host.TextureAssets;
		var result = new Queue<AssetLoader>();
		var seen = new HashSet<AssetLoader>();
		var stream = packet.BaseStream;
		var dictionaries = new string[packet.ReadInt32()];
		for ( int i = 0; i < dictionaries.Length; i++ ) dictionaries[i] = packet.Text();
		int vertices;
		while ( (vertices = packet.ReadInt32()) > 0 )
		{
			int flags = packet.ReadInt32();
			int bucket = flags & 7;
			if ( (flags >> 12) != 0 ) packet.ReadSingle();
			bool overlay = false;
			if ( bucket == 2 )
			{
				overlay = IsBuildingOverlay( packet.ReadUInt32() );
				stream.Position += 20; // Preset hash and four mask channels.
			}
			for ( int slot = 0; slot < 2; slot++ )
			{
				var name = packet.Text();
				int size = packet.ReadInt32();
				stream.Position += size;
				if ( size != 0 || overlay || (colorOnly && slot != 0) ) continue;
				foreach ( var dictionary in dictionaries )
				{
					if ( !lookup.TryGetValue( $"{dictionary}/{name}", out var asset ) ) continue;
					if ( !asset.HasStreamingTexture && seen.Add( asset ) ) result.Enqueue( asset );
					break;
				}
			}
			if ( (flags & 0x200) != 0 )
			{
				stream.Position += 5 * sizeof( float );
				var name = packet.Text();
				int size = packet.ReadInt32();
				stream.Position += size;
				if ( size == 0 && !colorOnly )
					foreach ( var dictionary in dictionaries )
					{
						if ( !lookup.TryGetValue( $"{dictionary}/{name}", out var asset ) ) continue;
						if ( !asset.HasStreamingTexture && seen.Add( asset ) ) result.Enqueue( asset );
						break;
					}
			}
			stream.Position += (long)vertices * (bucket == 2 ? 52 : 48);
			int indices = packet.ReadInt32();
			stream.Position += (long)indices * sizeof( ushort );
		}
		stream.Position = 0;
		return result;
	}

	WeakReference<Model> physics;
	bool emptyBounds;
	internal bool TryGetPhysics( out Model model )
	{
		model = null;
		if ( emptyBounds || (physics is not null && physics.TryGetTarget( out model ) && model.IsValid) )
			return true;

		// A mounted drawable builds its authored bounds from the same open resource as its render
		// mesh. Reuse that model when it already has a collision shape.
		if ( PreparedModel is { IsValid: true, Physics: { IsValid: true, Parts.Count: > 0 } } drawable )
		{
			model = drawable;
			return true;
		}
		return false;
	}

	internal BinaryReader ReadPhysics() => Host.ReadObjectCollision( entry );

	internal Model LoadPhysics( BinaryReader packet )
	{
		if ( TryGetPhysics( out var existing ) ) return existing;
		var builder = Model.Builder.WithName( entry.Name );
		if ( !ReadCollision( packet, builder ) )
		{
			emptyBounds = true;
			return null;
		}
		ReadObjectProperties( packet, builder );
		var model = builder.Create();
		physics = new( model );
		return model;
	}

	static bool ReadCollision( BinaryReader data, ModelBuilder builder )
	{
		int triangleCount = data.ReadInt32();
		for ( int hull = 0; hull < -triangleCount; hull++ )
		{
			var points = new Vector3[data.ReadInt32()];
			for ( int i = 0; i < points.Length; i++ )
				points[i] = data.Vector() * PacketReader.UnitsPerMetre;
			builder.AddCollisionHull( points.AsSpan() );
		}
		if ( triangleCount > 0 )
		{
			var positions = new Vector3[checked(triangleCount * 3)];
			var indices = new int[positions.Length];
			for ( int i = 0; i < positions.Length; i++ )
			{
				positions[i] = data.Vector() * PacketReader.UnitsPerMetre;
				indices[i] = i;
			}
			builder.AddCollisionMesh( positions.AsSpan(), indices.AsSpan() );
		}
		return triangleCount != 0;
	}

	internal Model LoadPrepared( PreparedModelData data )
	{
		using var scope = new StreamMeasurements.Stage( "model_create" );
		preparedModelData = data;
		try { return Model.Load( Path ); }
		finally { preparedModelData = null; }
	}

	protected override object Load()
	{
		if ( entry.Kind == Kind.Texture )
		{
			using var data = prepared ?? ReadPacket();
			var texture = StreamMeasurements.FromDds( data.ReadBytes( checked((int)data.BaseStream.Length) ), Path );
			if ( texture.IsValid() )
			{
				fullTexture = new( texture );
				Host.Textures.Release( (entry.File, entry.Member) );
				previewSource = null;
				Publish( texture );
				previewTexture = null;
				resolution = 0;
				Retain( texture, (long)texture.Width * texture.Height * 6 );
			}
			return texture;
		}
		if ( preparedModelData is { } model ) return BuildModel( model );
		using var read = ReadModel();
		return BuildModel( read );
	}

	Model BuildModel( PreparedModelData data )
	{
#if GTAV_BOREALIS_GEOMETRY
		if ( data.Drawable is { } drawable ) return BuildStoredModel( drawable );
#endif
		return BuildLegacyModel( data.Packet );
	}

	Model BuildLegacyModel( BinaryReader data )
	{
		var builder = Model.Builder.WithName( Path );
		List<(Material Material, MaterialKey Key)> paintSlots = entry.Kind == Kind.VehicleBody ? new() : null;
		if ( entry.Kind != Kind.Collision )
		{
			var dictionaries = new string[data.ReadInt32()];
			for ( int i = 0; i < dictionaries.Length; i++ )
				dictionaries[i] = data.Text();

			var materials = new Dictionary<MaterialKey, Material>();
			var overlayVertices = new List<Vertex>();
			var overlayIndices = new List<int>();
			Material overlayMaterial = null;
			int section = 0, firstOverlaySection = 0;
			int meshLod = entry.Kind is Kind.Model or Kind.DictionaryModel ? 0 : -1;
			string overlayDescription = null;
			void FlushOverlay()
			{
				if ( overlayVertices.Count == 0 ) return;
				string range = section == firstOverlaySection + 1 ? $"{firstOverlaySection}" : $"{firstOverlaySection}�{section - 1}";
				AddMesh( builder, overlayMaterial, overlayVertices.ToArray(), overlayIndices.ToArray(), false,
					$"GTA decal {range} | {overlayDescription}", meshLod );
				overlayVertices.Clear();
				overlayIndices.Clear();
			}

			int count;
			while ( (count = data.ReadInt32()) > 0 )
			{
				int sectionFlags = data.ReadInt32();
				int lod = sectionFlags >> 12;
				sectionFlags &= 0xfff;
				if ( lod > 0 )
				{
					float distance = data.ReadSingle();
					if ( lod != meshLod )
					{
						FlushOverlay();
						if ( float.IsFinite( distance ) && distance > 0 )
							builder.WithLodDistance( lod, distance * PacketReader.UnitsPerMetre );
						meshLod = lod;
					}
				}
				int bucket = sectionFlags & 7;
				uint effect = 0, preset = 0;
				Vector4 mask = default;
				if ( bucket == 2 )
				{
					effect = data.ReadUInt32();
					preset = data.ReadUInt32();
					mask = new Vector4( data.Vector(), data.ReadSingle() );
				}
				var color = ReadBinding( data, dictionaries, out string diffuseName );
				var normalMap = ReadBinding( data, dictionaries, out _ );
				float specFalloff = 0;
				Vector4 specResponse = default;
				TextureBinding specular = default;
				if ( (sectionFlags & 0x200) != 0 )
				{
					specFalloff = data.ReadSingle();
					specResponse = new Vector4( data.Vector(), data.ReadSingle() );
					specular = ReadBinding( data, dictionaries, out _ );
				}
				bool overlay = bucket == 2 && IsBuildingOverlay( effect );
				var key = new MaterialKey( sectionFlags, effect, preset, mask, color, normalMap, specular, specFalloff, specResponse );
				var material = GetMaterial( key, overlay, materials, paintSlots );
				var vertices = ReadVertices( data, count, bucket == 2, overlay );
				var indices = new int[data.ReadInt32()];
				var sourceIndices = MemoryMarshal.Cast<byte, ushort>( ReadBlock( data, checked(indices.Length * 2) ) );
				for ( int i = 0; i < indices.Length; i++ ) indices[i] = sourceIndices[i];
				if ( !overlay || material != overlayMaterial ) FlushOverlay();
				if ( overlay )
				{
					if ( overlayVertices.Count == 0 )
					{
						firstOverlaySection = section;
						overlayDescription = $"{OverlayName( effect )} | {diffuseName}";
					}
					overlayMaterial = material;
					int offset = overlayVertices.Count;
					overlayVertices.AddRange( vertices );
					foreach ( int index in indices ) overlayIndices.Add( offset + index );
				}
				else AddMesh( builder, material, vertices, indices, meshLod <= 0, lod: meshLod );
				section++;
			}
			FlushOverlay();
		}
		return FinishModel( builder, data, paintSlots, data.BaseStream.Length );
	}

	Material GetMaterial( MaterialKey key, bool overlay, Dictionary<MaterialKey, Material> materials, List<(Material Material, MaterialKey Key)> paintSlots )
	{
		var (sectionFlags, effect, preset, mask, color, normalMap, specular, specFalloff, specResponse) = key;
		int bucket = sectionFlags & 7;
		// Embedded textures belong to the drawable. Keep their bytes out of the mount cache.
		bool shared = entry.Kind is (Kind.Model or Kind.DictionaryModel) && color.Embedded is not { Length: > 0 } && normalMap.Embedded is not { Length: > 0 } && specular.Embedded is not { Length: > 0 };
		bool local = materials.TryGetValue( key, out var material );
		if ( !local && shared && Host.SharedMaterials.TryGetValue( key, out var weak ) && weak.TryGetTarget( out material ) && material.IsValid() )
		{
			materials[key] = material;
			if ( !overlay )
			{
				TrackSharedSurface( color );
				TrackSharedSurface( normalMap );
				TrackSharedSurface( specular );
			}
		}
		else if ( !local )
		{
			if ( overlay && effect == 0xe05f1a42 )
			{
				material = Material.Load( "materials/default/white.vmat" ).CreateCopy( $"gtav/decal/{preset:x8}" );
				material.SetFeature( "F_ALPHA_TEST", 1 );
				material.Set( "g_vColorTint", new Vector4( mask.x, mask.y, mask.z, 1 ) );
				material.Set( "g_tColor", Texture.Transparent );
				if ( color.Embedded is { Length: > 0 } )
					Host.BindLettering( material, StreamMeasurements.FromDds( color.Embedded, Path ) );
				else if ( color.Path is not null && Host.GetByPath( color.Path ) is AssetLoader atlas )
				{
					if ( atlas.TextureReady ) Host.BindLettering( material, Texture.Load( atlas.Path ) );
					else Host.BindTexture( atlas, material, true, lettering: true );
				}
			}
			else if ( overlay )
			{
				// Cache the raw shader defaults. Each draw still owns its texture and parameter bindings.
				material = Material.FromShader( "shaders/gtav_building_decal.shader" ).CreateCopy( $"gtav/decal/{preset:x8}" );
				material.SetFeature( "F_TRANSLUCENT", 1 );
				material.Set( "g_vDecalMask", mask );
				material.Set( "g_flDecalMode", effect switch { 0xd94d6305 => 1f, 0xae556d0e => 2f, _ => 0f } );
				material.Set( "g_flHasNormal", 0f );
				material.Set( "g_flColorReady", 0f );
				if ( (sectionFlags & MissingDecalColor) != 0 ) material.Set( "g_flUseDecalVertexTint", 0f );
				ApplyOverlay( color, material, true );
				ApplyOverlay( normalMap, material, false );
			}
			else if ( entry.Kind == Kind.VehicleBody && (sectionFlags & ManagedReader.ModelRenderPacket.VehiclePaintFlag) != 0 )
			{
				material = Material.FromShader( "shaders/gtav_vehicle_paint.shader" ).CreateCopy( $"gtav/vehicle/{entry.Name}_{materials.Count}.vmat" );
				material.Set( "g_flSpecFalloff", specFalloff );
				material.Set( "g_vSpecResponse", specResponse );
				ApplySurface( color, material, "g_tColor" );
				ApplySurface( specular, material, "g_tSpecular" );
			}
			else if ( (sectionFlags & 0x200) != 0 )
			{
				material = Material.FromShader( "shaders/gtav_world.shader" ).CreateCopy( $"gtav/world/{entry.Name}_{materials.Count}" );
				material.Set( "g_flSpecFalloff", float.IsFinite( specFalloff ) ? Math.Clamp( specFalloff, 0f, 2048f ) : 100f );
				ApplySurface( color, material, "g_tColor" );
				ApplySurface( normalMap, material, "g_tNormal" );
				material.Set( "g_vSpecResponse", specResponse );
				ApplySurface( specular, material, "g_tSpecular" );
			}
			else
			{
				material = Material.Load( "materials/default/white.vmat" ).CreateCopy( $"gtav/materials/{entry.Name}_{materials.Count}.vmat" );
				if ( bucket == 3 ) material.SetFeature( "F_ALPHA_TEST", 1 );
				if ( bucket is 1 or 2 ) material.SetFeature( "F_TRANSLUCENT", 1 );
				// The alpha mask comes from the diffuse texture. A white default fills
				// the entire polygon while that texture is in the streaming queue.
				if ( bucket is 1 or 2 or 3 && color.Path is not null )
					material.Set( "g_tColor", Texture.Transparent );
				ApplySurface( color, material, "g_tColor" );
				ApplySurface( normalMap, material, "g_tNormal" );
			}
			if ( shared ) Host.SharedMaterials[key] = new( material );
			materials[key] = material;
			paintSlots?.Add( (material, key) );
		}
		return material;
	}

	Model FinishModel( ModelBuilder builder, BinaryReader data, List<(Material Material, MaterialKey Key)> paintSlots, long retainedBytes )
	{
		ReadCollision( data, builder );
		if ( paintSlots is not null && data.BaseStream.Position < data.BaseStream.Length )
			AddVehiclePaints( builder, data, paintSlots );
		if ( entry.Kind == Kind.Fragment )
		{
			var count = data.ReadInt32();
			var pieces = new System.Collections.Generic.List<ModelBreakPiece>( count );
			for ( int i = 0; i < count; i++ )
			{
				uint id = data.ReadUInt32();
				var piece = new ModelBreakPiece
				{
					PieceName = data.Text(),
					Model = $"mount://{Host.Ident}/models/{entry.Name}/gibs/{id}.vmdl",
					CollisionTags = "",
					RandomSpawnChance = 1f,
					IsEssential = true
				};
				if ( Host.GetByPath( piece.Model ) is null ) continue;
				pieces.Add( piece );
			}
			if ( pieces.Count > 0 ) builder.WithData( pieces.ToArray() );
		}
		if ( entry.Kind is Kind.Fragment or Kind.Model or Kind.DictionaryModel )
			ReadObjectProperties( data, builder );
		else if ( entry.Kind == Kind.FragmentGroup )
			ApplyMass( builder, data.ReadSingle() );
		var model = builder.Create();
		preparedModel = new( model );
		if ( UseWeakResourceCache ) Retain( model, retainedBytes );
		return model;
	}

	void AddVehiclePaints( ModelBuilder builder, BinaryReader data, List<(Material Material, MaterialKey Key)> slots )
	{
		int count = data.ReadInt32();
		static float Linear( float s ) => s <= 0.04045f ? s / 12.92f : MathF.Pow( (s + 0.055f) / 1.055f, 2.4f );
		if ( count == 0 || !slots.Any( x => (x.Key.Flags & 0x100) != 0 ) ) return;
		var original = builder.AddMaterialGroup( "Original" );
		foreach ( var slot in slots ) original.AddMaterial( slot.Material );

		for ( int i = 0; i < count; i++ )
		{
			string finish = data.Text(), label = data.Text();
			uint rgb = data.ReadUInt32();
			var text = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
			var group = builder.AddMaterialGroup( text.ToTitleCase( finish + " / " + label.Replace( '_', ' ' ).ToLowerInvariant() ) );
			for ( int j = 0; j < slots.Count; j++ )
			{
				var (material, key) = slots[j];
				if ( (key.Flags & 0x100) != 0 )
				{
					material = material.CreateCopy( $"gtav/paint/{entry.Name}/{i}_{j}.vmat" );
					BindPendingPaintTexture( key.Color, material, "g_tColor" );
					BindPendingPaintTexture( key.Normal, material, "g_tNormal" );
					BindPendingPaintTexture( key.Specular, material, "g_tSpecular" );
					var color = Color.FromBytes( (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb );
					material.Set( "g_vColorTint", new Vector4( Linear( color.r ), Linear( color.g ), Linear( color.b ), 1 ) );
					material.Set( "g_flMetalness", finish switch { "chrome" => 1f, "metals" => 0.9f, "metallic" => 0.55f, _ => 0f } );
					material.Set( "g_flRoughnessScaleFactor", finish switch { "chrome" => 2f, "matte" => 0.35f, _ => 1f } );
				}
				group.AddMaterial( material );
			}
		}
	}

	// A material copy keeps its current textures. Register it for any remaining updates.
	void BindPendingPaintTexture( TextureBinding binding, Material material, string parameter )
	{
		if ( binding.Path is not null && Host.GetByPath( binding.Path ) is AssetLoader asset && !asset.TextureReady )
			ApplySurface( binding, material, parameter );
	}

	static void ApplyMass( ModelBuilder builder, float mass )
	{
		if ( float.IsFinite( mass ) && mass >= 0 ) builder.WithMass( mass );
	}

	void ReadObjectProperties( BinaryReader data, ModelBuilder builder )
	{
		float mass = data.ReadSingle();
		bool chair = ChairArchetypes.IsChair( entry.Name );
		ApplyMass( builder, chair && float.IsFinite( mass ) ? Math.Max( mass, ChairArchetypes.MinimumMass ) : mass );
		bool isObject = data.ReadBoolean();
		if ( !isObject && entry.Kind != Kind.Fragment ) return;
		var name = System.IO.Path.GetFileName( entry.Name );
		var archetype = Host.GetByPath( $"mount://{Host.Ident}/archetypes/{name}.prefab" ) as ArchetypeLoader;
		var extensions = archetype?.Extensions ?? [];
		bool explosive = entry.Kind == Kind.Fragment && extensions.Contains( DestroyExplosion.Instance );
		if ( isObject || explosive )
		{
			var material = data.BaseStream.Position < data.BaseStream.Length ? data.Text() : "";
			builder.WithData( new ModelPropData { Explosive = explosive, Health = ArchetypePhysics.ObjectHealth,
				DestructionSurface = DestructionSurface( material ),
				DestructionProfile = $"effects/gtav/props/{name}.propfx",
				DestructionCues = DestructionMappings.Resolve( extensions ),
				ImpactDamage = chair && mass > 0 ? mass / 10f : -1f } );
		}
	}

	static string DestructionSurface( string material )
	{
		var name = material.Split( '_', 2 )[0].ToLowerInvariant() switch
		{
			"glass" or "perspex" => "glass",
			"metal" or "chainlink" => "metal",
			"wood" or "tree" => "wood",
			"plastic" => "plastic",
			"rubber" => "rubber",
			"fabric" or "cloth" or "carpet" or "leather" => "fabric",
			"cardboard" or "paper" => "cardboard",
			"ceramic" or "porcelain" => "ceramic",
			"dirt" or "mud" or "sand" or "gravel" => "dirt",
			_ => "default"
		};
		return $"surfaces/{name}.surface";
	}

	// These drawable effects use colour overlays. Channel-only decals need a different render pass.
	bool IsBuildingOverlay( uint effect ) => entry.Kind is (Kind.Model or Kind.DictionaryModel) && OverlayName( effect ) is not null;

	static string OverlayName( uint effect ) => effect switch
	{
		0xe05f1a42 => "distance_map",
		0xf08729d4 => "decal",
		0x37c6e600 => "decal_glue",
		0x1510ebe7 => "normal_decal",
		0x1c1c2570 => "normal_spec_decal",
		0xd94d6305 => "decal_dirt",
		0xae556d0e => "decal_amb_only",
		_ => null
	};
	static void AddMesh( ModelBuilder builder, Material material, Vertex[] vertices, int[] indices, bool trace, string name = "mesh", int lod = -1 )
	{
		var positions = vertices.Select( v => v.Position ).ToArray();
		if ( trace ) builder.AddTraceMesh( positions.AsSpan(), indices.AsSpan() );
		var mesh = new Mesh( name, material ) { Bounds = BBox.FromPoints( positions ) };
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		if ( lod < 0 ) builder.AddMesh( mesh );
		else builder.AddMesh( mesh, lod );
	}

	internal readonly record struct MaterialKey( int Flags, uint Effect, uint Preset, Vector4 Mask, TextureBinding Color, TextureBinding Normal, TextureBinding Specular, float SpecFalloff, Vector4 SpecResponse );

	void ApplyOverlay( TextureBinding binding, Material material, bool color )
	{
		string parameter = color ? "g_tColor" : "g_tNormal";
		if ( binding.Path is not null && Host.GetByPath( binding.Path ) is AssetLoader asset && !asset.TextureReady )
		{
			Host.BindTexture( asset, material, color );
			return;
		}
		binding.Apply( material, parameter, Path );
		if ( binding.Path is not null || binding.Embedded is { Length: > 0 } )
			material.Set( color ? "g_flColorReady" : "g_flHasNormal", 1f );
	}

	internal readonly record struct TextureBinding( string Path, byte[] Embedded )
	{
		public bool Equals( TextureBinding other ) =>
			Path == other.Path && Embedded.AsSpan().SequenceEqual( other.Embedded );

		public override int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add( Path );
			hash.AddBytes( Embedded );
			return hash.ToHashCode();
		}

		internal void Apply( Material material, string parameter, string ownerPath )
		{
			if ( Embedded is { Length: > 0 } ) material.Set( parameter, StreamMeasurements.FromDds( Embedded, ownerPath ) );
			else if ( Path is not null ) material.Set( parameter, Texture.Load( Path ) );
		}
	}

	// Native mesh packets contain tightly packed, little-endian floats and 16-bit indices.
	// Advance the reader once per block, including when the stream is a slice of a larger packet.
	static ReadOnlySpan<byte> ReadBlock( BinaryReader data, int size )
	{
		if ( data.BaseStream is MemoryStream stream && stream.TryGetBuffer( out var buffer ) )
		{
			int start = checked((int)stream.Position);
			if ( size > stream.Length - start ) throw new EndOfStreamException();
			var block = buffer.AsSpan( start, size );
			stream.Position += size;
			return block;
		}
		var bytes = data.ReadBytes( size );
		if ( bytes.Length != size ) throw new EndOfStreamException();
		return bytes;
	}

	static Vertex[] ReadVertices( BinaryReader data, int count, bool hasTint, bool applyTint )
	{
		int stride = hasTint ? 52 : 48;
		var block = ReadBlock( data, checked(count * stride) );
		var vertices = new Vertex[count];
		for ( int i = 0; i < vertices.Length; i++ )
		{
			var record = block.Slice( i * stride, stride );
			var f = MemoryMarshal.Cast<byte, float>( record );
			var normal = new Vector3( f[3], f[4], f[5] ).Normal;
			var tangent = new Vector3( f[8], f[9], f[10] );
			if ( tangent.LengthSquared < 0.000001f )
				tangent = Vector3.Cross( MathF.Abs( normal.z ) < 0.9f ? Vector3.Up : Vector3.Forward, normal );
			vertices[i] = new Vertex( new Vector3( f[0], f[1], f[2] ) * PacketReader.UnitsPerMetre )
			{
				Normal = normal,
				TexCoord0 = new Vector4( f[6], f[7], 0, 0 ),
				Tangent = new Vector4( tangent.Normal, f[11] < 0 ? -1 : 1 )
			};
			if ( applyTint ) vertices[i].Color = new Color32( MemoryMarshal.Read<uint>( record[48..] ) );
		}
		return vertices;
	}

	TextureBinding ReadBinding( BinaryReader data, string[] dictionaries, out string name )
	{
		name = data.Text();
		var embedded = data.ReadBytes( data.ReadInt32() );
		if ( embedded.Length > 0 ) return new( null, embedded );

		foreach ( var dictionary in dictionaries )
		{
			if ( !Host.TextureAssets.TryGetValue( $"{dictionary}/{name}", out var texture ) ) continue;
			return new( texture.Path, null );
		}
		return default;
	}
}
