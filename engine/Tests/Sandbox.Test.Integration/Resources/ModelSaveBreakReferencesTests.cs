using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.ModelEditor.Nodes;

namespace ResourceTests;

[TestClass]
public class ModelSaveBreakReferencesTests
{
	private static HashSet<string> ExternalReferences( byte[] data )
	{
		using var stream = new MemoryStream( data );
		using var reader = new BinaryReader( stream );
		stream.Position = 8;
		var table = 8L + reader.ReadInt32();
		var count = reader.ReadUInt32();
		for ( var i = 0; i < count; i++ )
		{
			stream.Position = table + i * 12L;
			var tag = Encoding.ASCII.GetString( reader.ReadBytes( 4 ) );
			var field = stream.Position;
			var offset = reader.ReadInt32();
			var size = reader.ReadUInt32();
			if ( tag != "RERL" ) continue;
			var start = field + offset;
			stream.Position = start;
			var entries = start + reader.ReadInt32();
			var entryCount = reader.ReadUInt32();
			var result = new HashSet<string>( StringComparer.Ordinal );
			for ( var n = 0; n < entryCount; n++ )
			{
				var nameOffsetField = entries + n * 16L + 8;
				stream.Position = nameOffsetField;
				var name = nameOffsetField + reader.ReadInt32();
				Assert.IsTrue( name >= start && name < start + size );
				stream.Position = name;
				var bytes = new List<byte>();
				byte value;
				while ( stream.Position < start + size && (value = reader.ReadByte()) != 0 ) bytes.Add( value );
				result.Add( Encoding.UTF8.GetString( bytes.ToArray() ) );
			}
			return result;
		}
		return [];
	}

	[TestMethod]
	public void SaveIncludesBreakPieceReferencesAndMaterials()
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( material );
		var vertices = new Vertex[] { new( new Vector3( 0, 0, 0 ) ), new( new Vector3( 1, 0, 0 ) ), new( new Vector3( 0, 1, 0 ) ) };
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		var pieces = new ModelBreakPiece[] {
			new() { Model = "models/gibproof/first.vmdl" },
			new() { Model = "models/gibproof/second.vmdl" },
			new() { Model = "models/gibproof/first.vmdl" },
			new() { Model = "" }
		};
		var model = Model.Builder.AddMesh( mesh ).WithData( pieces ).Create();
		var refs = ExternalReferences( model.SaveToVmdl() );
		Assert.IsTrue( refs.Contains( "models/gibproof/first.vmdl" ) );
		Assert.IsTrue( refs.Contains( "models/gibproof/second.vmdl" ) );
		Assert.IsTrue( refs.Contains( material.ResourcePath ) );
		Assert.AreEqual( 3, refs.Count );
		CollectionAssert.AreEqual( pieces.Select( x => x.Model ).ToArray(), model.GetData<ModelBreakPiece[]>().Select( x => x.Model ).ToArray() );
	}
}
