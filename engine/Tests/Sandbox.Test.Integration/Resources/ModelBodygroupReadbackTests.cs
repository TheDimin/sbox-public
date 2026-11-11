using System;
using System.Linq;
using Editor;
using Sandbox;

namespace ResourceTests;

[TestClass]
public class ModelBodygroupReadbackTests
{
	[TestMethod]
	public void CapturesGroupsAndRejectsLimits()
	{
		var mat = Material.Load( "materials/default/white.vmat" );
		var mesh = new Mesh( mat );
		mesh.CreateVertexBuffer( 3, new Vertex[] { new() { Position = new( 0, 0, 0 ) }, new() { Position = new( 1, 0, 0 ) }, new() { Position = new( 0, 1, 0 ) } } );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } ); mesh.Bounds = new BBox( Vector3.Zero, Vector3.One );
		var model = Model.Builder.AddMesh( mesh, 0, "body", 0 ).AddMesh( mesh, 0, "body", 1 ).Create();
		Assert.AreEqual( ModelBodygroupReadback.Status.Success, ModelBodygroupReadback.Read( model, 64, 64, out var result ) );
		Assert.AreEqual( 2, result.MeshGroupCount ); Assert.AreEqual( 1, result.Groups.Length ); Assert.AreEqual( "body", result.Groups[0].Name ); Assert.AreEqual( 2, result.Groups[0].Choices.Length );
		Assert.AreEqual( ModelBodygroupReadback.Status.LimitExceeded, ModelBodygroupReadback.Read( model, 0, 64, out _ ) );
		Assert.AreEqual( ModelBodygroupReadback.Status.LimitExceeded, ModelBodygroupReadback.Read( model, 64, 1, out _ ) );
		Assert.AreEqual( ModelBodygroupReadback.Status.InvalidLimits, ModelBodygroupReadback.Read( model, 65, 64, out _ ) );
		var task = System.Threading.Tasks.Task.Run( () => ModelBodygroupReadback.Read( model, 64, 64, out _ ) ); task.Wait();
		Assert.AreEqual( ModelBodygroupReadback.Status.WrongThread, task.Result );
	}
}
