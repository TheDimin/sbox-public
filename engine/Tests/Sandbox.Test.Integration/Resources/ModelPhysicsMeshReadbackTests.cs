using System;
using System.Linq;
using Editor;
using Sandbox;
namespace ResourceTests;

[TestClass]
public class ModelPhysicsMeshReadbackTests
{
	[TestMethod]
	public void CapturesEveryCollisionMeshVertexAndCorner()
	{
		Vector3[] vertices = [new( 0, 0, 0 ), new( 3, 0, 0 ), new( 0, 2, 0 ), new( 0, 0, 4 )];
		uint[] indices = [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3];
		var builder = Model.Builder;
		builder.AddBody( 12 ).AddMesh( vertices, indices, Array.Empty<byte>() );
		var model = builder.Create(); var shape = model.Physics.Parts[0].Meshes[0];
		Assert.AreEqual( ModelPhysicsMeshReadback.Status.Success, ModelPhysicsMeshReadback.Read( shape, 4, 12, out var read ) );
		Assert.AreEqual( 4, read.Vertices.Length ); Assert.AreEqual( 12, read.Indices.Length );
		// Native cooking can reorder triangles and vertices. Compare all oriented corners.
		string Key( Vector3 v ) => $"{v.x:R}/{v.y:R}/{v.z:R}";
		string Triangle( Vector3 a, Vector3 b, Vector3 c )
		{
			var x = Key( a ); var y = Key( b ); var z = Key( c );
			return new[] { x + "|" + y + "|" + z, y + "|" + z + "|" + x, z + "|" + x + "|" + y }.Order().First();
		}
		var expected = Enumerable.Range( 0, 4 ).Select( t => Triangle( vertices[indices[t * 3]], vertices[indices[t * 3 + 1]], vertices[indices[t * 3 + 2]] ) ).Order().ToArray();
		var actual = Enumerable.Range( 0, 4 ).Select( t => Triangle( read.Vertices[read.Indices[t * 3]], read.Vertices[read.Indices[t * 3 + 1]], read.Vertices[read.Indices[t * 3 + 2]] ) ).Order().ToArray();
		CollectionAssert.AreEqual( expected, actual );
		Assert.IsNull( read.Materials );
		Assert.AreEqual( ModelPhysicsMeshReadback.Status.LimitExceeded, ModelPhysicsMeshReadback.Read( shape, 3, 12, out _ ) );
		Assert.AreEqual( ModelPhysicsMeshReadback.Status.LimitExceeded, ModelPhysicsMeshReadback.Read( shape, 4, 9, out _ ) );
		Assert.AreEqual( ModelPhysicsMeshReadback.Status.InvalidLimits, ModelPhysicsMeshReadback.Read( shape, 0, 12, out _ ) );
		var thread = System.Threading.Tasks.Task.Run( () => ModelPhysicsMeshReadback.Read( shape, 4, 12, out _ ) ); thread.Wait();
		Assert.AreEqual( ModelPhysicsMeshReadback.Status.WrongThread, thread.Result );
	}
	[TestMethod]
	public void CapturesPerTriangleMaterialBytesAndSurfacePalette()
	{
		var surface = Surface.FindByName( "default" ); Surface previous = null; bool installed = surface == null;
		if ( installed ) { Surface.All.TryGetValue( 0, out previous ); surface = new Surface { Index = 0 }; surface.RegisterWeakResourceId( "surfaces/default.surface" ); Surface.All[0] = surface; }
		try
		{
			Vector3[] vertices = [new( 0, 0, 0 ), new( 3, 0, 0 ), new( 0, 2, 0 ), new( 0, 0, 4 )];
			uint[] indices = [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]; byte[] materials = [0, 1, 0, 1];
			var builder = Model.Builder.AddSurface( surface ).AddSurface( surface ); builder.AddBody( 12 ).AddMesh( vertices, indices, materials );
			var model = builder.Create(); var shape = model.Physics.Parts[0].Meshes[0];
			Assert.AreEqual( ModelPhysicsMeshReadback.Status.Success, ModelPhysicsMeshReadback.Read( shape, 4, 12, out var read ) );
			Assert.IsNotNull( read.Materials ); Assert.AreEqual( 4, read.Materials.Length ); Assert.AreEqual( 3, read.Surfaces.Length );
			// Native cooking retains both authored slots and appends the implicit body default.
			Assert.AreEqual( surface.ResourcePath, read.Surfaces[0] ); Assert.AreEqual( surface.ResourcePath, read.Surfaces[1] ); Assert.AreEqual( surface.ResourcePath, read.Surfaces[2] );
			string Corner( Vector3 v ) => $"{v.x:R}/{v.y:R}/{v.z:R}";
			string Face( Vector3 a, Vector3 b, Vector3 c ) => string.Join( "|", new[] { Corner( a ), Corner( b ), Corner( c ) }.Order() );
			var expected = Enumerable.Range( 0, 4 ).ToDictionary( t => Face( vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]] ), t => materials[t] );
			for ( int t = 0; t < 4; t++ ) Assert.AreEqual( expected[Face( read.Vertices[read.Indices[3 * t]], read.Vertices[read.Indices[3 * t + 1]], read.Vertices[read.Indices[3 * t + 2]] )], read.Materials[t] );
		}
		finally { if ( installed ) { if ( previous != null ) Surface.All[0] = previous; else Surface.All.Remove( 0 ); } }
	}

}
