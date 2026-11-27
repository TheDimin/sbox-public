using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

/// <summary>Owns a drawable resource and keeps its render buffers in their source formats.</summary>
internal sealed partial class DrawableAsset : IDisposable
{
	readonly RscResource resource;
	Action release;
	readonly bool enhanced;
	readonly List<Section> sections = new();
	List<NumericsMatrix> transforms;

	internal readonly record struct Section( DrawableGeometry Geometry, Material Material, int Lod, float Distance,
		NumericsVector3 Min, NumericsVector3 Max, int Transform );
	internal readonly record struct Material( uint Name, uint Preset, int Bucket, Binding Diffuse, Binding Normal, Binding Specular,
		NumericsVector4 DecalMask, NumericsVector4 SpecularResponse, float SpecularFalloff, bool PrimaryPaint = false, bool VehiclePaint = false );
	readonly record struct DrawOptions( bool AllLods, NumericsMatrix Placement, ulong Parent = 0, int Bone = -1, bool UseBonePose = false, bool VehicleBody = false );
	internal readonly record struct Binding( string Name, EmbeddedTexture Embedded );
	internal readonly record struct EmbeddedTexture( TextureDds Header, RscResource Resource, ulong Address, int PayloadLength )
	{
		internal int Length => Resource is null ? 0 : Header.HeaderSize + PayloadLength;
		internal void CopyTo( Span<byte> destination )
		{
			Header.WriteHeader( destination );
			if ( PayloadLength != 0 ) Resource.At( Address, PayloadLength ).CopyTo( destination[Header.HeaderSize..] );
		}

		internal void Write( BinaryWriter writer )
		{
			writer.Write( Length );
			if ( Resource is null ) return;
			Span<byte> header = stackalloc byte[Header.HeaderSize];
			Header.WriteHeader( header );
			writer.Write( header );
			if ( PayloadLength != 0 ) writer.Write( Resource.At( Address, PayloadLength ) );
		}
	}

	static readonly uint NormalSpec = ArchiveIndex.HashName( "normal_spec" );
	const uint VehiclePaint1 = 0xF9FB7331;
	static readonly uint DiffuseSampler = ArchiveIndex.HashName( "DiffuseSampler" );
	static readonly uint DistanceMapSampler = ArchiveIndex.HashName( "distanceMapSampler" );
	static readonly uint DiffuseLayer = ArchiveIndex.HashName( "TextureSampler_layer0" );
	static readonly uint BumpSampler = ArchiveIndex.HashName( "BumpSampler" );
	static readonly uint BumpLayer = ArchiveIndex.HashName( "BumpSampler_layer0" );
	static readonly uint SpecularSampler = ArchiveIndex.HashName( "SpecSampler" );
	static readonly uint Falloff = ArchiveIndex.HashName( "specularFalloffMult" );
	static readonly uint SpecularMask = ArchiveIndex.HashName( "specMapIntMask" );
	static readonly uint SpecularIntensity = ArchiveIndex.HashName( "specularIntensityMult" );
	static readonly uint DirtDecalMask = ArchiveIndex.HashName( "DirtDecalMask" );
	static readonly uint AmbientDecalMask = ArchiveIndex.HashName( "AmbientDecalMask" );
	static readonly uint FillColour = ArchiveIndex.HashName( "fillColor" );
	static readonly uint MaterialDiffuse = ArchiveIndex.HashName( "matDiffuseColor" );

	internal IReadOnlyList<Section> Sections => sections;
	internal RscResource Resource => resource;
	internal string[] TextureDictionaries { get; }
	internal ulong BoundsPointer { get; }
	internal NumericsMatrix TransformAt( int id ) => id == 0 ? NumericsMatrix.Identity : transforms[id - 1];

	internal DrawableAsset( ArchiveIndex index, ArchiveContent content, in ArchiveIndex.FileEntry file, uint member, Kind kind = Kind.Fragment,
		RscResource shared = null, Action releaseShared = null )
	{
		bool dictionary = file.Extension == ".ydd";
		bool fragment = file.Extension == ".yft";
		if ( !dictionary && !fragment && file.Extension != ".ydr" ) throw new ArgumentException( "Expected a drawable resource.", nameof(file) );

		uint textureHash = index.FindArchetype( dictionary ? member : file.Hash )?.Data.Texture ?? file.Hash;
		Span<uint> parents = stackalloc uint[16];
		int count = index.FindTextureParents( textureHash, parents );
		TextureDictionaries = new string[count + 1];
		TextureDictionaries[0] = index.NameOf( textureHash );
		for ( int i = 0; i < count; i++ ) TextureDictionaries[i + 1] = index.NameOf( parents[i] );

		resource = shared ?? new RscResource( content, file );
		release = releaseShared ?? resource.Dispose;
		try
		{
			enhanced = fragment ? resource.Version == 171 : resource.Version is 154 or 159;
			if ( fragment )
			{
				BoundsPointer = ReadFragmentSections( kind, member );
				return;
			}
			ulong root = dictionary ? FindMember( resource, member ) ?? throw new InvalidDataException( "Drawable dictionary member is missing." )
				: RscResource.SystemBase;
			if ( root == 0 ) return;
			BoundsPointer = Pointer( resource.At( root + 0xC8, 8 ), 0 );
			ReadSections( root, new DrawOptions( true, NumericsMatrix.Identity ) );
		}
		catch
		{
			if ( shared is null ) Dispose();
			throw;
		}
	}

