using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ResourceTests;

/// <summary>Real component state transactions, separate from authored-skeleton and GPU motion acceptance.</summary>
[TestClass]
public sealed class GltfSequenceAccessorStateTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FreshDefaultsAndMissingJsonBlendingAllowComponentCreation( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		var renderer = fixture.Renderer;
		Assert.IsTrue( renderer.SceneModel.IsValid() );
		Assert.IsFalse( renderer.Sequence.Blending );
		Assert.IsTrue( renderer.Sequence.Looping );
		Assert.AreEqual( 1f, renderer.PlaybackRate );
		Assert.IsTrue( string.IsNullOrEmpty( renderer.Sequence.Name ) );
		((IJsonPopulator)renderer.Sequence).Deserialize( JsonNode.Parse( """{"Name":"Move"}""" ) );
		Assert.IsFalse( renderer.Sequence.Blending );
		Assert.IsTrue( renderer.Sequence.Looping );
		Assert.AreEqual( "Move", renderer.SceneModel.CurrentSequence.Name );
		fixture.Tick();
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RejectedBlendingChangePreservesComponentAndRuntimeState( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		fixture.Prepare();
		var before = Snapshot( fixture.Renderer );
		Assert.ThrowsException<NotSupportedException>( () => fixture.Renderer.Sequence.Blending = true );
		AssertState( before, fixture.Renderer );
		fixture.Renderer.Sequence.Apply();
		AssertState( before, fixture.Renderer );
	}

	[TestMethod]
	[DataRow( false, float.NaN )]
	[DataRow( false, float.PositiveInfinity )]
	[DataRow( false, float.NegativeInfinity )]
	[DataRow( true, float.NaN )]
	[DataRow( true, float.PositiveInfinity )]
	[DataRow( true, float.NegativeInfinity )]
	public void RejectedClockChangePreservesComponentAndRuntimeState( bool normalized, float invalid )
	{
		using var fixture = new Fixture();
		fixture.Prepare();
		var before = Snapshot( fixture.Renderer );
		if ( normalized ) Assert.ThrowsException<ArgumentOutOfRangeException>( () => fixture.Renderer.Sequence.TimeNormalized = invalid );
		else Assert.ThrowsException<ArgumentOutOfRangeException>( () => fixture.Renderer.Sequence.Time = invalid );
		AssertState( before, fixture.Renderer );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void RejectedAccessorRatePreservesComponentAndRuntimeState( float invalid )
	{
		using var fixture = new Fixture();
		fixture.Prepare();
		var before = Snapshot( fixture.Renderer );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => fixture.Renderer.Sequence.PlaybackRate = invalid );
		AssertState( before, fixture.Renderer );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void RejectedDirectSceneRatePreservesComponentAndRuntimeState( float invalid )
	{
		using var fixture = new Fixture();
		fixture.Prepare();
		var before = Snapshot( fixture.Renderer );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => fixture.Renderer.SceneModel.PlaybackRate = invalid );
		AssertState( before, fixture.Renderer );
	}

	[TestMethod]
	[DataRow( -2f )]
	[DataRow( 0f )]
	[DataRow( 7f )]
	[DataRow( float.MaxValue )]
	[DataRow( float.MinValue )]
	public void FiniteRatesRemainAcceptedWithoutResettingTheClock( float rate )
	{
		using var fixture = new Fixture();
		fixture.Prepare();
		fixture.Renderer.Sequence.PlaybackRate = rate;
		Assert.AreEqual( rate, fixture.Renderer.PlaybackRate );
		Assert.AreEqual( rate, fixture.Renderer.SceneModel.PlaybackRate );
		Assert.AreEqual( .375f, fixture.Renderer.Sequence.Time );
		Assert.AreEqual( "Move", fixture.Renderer.Sequence.Name );
		Assert.IsFalse( fixture.Renderer.Sequence.Looping );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LoopingCanChangeWithoutResettingSelectionOrClock( bool looping )
	{
		using var fixture = new Fixture();
		fixture.Prepare();
		fixture.Renderer.Sequence.Looping = looping;
		Assert.AreEqual( looping, fixture.Renderer.Sequence.Looping );
		Assert.AreEqual( .375f, fixture.Renderer.Sequence.Time );
		Assert.AreEqual( "Move", fixture.Renderer.Sequence.Name );
		Assert.AreEqual( .5f, fixture.Renderer.Sequence.PlaybackRate );
	}

	[TestMethod]
	public void RejectedJsonBlendingDoesNotPartiallyApplyEarlierProperties()
	{
		// Separate positive acceptance for the bulk deserialization transaction.
		// Property-level fixes alone do not make this pass.
		using var fixture = new Fixture();
		fixture.Prepare();
		var before = Snapshot( fixture.Renderer );
		Assert.ThrowsException<NotSupportedException>( () => ((IJsonPopulator)fixture.Renderer.Sequence).Deserialize( JsonNode.Parse( """{"Name":"Other","Looping":true,"Blending":true}""" ) ) );
		AssertState( before, fixture.Renderer );
	}

	private sealed record State( string Json, string Name, string ActiveName, float Time, float Rate, float NativeRate, bool Looping, bool Blending, Transform Pose );
	private static State Snapshot( SkinnedModelRenderer renderer ) => new(
		((IJsonPopulator)renderer.Sequence).Serialize().ToJsonString(), renderer.Sequence.Name,
		renderer.SceneModel.CurrentSequence.Name, renderer.Sequence.Time, renderer.PlaybackRate,
		renderer.SceneModel.PlaybackRate, renderer.Sequence.Looping, renderer.Sequence.Blending,
		renderer.SceneModel.GetBoneWorldTransform( 0 ) );
	private static void AssertState( State expected, SkinnedModelRenderer renderer ) => Assert.AreEqual( expected, Snapshot( renderer ), "A rejected edit must preserve serialized state, selection, clock, rate, looping and posed bones." );

	private sealed class Fixture : IDisposable
	{
		private readonly BaseFileSystem previousMount;
		private readonly string directory;
		private Scene scene;
		private Model model;
		internal SkinnedModelRenderer Renderer { get; private set; }

		internal Fixture( bool compiled = false )
		{
			Assert.IsTrue( ThreadSafe.IsMainThread );
			previousMount = GlobalContext.Current.FileMount;
			directory = Path.Combine( Path.GetTempPath(), "gltf_sequence_state_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory );
			try
			{
				Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
				var source = Source(); var filename = "state_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
				GlobalContext.Current.FileMount = new AggregateFileSystem();
				Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
				model = Model.Load( filename ); Assert.IsTrue( model is { IsValid: true, IsError: false } );
				scene = Scene.CreateEditorScene();
				using var scope = scene.Push();
				Renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				Renderer.Model = model; Renderer.UseAnimGraph = false; Renderer.PlayAnimationsInEditorScene = true;
				Renderer.Enabled = true; Tick();
				Assert.IsTrue( Renderer.SceneModel.IsValid() );
			}
			catch { Dispose(); throw; }
		}

		internal void Prepare()
		{
			Renderer.Sequence.Name = "Move"; Renderer.Sequence.Time = .375f;
			Renderer.Sequence.Looping = false; Renderer.PlaybackRate = .5f; Tick();
		}
		internal void Tick() { using var scope = scene.Push(); scene.EditorTick( .5f, 0 ); }
		public void Dispose()
		{
			scene?.Destroy(); scene = null; model?.Destroy(); model = null;
			GlobalContext.Current.FileMount = previousMount;
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true );
		}
	}

	private static byte[] Source()
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
			foreach ( var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0 } ) writer.Write( value );
		var binary = stream.ToArray();
		string Clip( string name ) => "{\"name\":\"" + name + "\",\"samplers\":[{\"input\":1,\"output\":2}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}]}";
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( binary ) + "\",\"byteLength\":" + binary.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}],\"nodes\":[{\"mesh\":0}],\"animations\":[" + Clip( "Move" ) + "," + Clip( "Other" ) + "],\"scenes\":[{\"nodes\":[0]}],\"scene\":0}" );
	}
}
