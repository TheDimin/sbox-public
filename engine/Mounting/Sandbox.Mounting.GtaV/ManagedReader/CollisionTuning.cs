using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GtaV.ManagedReader;

/// <summary>Material names, densities and model overrides used by the collision packets.</summary>
internal sealed class CollisionTuning
{
	readonly record struct Material( string Name, float Density );
	readonly Material[] materials;
	readonly Dictionary<uint, float> massOverrides = new();
	readonly Dictionary<uint, float> doorMultipliers = new();

	internal CollisionTuning( ArchiveIndex index, ArchiveContent content )
	{
		var file = index.Find( ArchiveIndex.HashName( "materials" ), ".dat" )
			?? throw new InvalidDataException( "The install has no materials.dat." );
		materials = ReadMaterials( content.Extract( file ) );

		var tunables = ReadTuning( index, content, "tunableobjects" )?.Element( "TunableObjects" );
		if ( tunables is not null )
		{
			foreach ( var item in tunables.Elements() )
			{
				string name = (string)item.Element( "objectName" );
				if ( !string.IsNullOrEmpty( name ) && ReadNumber( item, "objectMass" ) is { } mass )
					massOverrides[ArchiveIndex.HashName( name )] = mass;
			}
		}

		var doors = ReadTuning( index, content, "doortuning" );
		var tuning = doors?.Element( "NamedTuningArray" );
		var mappings = doors?.Element( "ModelToTuneMapping" );
		if ( tuning is null || mappings is null ) return;

		var named = new Dictionary<string, float>( StringComparer.Ordinal );
		foreach ( var item in tuning.Elements() )
			if ( ReadNumber( item.Element( "Tuning" ), "MassMultiplier" ) is { } scale )
				named[(string)item.Element( "Name" ) ?? string.Empty] = scale;

		foreach ( var item in mappings.Elements() )
			if ( named.TryGetValue( (string)item.Element( "TuningName" ) ?? string.Empty, out float scale ) )
				doorMultipliers[ArchiveIndex.HashName( (string)item.Element( "ModelName" ) ?? string.Empty )] = scale;
	}

	internal float Density( byte id ) => id < materials.Length ? materials[id].Density : 1f;
	internal string Name( int id ) => id < materials.Length ? materials[id].Name : string.Empty;
	internal bool TryMassOverride( uint name, out float mass ) => massOverrides.TryGetValue( name, out mass );
	internal float DoorMultiplier( uint name ) => doorMultipliers.GetValueOrDefault( name, 1f );

	static Material[] ReadMaterials( byte[] bytes )
	{
		var rows = new List<Material>();
		using var text = new StringReader( Encoding.UTF8.GetString( bytes ) );
		bool hasVersion = false;
		while ( text.ReadLine() is { } line )
		{
			var fields = line.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
			if ( fields.Length == 0 || fields[0][0] == '#' ) continue;
			if ( !hasVersion )
			{
				hasVersion = true;
				continue;
			}
			if ( fields.Length < 23 ) throw new InvalidDataException( "A collision material has fewer than 23 columns." );
			float.TryParse( fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float density );
			rows.Add( new Material( fields[0], density ) );
		}
		if ( !hasVersion ) throw new InvalidDataException( "The collision materials file has no version." );
		return rows.ToArray();
	}

	static XElement ReadTuning( ArchiveIndex index, ArchiveContent content, string name )
	{
		if ( index.Find( ArchiveIndex.HashName( name ), ".meta" ) is not { } file ) return null;
		using var stream = new MemoryStream( content.Extract( file ), writable: false );
		using var reader = XmlReader.Create( stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit } );
		return XElement.Load( reader );
	}

	static float? ReadNumber( XElement item, string field )
	{
		var text = (string)item?.Element( field )?.Attribute( "value" );
		if ( string.IsNullOrEmpty( text ) || text[0] == '+' || text.AsSpan().Trim().Length != text.Length )
			return null;
		return float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number )
			&& float.IsFinite( number ) && number >= 0 ? number : null;
	}
}
