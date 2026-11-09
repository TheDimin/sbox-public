using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using Sandbox.Engine;

namespace ResourceTests;

[TestClass]
public class ModelReadbackTests
{
	[StructLayout( LayoutKind.Sequential )]
	struct PositionOnly
	{
		[VertexLayout.Position] public Vector3 Position;
	}

	static Model CreateFixture()
	{
		var material = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 4, new Vertex[]
		{
			new() { Position = new( 0, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new( 1, 0, 0 ), Normal = Vector3.Up },
			new() { Position = new( 0, 1, 0 ), Normal = Vector3.Up },
			new() { Position = new( 0, 0, 1 ), Normal = Vector3.Up }
		} );
		mesh.CreateIndexBuffer( 6, new[] { 0, 1, 2, 0, 1, 2 } );
		mesh.Bounds = new BBox( Vector3.Zero, Vector3.One );
		mesh.AddSubMesh( material, 3, 3, 1, 3 );
		var lod1 = new Mesh( material );
		lod1.CreateVertexBuffer( 3, new PositionOnly[]
		{
			new() { Position = new( 4, 0, 0 ) },
			new() { Position = new( 5, 0, 0 ) },
			new() { Position = new( 4, 1, 0 ) }
		} );
		lod1.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		lod1.Bounds = new BBox( new Vector3( 4, 0, 0 ), new Vector3( 5, 1, 0 ) );
		return Model.Builder.AddMesh( mesh, 0 ).AddMesh( lod1, 1 ).Create();
	}

	[TestMethod]
	public void ReadsEachLodAndRebasesSharedDraws()
	{
		Assert.IsTrue( ThreadSafe.IsMainThread, "Native readback must run on the engine main thread." );
		var model = CreateFixture();
		var meshes = model.MeshInfo.Meshes;
		int m0 = Array.FindIndex( meshes, m => (m.LodMask & 1) != 0 );
		int m1 = Array.FindIndex( meshes, m => (m.LodMask & 2) != 0 );
		Assert.IsTrue( m0 >= 0 && m1 >= 0 && m0 != m1 );
		Assert.AreEqual( 2, meshes[m0].DrawCalls.Length );
		Assert.AreEqual( ModelReadback.Status.Success, ModelReadback.ReadDraw( model, m0, 1, 4, 6, out var shared ) );
		Assert.AreEqual( 3, shared.Indices.Length );
		var actual = shared.Indices.Select( i => shared.Vertices[i].Position ).ToArray();
		CollectionAssert.AreEqual( new[] { new Vector3( 1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ) }, actual );
		Assert.IsTrue( shared.AvailableStreams.HasFlag( ModelReadback.Streams.Position ) );
		Assert.AreEqual( ModelReadback.Status.Success, ModelReadback.ReadDraw( model, m1, 0, 4, 6, out var positionOnly ) );
		Assert.IsTrue( positionOnly.AvailableStreams.HasFlag( ModelReadback.Streams.Position ) );
		Assert.IsFalse( positionOnly.AvailableStreams.HasFlag( ModelReadback.Streams.Normal ) );
		Assert.AreEqual( new Vector3( 4, 0, 0 ), positionOnly.Vertices[positionOnly.Indices[0]].Position );
		var firstCopy = shared.Vertices[0];
		Assert.AreEqual( ModelReadback.Status.Success, ModelReadback.ReadDraw( model, m0, 1, 4, 6, out var next ) );
		Assert.AreNotSame( shared.Vertices, next.Vertices );
		next.Vertices[0] = default;
		Assert.AreEqual( firstCopy.Position, shared.Vertices[0].Position );
	}

