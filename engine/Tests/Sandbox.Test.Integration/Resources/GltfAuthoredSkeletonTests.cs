using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ResourceTests;

/// <summary>
/// Positive authored-skeleton acceptance, separate from the initial render-palette
/// motion proof. Current proxy bones do not satisfy these tests. Palette readback
/// here verifies engine state, not GPU appearance or attachment/physics/graph parity.
/// </summary>
[TestClass]
public sealed class GltfAuthoredSkeletonTests
{
	private const float Unit = 1f / .0254f;

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SourceJointNamesHierarchyAndRestTransformsReachExistingModelApi( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		var model = fixture.Model;
		AssertSourceSkeleton( model );
		AssertVector( new Vector3( 0, Unit, 0 ), model.GetBoneTransform( "Armature" ).Position );
		AssertVector( new Vector3( 0, Unit, Unit ), model.GetBoneTransform( "Root" ).Position );
		AssertVector( new Vector3( 0, Unit, 2 * Unit ), model.GetBoneTransform( "Tip" ).Position );
		AssertVector( Vector3.One, model.GetBoneTransform( "Tip" ).Scale );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BoneObjectsAndAnimationTransformsFollowTheAuthoredSkeleton( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		var renderer = fixture.CreateRenderer( boneObjects: true );
		var tip = fixture.Model.Bones.GetBone( "Tip" );
		Assert.IsNotNull( tip, "The authored source joint must be addressable through the established bone API." );
		renderer.Sequence.Name = "Reach";
		renderer.Sequence.Time = .5f;
		fixture.Tick();
		var tipObject = renderer.GetBoneObject( "Tip" );
		Assert.IsTrue( tipObject.IsValid() );
		Assert.AreEqual( "Root", tipObject.Parent.Name );
		AssertVector( new Vector3( 0, 0, 1.5f * Unit ), tipObject.LocalPosition );
		AssertVector( new Vector3( 0, Unit, 2.5f * Unit ), tipObject.WorldPosition );
		Assert.IsTrue( renderer.TryGetBoneTransform( "Tip", out var world ) );
		AssertVector( tipObject.WorldPosition, world.Position );
		Assert.IsTrue( renderer.TryGetBoneTransformAnimation( tip, out var animated ) );
		AssertVector( world.Position, animated.Position );
		AssertVector( tipObject.LocalPosition, renderer.SceneModel.GetParentSpaceBone( tip.Index ).Position );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SharedSourceJointsHaveOnePublicIdentityAndSeparateSkinPalettes( bool compiled )
	{
		using var fixture = new Fixture( compiled, multipleSkins: true );
		AssertSourceSkeleton( fixture.Model );
		var renderer = fixture.CreateRenderer();
		renderer.Sequence.Name = "Reach";
		renderer.Sequence.Time = .5f;
		fixture.Tick();
		Assert.IsTrue( renderer.TryGetBoneTransform( "Tip", out var world ) );
		AssertVector( new Vector3( 0, Unit, 2.5f * Unit ), world.Position );
		var palettes = new List<Matrix3x4>();
		for ( var mesh = 0; mesh < fixture.Model.MeshCount; mesh++ ) palettes.AddRange( renderer.SceneModel.GetGltfSkinningPalette( mesh ).ToArray() );
		foreach ( var expectedY in new[] { 0f, -3 * Unit } )
			Assert.IsTrue( palettes.Any( palette => MathF.Abs( palette.Row1.w - expectedY ) < .0001f && MathF.Abs( palette.Row2.w - .5f * Unit ) < .0001f ),
				"Different inverse binds must produce different render palettes without duplicating or moving the public source joint; native mesh packing is unrestricted." );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NonUnitRestScaleIsPreservedByModelAndRuntimeBoneApis( bool compiled )
	{
		using var fixture = new Fixture( compiled, scaled: true );
		AssertSourceSkeleton( fixture.Model );
		var rest = fixture.Model.GetBoneTransform( "Root" );
		AssertVector( new Vector3( 4, 2, 3 ), rest.Scale );
		AssertVector( new Vector3( 0, Unit, 4 * Unit ), fixture.Model.GetBoneTransform( "Tip" ).Position );
		var renderer = fixture.CreateRenderer();
		Assert.IsTrue( renderer.TryGetBoneTransform( "Root", out var world ) );
		AssertVector( rest.Scale, world.Scale );
		AssertVector( rest.Position, world.Position );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void InheritedShearRemainsAnExactRuntimeRenderPalette( bool compiled )
	{
		using var fixture = new Fixture( compiled, scaled: true, sheared: true );
		var renderer = fixture.CreateRenderer();
		fixture.Tick();
		// Independent row-vector construction from the fixture's authored nodes.
		// Do not use the importer, pose evaluator or its coordinate-conversion helper.
		var root = NMatrix.CreateScale( 2, 3, 4 ) * NMatrix.CreateTranslation( 0, 1, 0 ) * NMatrix.CreateTranslation( 1, 0, 0 );
		var tip = NMatrix.CreateRotationZ( MathF.PI / 4 ) * NMatrix.CreateTranslation( 0, 1, 0 ) * root;
		var expected = NMatrix.CreateTranslation( -1, -2, 0 ) * tip;
		var palette = renderer.SceneModel.GetGltfSkinningPalette( 0 ).ToArray();
		Assert.IsTrue( palette.Any( actual => Matches( actual, expected ) ), "A valid inherited affine transform must reach the runtime palette without TRS approximation or rejection." );
	}

	private static void AssertSourceSkeleton( Model model )
	{
		Assert.AreEqual( 3, model.BoneCount, "One source master skeleton must include the two joints and their non-joint ancestor, independently of skin count." );
		CollectionAssert.AreEquivalent( new[] { "Armature", "Root", "Tip" }, model.Bones.AllBones.Select( bone => bone.Name ).ToArray() );
		var armature = model.Bones.GetBone( "Armature" );
		var root = model.Bones.GetBone( "Root" );
		var tip = model.Bones.GetBone( "Tip" );
		Assert.IsNotNull( armature ); Assert.IsNotNull( root ); Assert.IsNotNull( tip );
		Assert.IsNull( armature.Parent );
		Assert.AreSame( armature, root.Parent );
		Assert.AreSame( root, tip.Parent );
		Assert.AreEqual( root.Index, model.GetBoneParent( tip.Index ) );
		Assert.AreEqual( armature.Index, model.GetBoneParent( root.Index ) );
	}

	private static bool Matches( Matrix3x4 actual, NMatrix expected )
	{
		foreach ( var marker in new[] { NVector.Zero, NVector.UnitX, NVector.UnitY, NVector.UnitZ } )
		{
			var point = ConvertPoint( marker );
			var transformed = new Vector3( DotRow( actual.Row0, point ), DotRow( actual.Row1, point ), DotRow( actual.Row2, point ) );
			if ( !transformed.AlmostEqual( ConvertPoint( NVector.Transform( marker, expected ) ), .0001f ) ) return false;
		}
		return true;
	}

	private static float DotRow( Vector4 row, Vector3 point ) => row.x * point.x + row.y * point.y + row.z * point.z + row.w;

	private static Vector3 ConvertPoint( NVector value ) => new( value.Z * Unit, value.X * Unit, value.Y * Unit );
	private static void AssertVector( Vector3 expected, Vector3 actual ) => Assert.IsTrue( expected.AlmostEqual( actual, .0001f ), $"Expected {expected}; actual {actual}." );

	private sealed class Fixture : IDisposable
	{
		private readonly BaseFileSystem previousMount;
		private readonly string directory;
		private Scene scene;
		internal Model Model { get; private set; }

		internal Fixture( bool compiled, bool multipleSkins = false, bool scaled = false, bool sheared = false )
		{
			Assert.IsTrue( ThreadSafe.IsMainThread );
			previousMount = GlobalContext.Current.FileMount;
			directory = Path.Combine( Path.GetTempPath(), "gltf_authored_skeleton_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory );
			try
			{
				Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
				var source = Source( multipleSkins, scaled, sheared );
				var filename = "authored_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
				GlobalContext.Current.FileMount = new AggregateFileSystem();
				Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
				Model = Model.Load( filename );
				Assert.IsTrue( Model is { IsValid: true, IsError: false } );
			}
			catch { Dispose(); throw; }
		}

		internal SkinnedModelRenderer CreateRenderer( bool boneObjects = false )
		{
			scene ??= Scene.CreateEditorScene();
			using var scope = scene.Push();
			var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.Model = Model;
			renderer.CreateBoneObjects = boneObjects;
			renderer.UseAnimGraph = false;
			renderer.PlaybackRate = 0;
			renderer.PlayAnimationsInEditorScene = true;
			renderer.Enabled = true;
			Tick();
			Assert.IsTrue( renderer.SceneModel.IsValid(), "A valid glTF, including scaled or sheared source poses, must instantiate through the normal component path." );
			return renderer;
		}

		internal void Tick() { using var scope = scene.Push(); scene.EditorTick( .5f, 0 ); }

		public void Dispose()
		{
			scene?.Destroy(); scene = null;
			Model?.Destroy(); Model = null;
			GlobalContext.Current.FileMount = previousMount;
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true );
		}
	}

	private static byte[] Source( bool multipleSkins, bool scaled, bool sheared )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true );
		var views = new List<object>(); var accessors = new List<Dictionary<string, object>>();
		int Accessor( byte[] bytes, int componentType, int count, string type )
		{
			var index = accessors.Count;
			views.Add( new { buffer = 0, byteOffset = (int)stream.Position, byteLength = bytes.Length } );
			writer.Write( bytes );
			accessors.Add( new() { ["bufferView"] = index, ["componentType"] = componentType, ["count"] = count, ["type"] = type } );
			return index;
		}
		int Floats( float[] values, int count, string type ) { var bytes = new byte[values.Length * 4]; Buffer.BlockCopy( values, 0, bytes, 0, bytes.Length ); return Accessor( bytes, 5126, count, type ); }
		float[] InverseBinds( float x ) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, -1, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, -2, 0, 1];
		var positions = Floats( [0, 0, 0, 1, 0, 0, 0, 1, 0], 3, "VEC3" );
		accessors[positions]["min"] = new[] { 0, 0, 0 }; accessors[positions]["max"] = new[] { 1, 1, 0 };
		var joints = Accessor( [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], 5121, 3, "VEC4" );
		var weights = Floats( [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], 3, "VEC4" );
		var firstBinds = Floats( InverseBinds( -1 ), 2, "MAT4" );
		var times = Floats( [0, 1], 2, "SCALAR" ); accessors[times]["min"] = new[] { 0 }; accessors[times]["max"] = new[] { 1 };
		var translations = Floats( [0, 1, 0, 0, 2, 0], 2, "VEC3" );
		var nodes = new List<Dictionary<string, object>>
		{
			new() { ["name"] = "Armature", ["translation"] = new[] { 1, 0, 0 }, ["children"] = new[] { 1 } },
			new() { ["name"] = "Root", ["translation"] = new[] { 0, 1, 0 }, ["children"] = new[] { 2 } },
			new() { ["name"] = "Tip", ["translation"] = new[] { 0, 1, 0 } },
			new() { ["name"] = "BodyA", ["mesh"] = 0, ["skin"] = 0 }
		};
		if ( scaled ) nodes[1]["scale"] = new[] { 2, 3, 4 };
		if ( sheared ) nodes[2]["rotation"] = new[] { 0f, 0f, MathF.Sin( MathF.PI / 8 ), MathF.Cos( MathF.PI / 8 ) };
		var skins = new List<object> { new { name = "First", joints = new[] { 1, 2 }, skeleton = 0, inverseBindMatrices = firstBinds } };
		if ( multipleSkins )
		{
			var secondBinds = Floats( InverseBinds( -4 ), 2, "MAT4" );
			skins.Add( new { name = "Second", joints = new[] { 1, 2 }, skeleton = 0, inverseBindMatrices = secondBinds } );
			nodes.Add( new() { ["name"] = "BodyB", ["mesh"] = 0, ["skin"] = 1 } );
		}
		var binary = stream.ToArray();
		return JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0" }, buffers = new[] { new { byteLength = binary.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String( binary ) } }, bufferViews = views, accessors,
			meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = positions, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights } } } } }, nodes, skins,
			animations = new[] { new { name = "Reach", samplers = new[] { new { input = times, output = translations } }, channels = new[] { new { sampler = 0, target = new { node = 2, path = "translation" } } } } },
			scenes = new[] { new { nodes = multipleSkins ? new[] { 0, 3, 4 } : new[] { 0, 3 } } }, scene = 0
		} );
	}
}
