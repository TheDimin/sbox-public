// Run in the real editor. This deliberately tests the native procedural mesh
// contract independently of glTF parsing, without claiming glTF load acceptance.
await MainThread.Wait();
if ( Graphics.IsActive ) throw new InvalidOperationException( "Run outside rendering." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var rendererAssembly = System.Reflection.Assembly.Load( "Sandbox.SceneRenderer" );
var readSkin = rendererAssembly.GetType( "Sandbox.SceneRenderer.Gpu.RenderContext", true ).GetMethod( "ReadMeshSkin", flags );
var rows = new System.Collections.Generic.List<object>();
var material = Material.Load( "materials/default/white.vmat" );
foreach ( var floatingWeights in new[] { false, true } )
{
	Model model = null;
	try
	{
		var data = new Vertex[3];
		var stride = System.Runtime.InteropServices.Marshal.SizeOf<Vertex>();
		var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes( data.AsSpan() );
		var layout = new System.Collections.Generic.List<VertexAttribute>
		{
			new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
			new( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
			new( VertexAttributeType.BlendWeights, floatingWeights ? VertexAttributeFormat.Float32 : VertexAttributeFormat.UInt8, 4 )
		};
		var used = floatingWeights ? 60 : 48;
		for ( var set = 0; used < stride; set++ )
		{
			var components = Math.Min( 4, (stride - used) / 4 );
			if ( components == 0 ) throw new InvalidOperationException( "Unexpected diagnostic POD alignment." );
			layout.Add( new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, components, set ) );
			used += components * 4;
		}
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			var at = vertex * stride;
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at, 4 ), vertex == 1 ? 20 : 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 4, 4 ), vertex == 2 ? 20 : 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 20, 4 ), 1 );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 24, 4 ), 1 );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 36, 4 ), -1 );
			bytes[at + 40] = 0; bytes[at + 41] = 1;
			if ( floatingWeights )
			{
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 44, 4 ), .3f );
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.Slice( at + 48, 4 ), .7f );
			}
			else { bytes[at + 44] = 85; bytes[at + 45] = 170; }
		}
		var mesh = new Mesh( "gltf_native_skin_contract", material );
#pragma warning disable CS0618
		mesh.CreateVertexBuffer( data.Length, layout.ToArray(), data.AsSpan() );
#pragma warning restore CS0618
		mesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() );
		mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 20, 20, 0 ) );
		var builder = Model.Builder.WithName( "gltf_native_skin_contract_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh );
		builder.AddBone( "joint_0", Vector3.Zero, Rotation.Identity );
		builder.AddBone( "joint_1", Vector3.Zero, Rotation.Identity );
		model = builder.Create();
		if ( model is null || !model.IsValid || model.IsError ) throw new InvalidOperationException( "Native procedural model failed." );
		var skin = readSkin.Invoke( null, new object[] { model, 0 } );
		if ( skin is null ) throw new InvalidOperationException( "Native model did not create render skin metadata." );
		var skinType = skin.GetType();
		rows.Add( new { floatingWeights, valid = true, bones = model.BoneCount, meshBones = skinType.GetProperty( "BoneCount", flags ).GetValue( skin ), weights = skinType.GetField( "BlendWeightCount", flags ).GetValue( skin ), masterBones = skinType.GetField( "MasterBones", flags ).GetValue( skin ), inverseBinds = skinType.GetField( "InverseBindPoses", flags ).GetValue( skin ) } );
	}
	catch ( Exception e ) { rows.Add( new { floatingWeights, valid = false, error = e.ToString() } ); }
	finally { if ( model is not null ) typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ); }
}
return System.Text.Json.JsonSerializer.Serialize( new { purpose = "Native procedural skin declaration contract, not glTF acceptance", rows }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true } );