	[TestMethod]
	public void RejectsBadRequestsAndWorkerThread()
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var model = CreateFixture();
		Assert.AreEqual( ModelReadback.Status.InvalidModel, ModelReadback.ReadDraw( null, 0, 0, 4, 6, out _ ) );
		Assert.AreEqual( ModelReadback.Status.InvalidMesh, ModelReadback.ReadDraw( model, -1, 0, 4, 6, out _ ) );
		Assert.AreEqual( ModelReadback.Status.InvalidDraw, ModelReadback.ReadDraw( model, 0, 1000, 4, 6, out _ ) );
		Assert.AreEqual( ModelReadback.Status.InvalidLimits, ModelReadback.ReadDraw( model, 0, 0, ModelReadback.MaxVertices + 1, 6, out _ ) );
		Assert.AreEqual( ModelReadback.Status.LimitExceeded, ModelReadback.ReadDraw( model, 0, 0, 1, 1, out var failed ) );
		Assert.IsNull( failed );
		var task = Task.Run( () => ModelReadback.ReadDraw( model, 0, 0, 4, 6, out _ ) );
		Assert.AreEqual( ModelReadback.Status.WrongThread, task.GetAwaiter().GetResult() );
	}

	[TestMethod]
	public void ReadsCompiledTwoLodFixtureFromIsolatedMount()
	{
		var fixture = Environment.GetEnvironmentVariable( "B2SBOX_COMPILED_FIXTURE" );
		if ( string.IsNullOrEmpty( fixture ) ) Assert.Inconclusive( "Set B2SBOX_COMPILED_FIXTURE to the generated two-LOD compiled fixture." );
		Assert.IsTrue( ThreadSafe.IsMainThread );
		string dir = Path.Combine( Path.GetTempPath(), $"model-readback-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( dir );
		var previousMount = GlobalContext.Current.FileMount;
		try
		{
			File.Copy( fixture, Path.Combine( dir, "readback_known.vmdl_c" ) );
			NativeEngine.FullFileSystem.AddProjectPath( "model_readback_test", dir );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( dir ) );
			var model = Model.Load( "readback_known.vmdl" );
			Assert.IsNotNull( model );
			Assert.IsFalse( model.IsError );
			var meshes = model.MeshInfo.Meshes;
			var exportedMeshes = new List<object>();
			for ( int lod = 0; lod < 2; lod++ )
			{
				int total = 0;
				for ( int mesh = 0; mesh < meshes.Length; mesh++ )
				{
					if ( (meshes[mesh].LodMask & (1 << lod)) == 0 ) continue;
					var exportedDraws = new List<object>();
					for ( int draw = 0; draw < meshes[mesh].DrawCalls.Length; draw++ )
					{
						Assert.AreEqual( ModelReadback.Status.Success, ModelReadback.ReadDraw( model, mesh, draw, 250_000, 750_000, out var data ) );
						Assert.IsTrue( data.AvailableStreams.HasFlag( ModelReadback.Streams.Position ) );
						Assert.IsTrue( data.AvailableStreams.HasFlag( ModelReadback.Streams.Normal ) );
						Assert.IsTrue( data.AvailableStreams.HasFlag( ModelReadback.Streams.TexCoord0 ) );
						total += data.Indices.Length / 3;
						exportedDraws.Add( new
						{
							vertices = data.Vertices.Select( v => new { position = new[] { v.Position.x, v.Position.y, v.Position.z }, uv0 = new[] { v.TexCoord0.x, v.TexCoord0.y } } ).ToArray(),
							indices = data.Indices
						} );
					}
					exportedMeshes.Add( new { Name = meshes[mesh].Name, draws = exportedDraws } );
				}
				Assert.AreEqual( lod == 0 ? 576 : 144, total );
			}
			var output = Environment.GetEnvironmentVariable( "B2SBOX_READBACK_TEST_OUTPUT" );
			if ( !string.IsNullOrEmpty( output ) )
				File.WriteAllText( output, JsonSerializer.Serialize( new { report = new { diagnosticPositionsOnly = true, geometryComplete = false, geometry = exportedMeshes } } ) );
		}
		finally
		{
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( dir, true );
		}
	}


	static void RewriteFixtureGuid( byte[] bytes, Guid id )
	{
		if ( bytes.Length < 16 || BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( 4 ) ) != 12 ) throw new InvalidDataException( "Invalid fixture header." );
		int table = checked( 8 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 8 ) ) );
		uint count = BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 12 ) );
		if ( count > 4096 || table < 16 || table + 12L * count > bytes.Length ) throw new InvalidDataException( "Invalid fixture block table." );
		int seen = 0;
		for ( int i = 0; i < count; i++ )
		{
			int entry = checked(table + i * 12);
			if ( !bytes.AsSpan( entry, 4 ).SequenceEqual( "GUID"u8 ) ) continue;
			int start = checked(entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( entry + 4 ) ));
			uint length = BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( entry + 8 ) );
			if ( length != 16 || start < 0 || (long)start + length > bytes.Length || ++seen != 1 ) throw new InvalidDataException( "Invalid fixture GUID block." );
			if ( !id.TryWriteBytes( bytes.AsSpan( start, 16 ) ) ) throw new InvalidDataException( "Cannot write fixture GUID." );
		}
		if ( seen != 1 ) throw new InvalidDataException( "Fixture requires one GUID block." );
	}

	[TestMethod]
	public void PrivateAliasBypassesStalePathAndGuidCache()
	{
		var parentPath = Environment.GetEnvironmentVariable( "B2SBOX_COMPILED_FIXTURE" );
		var piecePath = Environment.GetEnvironmentVariable( "B2SBOX_COMPILED_PIECE" );
		if ( string.IsNullOrEmpty( parentPath ) || string.IsNullOrEmpty( piecePath ) ) Assert.Inconclusive( "Set the compiled parent and piece fixture paths." );
		Assert.IsTrue( ThreadSafe.IsMainThread );
		string dir = Path.Combine( Path.GetTempPath(), $"model-alias-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( dir );
		var previousMount = GlobalContext.Current.FileMount;
		try
		{
			var oldGuid = Guid.NewGuid(); var newGuid = Guid.NewGuid();
			var parent = File.ReadAllBytes( parentPath ); var replacement = File.ReadAllBytes( piecePath );
			RewriteFixtureGuid( parent, oldGuid ); RewriteFixtureGuid( replacement, oldGuid );
			File.WriteAllBytes( Path.Combine( dir, "original.vmdl_c" ), parent );
			File.WriteAllText( Path.Combine( dir, "original.vmdl.meta" ), $"{{\"guid\":\"{oldGuid}\"}}" );
			NativeEngine.FullFileSystem.AddProjectPath( "model_alias_test", dir );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( dir ) );
			var old = Model.Load( "original.vmdl" );
			Assert.IsNotNull( old ); Assert.IsFalse( old.IsError );
			Assert.AreEqual( oldGuid, old.Guid );
			int oldTriangles = old.MeshInfo.TotalTriangles;
			File.WriteAllBytes( Path.Combine( dir, "original.vmdl_c" ), replacement );
			var cached = Model.Load( "original.vmdl" );
			Assert.AreSame( old, cached );
			Assert.AreEqual( oldTriangles, cached.MeshInfo.TotalTriangles );
			RewriteFixtureGuid( replacement, newGuid );
			File.WriteAllBytes( Path.Combine( dir, "alias.vmdl_c" ), replacement );
			File.WriteAllText( Path.Combine( dir, "alias.vmdl.meta" ), $"{{\"guid\":\"{newGuid}\"}}" );
			NativeEngine.g_pResourceSystem.NotifyMetaFileChanged( "alias.vmdl", true );
			var fresh = Model.Load( "alias.vmdl" );
			Assert.IsNotNull( fresh ); Assert.IsFalse( fresh.IsError );
			Assert.AreEqual( newGuid, fresh.Guid );
			Assert.AreNotSame( old, fresh );
			Assert.AreNotEqual( oldTriangles, fresh.MeshInfo.TotalTriangles );
		}
		finally
		{
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( dir, true );
		}
	}

}