	internal static ulong? FindMember( RscResource resource, uint member )
	{
		var dictionary = resource.At( RscResource.SystemBase, 0x3A );
		var hashes = resource.Array( Pointer( dictionary, 0x20 ), Short( dictionary, 0x28 ), sizeof(uint) );
		var pointers = resource.Array( Pointer( dictionary, 0x30 ), Short( dictionary, 0x38 ), sizeof(ulong) );
		if ( hashes.Length / sizeof(uint) != pointers.Length / sizeof(ulong) )
			throw new InvalidDataException( "Drawable dictionary member count mismatch." );
		for ( int i = 0; i < hashes.Length / sizeof(uint); i++ )
			if ( Word( hashes, i * sizeof(uint) ) == member ) return Pointer( pointers, i * sizeof(ulong) );
		return null;
	}

	void ReadSections( ulong root, in DrawOptions options )
	{
		if ( root == 0 ) return;
		var drawable = resource.At( root, 0x80 );
		ulong groupPointer = Pointer( drawable, 0x10 );
		if ( groupPointer == 0 && options.Parent != 0 )
			groupPointer = Pointer( resource.At( options.Parent + 0x10, 8 ), 0 );
		var group = groupPointer == 0 ? [] : resource.At( groupPointer, 0x1A );
		ulong texturePointer = group.IsEmpty ? 0 : Pointer( group, 8 );
		var shaders = group.IsEmpty ? [] : resource.Array( Pointer( group, 0x10 ), Short( group, 0x18 ), sizeof(ulong) );
		var materials = shaders.IsEmpty ? [] : new Material?[shaders.Length / sizeof(ulong)];
		Dictionary<uint, ulong> embedded = null;
		var drawMin = Vector( drawable, 0x30 );
		var drawMax = Vector( drawable, 0x40 );
		var pose = options.UseBonePose ? new FragmentPose( resource, root ) : default;
		int lod = 0;
		float distance = 0;
		for ( int source = 0; source < 4; source++ )
		{
			ulong pointer = Pointer( drawable, 0x50 + 8 * source );
			if ( pointer == 0 ) continue;
			int firstSection = sections.Count;
			var list = resource.At( pointer, 10 );
			var models = resource.Array( Pointer( list, 0 ), Short( list, 8 ), sizeof(ulong) );
			for ( int model = 0; model < models.Length; model += sizeof(ulong) )
			{
				ulong modelPointer = Pointer( models, model );
				if ( modelPointer == 0 ) continue;
				var row = resource.At( modelPointer, 0x2E );
				int bone = row[0x2B];
				if ( (row[0x2C] & 1) == 0 || (options.Bone >= 0 && options.Bone != bone) ) continue;
				int count = Short( row, 0x10 );
				var geometries = resource.Array( Pointer( row, 8 ), count, sizeof(ulong) );
				var shaderIndices = resource.Array( Pointer( row, 0x20 ), count, sizeof(ushort) );
				var bounds = resource.Array( Pointer( row, 0x18 ), count > 1 ? count + 1 : count, 32 );
				int transform = -1;
				for ( int g = 0; g < geometries.Length / sizeof(ulong); g++ )
				{
					ulong geometryPointer = Pointer( geometries, g * sizeof(ulong) );
					if ( geometryPointer == 0 ) continue;
					var geometry = new DrawableGeometry( resource, geometryPointer, enhanced );
					if ( geometry.IsEmpty ) continue;
					int shader = shaderIndices.IsEmpty ? 0 : Short( shaderIndices, g * sizeof(ushort) );
					var material = shader < materials.Length ?
						materials[shader] ??= ReadMaterial( Pointer( shaders, shader * sizeof(ulong) ), texturePointer, options, ref embedded )
						: default;
					if ( material.Name is 0x21ABE462 or 0xD5738C1B || material.Preset is 0x711FEB4A or 0xA0DDA4ED ) continue;
					int box = count > 1 ? (g + 1) * 32 : 0;
					var min = bounds.IsEmpty ? drawMin : Vector( bounds, box );
					var max = bounds.IsEmpty ? drawMax : Vector( bounds, box + 16 );
					if ( transform < 0 )
					{
						var placement = options.UseBonePose && row[0x29] == 0 ? pose.Placement( bone, options.Placement ) : options.Placement;
						transform = RegisterTransform( placement );
					}
					sections.Add( new Section( geometry, material, lod, distance, min, max, transform ) );
				}
			}
			if ( sections.Count == firstSection ) continue;
			if ( !options.AllLods ) break;
			distance = Float( drawable, 0x70 + 4 * source );
			lod++;
		}
	}

