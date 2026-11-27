using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

/// <summary>Reads the parameters referenced by the mount from one shader.</summary>
internal readonly ref struct DrawableShader
{
	readonly RscResource resource;
	readonly bool enhanced;
	readonly ReadOnlySpan<byte> parameters, legacyNames, texturePointers, bufferPointers, buffers;
	readonly DrawableShaderTable.Shader mapping;
	readonly int bufferCount;
	internal uint Name { get; }
	internal uint Preset { get; }
	internal byte Bucket { get; }

	internal DrawableShader( RscResource resource, ulong pointer, bool enhanced )
	{
		this.resource = resource;
		this.enhanced = enhanced;
		if ( pointer == 0 ) return;
		if ( !enhanced )
		{
			var shader = resource.At( pointer, 0x1C );
			Name = Word( shader, 8 );
			Bucket = shader[0x11];
			Preset = Word( shader, 0x18 );
			int count = shader[0x10];
			ulong data = Pointer( shader, 0 );
			parameters = resource.Array( data, count, 16 );
			if ( !parameters.IsEmpty )
			{
				int hashesOffset = parameters.Length;
				for ( int i = 0; i < parameters.Length; i += 16 ) hashesOffset += 16 * parameters[i];
				legacyNames = resource.At( data + (uint)hashesOffset, count * sizeof(uint) );
			}
			return;
		}

		var gen9 = resource.At( pointer, 0x3A );
		Name = Word( gen9, 0 );
		Bucket = gen9[0x39];
		mapping = DrawableShaderTable.Find( Name );
		Preset = mapping.Preset( Bucket );
		ulong info = Pointer( gen9, 0x20 );
		if ( info == 0 ) return;

		var header = resource.At( info, 8 );
		int copies = header[7];
		if ( copies == 0 ) throw new InvalidDataException( "The shader parameter block has no copies." );
		bufferCount = header[0];
		parameters = resource.Array( info + 8, header[4], 8 );
		texturePointers = resource.Array( Pointer( gen9, 0x10 ), header[1], sizeof(ulong) );
		if ( bufferCount == 0 ) return;

		ulong dataPointer = Pointer( gen9, 8 );
		bufferPointers = resource.Array( dataPointer, bufferCount * copies, sizeof(ulong) );
		if ( bufferPointers.IsEmpty ) throw new InvalidDataException( "The shader parameter block has no buffer pointers." );
		ulong first = Pointer( bufferPointers, 0 );
		ulong last = bufferPointers.Length > bufferCount * sizeof(ulong) ? Pointer( bufferPointers, bufferCount * sizeof(ulong) )
			: Pointer( bufferPointers, (bufferCount - 1) * sizeof(ulong) ) + 16;
		if ( last < first || last - first > int.MaxValue ) throw new InvalidDataException( "Invalid shader buffer size." );
		buffers = resource.Array( first, (int)(last - first), 1 );
	}

	internal string Texture( uint name )
	{
		if ( !enhanced )
		{
			int index = Find( legacyNames, sizeof(uint), name );
			return index >= 0 && parameters[16 * index] == 0 ? ReadTextureReference( Pointer( parameters, 16 * index + 8 ) ) : string.Empty;
		}
		if ( !TryGetEnhancedName( name, constant: false, out uint enhancedName, out _ ) ) return string.Empty;
		int index9 = enhancedName == 0 ? -1 : Find( parameters, 8, enhancedName );
		if ( index9 < 0 ) return string.Empty;
		uint tag = Word( parameters, index9 * 8 + 4 );
		int slot = (int)((tag >> 2) & 0xFF);
		return (tag & 3) == 0 && slot < texturePointers.Length / sizeof(ulong)
			? ReadTextureReference( Pointer( texturePointers, slot * sizeof(ulong) ) ) : string.Empty;
	}

	internal NumericsVector4? Constant( uint name )
	{
		if ( !enhanced )
		{
			int index = Find( legacyNames, sizeof(uint), name );
			if ( index < 0 || parameters[16 * index] == 0 ) return null;
			ulong value = Pointer( parameters, 16 * index + 8 );
			return value == 0 ? default(NumericsVector4) : Vector4( resource.At( value, 16 ), 0 );
		}
		if ( !TryGetEnhancedName( name, constant: true, out uint enhancedName, out var defaults ) ) return null;
		int index9 = enhancedName == 0 ? -1 : Find( parameters, 8, enhancedName );
		if ( index9 < 0 ) return mapping.HasLegacyParameters ? defaults : null;
		uint tag = Word( parameters, index9 * 8 + 4 );
		if ( (tag & 3) != 3 ) return mapping.HasLegacyParameters ? defaults : null;

		int slot = (int)((tag >> 2) & 0x3F);
		int offset = (int)((tag >> 8) & 0xFFF);
		int size = (int)(tag >> 20);
		int start = slot < bufferCount ? checked((int)(Pointer( bufferPointers, slot * sizeof(ulong) ) - Pointer( bufferPointers, 0 ))) : buffers.Length;
		int position = checked(start + offset);
		int bytes = Math.Min( size, 16 );
		int required = size <= 16 ? size : size - size % 16;
		if ( position < 0 || position > buffers.Length || required > buffers.Length - position )
			return ClearComponents( defaults, bytes );
		var result = defaults;
		for ( int i = 0; i < bytes / sizeof(float); i++ ) result[i] = Float( buffers, position + i * sizeof(float) );
		return result;
	}

	bool TryGetEnhancedName( uint name, bool constant, out uint enhancedName, out NumericsVector4 defaults )
	{
		defaults = default;
		enhancedName = name;
		if ( !mapping.HasLegacyParameters ) return true;
		if ( !mapping.TryGetParameter( name, out var parameter ) || parameter.Constant != constant ) return false;
		enhancedName = parameter.Enhanced;
		defaults = parameter.Default;
		return true;
	}

	static NumericsVector4 ClearComponents( NumericsVector4 value, int bytes )
	{
		for ( int i = 0; i < bytes / sizeof(float); i++ ) value[i] = 0;
		return value;
	}

	string ReadTextureReference( ulong pointer ) => pointer == 0 ? string.Empty : resource.ReadString( Pointer( resource.At( pointer + 0x28, 8 ), 0 ) );

	static int Find( ReadOnlySpan<byte> data, int stride, uint name )
	{
		for ( int at = 0; at < data.Length; at += stride )
			if ( Word( data, at ) == name ) return at / stride;
		return -1;
	}
}
