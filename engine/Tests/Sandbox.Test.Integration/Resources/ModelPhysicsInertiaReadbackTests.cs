using Editor;
using Sandbox;
namespace ResourceTests;

[TestClass]
public class ModelPhysicsInertiaReadbackTests
{
	[TestMethod]
	public void CapturesAuthoredInertiaAndRejectsInvalidAccess()
	{
		var builder = Model.Builder;
		builder.AddBody( 12 ).SetInertiaScale( 2.5f ).AddBox( new Vector3( 1, 2, 3 ) );
		var model = builder.Create(); var physics = model.Physics;
		Assert.AreEqual( ModelPhysicsInertiaReadback.Status.Success, ModelPhysicsInertiaReadback.Read( physics, 0, out var raw, out var effective ) );
		Assert.AreEqual( 2.5f, raw ); Assert.AreEqual( 2.5f, effective );
		Assert.AreEqual( ModelPhysicsInertiaReadback.Status.InvalidBody, ModelPhysicsInertiaReadback.Read( physics, 1, out _, out _ ) );
		var task = System.Threading.Tasks.Task.Run( () => ModelPhysicsInertiaReadback.Read( physics, 0, out _, out _ ) ); task.Wait();
		Assert.AreEqual( ModelPhysicsInertiaReadback.Status.WrongThread, task.Result );
		// Native zero is a serialized sentinel; the effective wrapper evaluates it to one.
		var native=physics.Parts[0].native;native.m_flInertiaScale=0;
		Assert.AreEqual(ModelPhysicsInertiaReadback.Status.Success,ModelPhysicsInertiaReadback.Read(physics,0,out raw,out effective));
		Assert.AreEqual(0f,raw);Assert.AreEqual(1f,effective);
		native.m_flInertiaScale=float.NaN;
		Assert.AreEqual(ModelPhysicsInertiaReadback.Status.InvalidData,ModelPhysicsInertiaReadback.Read(physics,0,out _,out _));
		native.m_flInertiaScale=2.5f;
	}
}
