using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GtaV.ManagedReader;

internal static class TextureRelationships
{
	internal readonly record struct Relationship( uint Child, uint Parent );
	internal readonly record struct NamedRelationship( string Child, string Parent );
	internal static bool ContainsRelationships( string stem, string extension ) => (stem, extension) is
		("gtxd" or "mph4_gtxd", ".ymt") or ("gtxd" or "vehicles", ".meta");

	internal static NamedRelationship[] Read( byte[] bytes )
	{
		var pairs = new List<NamedRelationship>();
		if ( bytes.AsSpan().StartsWith( "RBF0"u8 ) ) ReadRbf( bytes, pairs );
		else ReadXml( bytes, pairs );
		return pairs.ToArray();
	}

	static void ReadXml( byte[] bytes, List<NamedRelationship> pairs )
	{
		string parent = null;
		foreach ( var node in ArchiveXml.Read( bytes ).Descendants() )
		{
			if ( node.Name.LocalName.Equals( "parent", StringComparison.OrdinalIgnoreCase ) && parent is null ) parent = node.Value.Trim();
			else if ( node.Name.LocalName.Equals( "child", StringComparison.OrdinalIgnoreCase ) && parent is not null )
			{
				Add( pairs, parent, node.Value.Trim() );
				parent = null;
			}
		}
	}

	// Only the tagged elements and byte contents used by the relationship index are retained.
	static void ReadRbf( byte[] bytes, List<NamedRelationship> pairs )
	{
		using var stream = new MemoryStream( bytes, 0, bytes.Length, writable: false, publiclyVisible: true );
		using var reader = new BinaryReader( stream );
		stream.Position = 4;
		var names = new List<string>();
		var open = new Stack<string>();
		string parent = null;
		string child = null;
		while ( stream.Position < stream.Length )
		{
			int index = reader.ReadByte();
			int type = reader.ReadByte();
			if ( index == 0xFF )
			{
				if ( !open.TryPop( out var closed ) ) return;
				if ( closed == "item" )
				{
					Add( pairs, parent, child );
					parent = child = null;
				}
				continue;
			}
			if ( index == 0xFD )
			{
				int length = reader.ReadInt32();
				if ( open.TryPeek( out string node ) && node is "parent" or "child" )
				{
					string text = Text( reader, length ).Replace( "\0", "", StringComparison.Ordinal );
					if ( node == "parent" ) parent = text;
					else child = text;
				}
				else Advance( stream, length );
				continue;
			}
			if ( index == names.Count ) names.Add( Text( reader, reader.ReadUInt16() ).ToLowerInvariant() );
			if ( index >= names.Count ) throw new InvalidDataException( "RBF token refers to an undeclared name." );
			if ( type == 0 ) open.Push( names[index] );
			int payload = type switch
			{
				0 => 6,
				0x10 or 0x40 => 4,
				0x20 or 0x30 => 0,
				0x50 => 12,
				0x60 => reader.ReadUInt16(),
				_ => throw new InvalidDataException( $"Unknown RBF token type {type:X2}." ),
			};
			Advance( stream, payload );
		}
	}

	static void Advance( Stream stream, int length )
	{
		if ( length < 0 || length > stream.Length - stream.Position ) throw new InvalidDataException( "Truncated RBF token." );
		stream.Position += length;
	}

	static string Text( BinaryReader reader, int length )
	{
		var stream = (MemoryStream)reader.BaseStream;
		int offset = (int)stream.Position;
		Advance( stream, length );
		return Encoding.UTF8.GetString( stream.GetBuffer(), offset, length );
	}

	static void Add( List<NamedRelationship> pairs, string parent, string child )
	{
		if ( !string.IsNullOrEmpty( parent ) && !string.IsNullOrEmpty( child ) )
			pairs.Add( new NamedRelationship( child.ToLowerInvariant(), parent.ToLowerInvariant() ) );
	}
}