	int RegisterTransform( in NumericsMatrix placement )
	{
		if ( placement.IsIdentity ) return 0;
		(transforms ??= new()).Add( placement );
		return transforms.Count;
	}

	Material ReadMaterial( ulong pointer, ulong textureDictionary, in DrawOptions options, ref Dictionary<uint, ulong> embedded )
	{
		var shader = new DrawableShader( resource, pointer, enhanced );
		if ( shader.Name is 0x21ABE462 or 0xD5738C1B || shader.Preset is 0x711FEB4A or 0xA0DDA4ED )
			return new Material( shader.Name, shader.Preset, 0, default, default, default, default, default, 0 );

		const uint distanceMap = 0xE05F1A42;
		string diffuse = shader.Texture( shader.Name == distanceMap ? DistanceMapSampler : DiffuseSampler );
		if ( diffuse.Length == 0 ) diffuse = shader.Texture( DiffuseLayer );
		string normal = shader.Texture( BumpSampler );
		if ( normal.Length == 0 ) normal = shader.Texture( BumpLayer );
		int bucket = shader.Bucket & 7;
		var mask = bucket != 2 ? default : shader.Name == distanceMap
			? shader.Constant( FillColour ) ?? new NumericsVector4( 0, 0, 1, 1 )
			: shader.Constant( DirtDecalMask ) ?? shader.Constant( AmbientDecalMask ) ?? new NumericsVector4( 1, 0, 0, 0 );
		bool vehiclePaint = options.VehicleBody && shader.Name == VehiclePaint1;
		string specular = vehiclePaint || (options.AllLods && bucket == 0 && shader.Name == NormalSpec)
			? shader.Texture( SpecularSampler )
			: string.Empty;
		float falloff = 0;
		NumericsVector4 response = default;
		if ( specular.Length != 0 || vehiclePaint )
		{
			falloff = shader.Constant( Falloff )?.X ?? (vehiclePaint ? 180f : 100f);
			response = shader.Constant( SpecularMask ) ?? new NumericsVector4( 1, 0, 0, 0 );
			response.W = shader.Constant( SpecularIntensity )?.X ?? (vehiclePaint ? 0.15f : 0.125f);
		}
		bool primary = options.VehicleBody && shader.Constant( MaterialDiffuse ) is { X: 2f, Y: 1f, Z: 1f };
		return new Material( shader.Name, shader.Preset, bucket, Bind( diffuse, textureDictionary, ref embedded ),
			Bind( normal, textureDictionary, ref embedded ), Bind( specular, textureDictionary, ref embedded ), mask, response, falloff, primary, vehiclePaint );
	}

	Binding Bind( string name, ulong pointer, ref Dictionary<uint, ulong> embedded )
	{
		if ( pointer == 0 ) return new Binding( name, default );
		embedded ??= ReadEmbeddedIndex( pointer );
		if ( !embedded.TryGetValue( ArchiveIndex.HashName( name ), out ulong texture ) ) return new Binding( name, default );
		var row = resource.At( texture, TextureDds.DescriptorSize( enhanced ) );
		var dds = TextureDds.Describe( row, enhanced, out ulong address, out ulong requested );
		int length = address == 0 ? 0 : (int)Math.Min( requested, (ulong)resource.Remaining( address ).Length );
		return new Binding( name, new EmbeddedTexture( dds, resource, address, length ) );
	}

	Dictionary<uint, ulong> ReadEmbeddedIndex( ulong pointer )
	{
		var dictionary = resource.At( pointer, 0x3A );
		var hashes = resource.Array( Pointer( dictionary, 0x20 ), Short( dictionary, 0x28 ), sizeof(uint) );
		var pointers = resource.Array( Pointer( dictionary, 0x30 ), Short( dictionary, 0x38 ), sizeof(ulong) );
		var textures = new Dictionary<uint, ulong>( pointers.Length / sizeof(ulong) );
		for ( int i = 0; i < pointers.Length / sizeof(ulong); i++ )
		{
			ulong texture = Pointer( pointers, i * sizeof(ulong) );
			if ( texture == 0 ) continue;
			string name = resource.ReadString( Pointer( resource.At( texture + 0x28, 8 ), 0 ) );
			if ( name.Length == 0 && i < hashes.Length / sizeof(uint) )
				name = "hash_" + Word( hashes, i * sizeof(uint) ).ToString( "X8", CultureInfo.InvariantCulture );
			textures.TryAdd( ArchiveIndex.HashName( name ), texture );
		}
		return textures;
	}

	public void Dispose() => System.Threading.Interlocked.Exchange( ref release, null )?.Invoke();
}
