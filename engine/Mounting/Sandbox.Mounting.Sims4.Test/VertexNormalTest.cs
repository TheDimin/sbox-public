using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

[TestClass]
public class VertexNormalTest
{
	[TestMethod]
	public void ResolvedMeshes_HaveNonZeroNormals()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int totalMeshes = 0;
		int meshesWithNormals = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 100 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length == 0 ) continue;
					totalMeshes++;

					bool hasNormal = false;
					for ( int i = 0; i < Math.Min( mesh.Vertices.Length, 10 ); i++ )
					{
						var n = mesh.Vertices[i].Normal;
						if ( n != null && n.Length >= 3 )
						{
							float len = MathF.Sqrt( n[0] * n[0] + n[1] * n[1] + n[2] * n[2] );
							if ( len > 0.01f ) { hasNormal = true; break; }
						}
					}

					if ( hasNormal ) meshesWithNormals++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Meshes with normals: {meshesWithNormals}/{totalMeshes}" );
		Assert.IsTrue( meshesWithNormals > 0,
			$"Expected some meshes to have non-zero normals ({totalMeshes} total meshes)." );
	}

	[TestMethod]
	public void Vertex_NormalValues_AreSane()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length == 0 ) continue;

					int withNormal = 0;
					int nullNormal = 0;
					int zeroNormal = 0;

					for ( int i = 0; i < Math.Min( mesh.Vertices.Length, 20 ); i++ )
					{
						var n = mesh.Vertices[i].Normal;
						if ( n == null ) { nullNormal++; continue; }
						if ( n.Length < 3 ) { nullNormal++; continue; }

						float len = MathF.Sqrt( n[0] * n[0] + n[1] * n[1] + n[2] * n[2] );
						if ( len < 0.01f ) { zeroNormal++; continue; }

						withNormal++;
					}

					if ( withNormal > 0 )
					{
						Console.WriteLine( $"MODL {entry.Key}: {withNormal} valid normals" );
						Assert.IsTrue( withNormal > 0 );
						return;
					}
				}
			}
			catch { }
		}

		Assert.Fail( "No mesh with non-zero normals found in 200 MODLs." );
	}

	[TestMethod]
	public void Vertex_RawNormalData_Diagnostic()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 5 ) )
		{
			try
			{
				var rcol = package.GetResource<RcolContainer>( entry );
				var modl = rcol.GetChunk<ModlChunk>();
				if ( modl == null || modl.LodEntries.Count == 0 ) continue;

				var lodEntry = modl.LodEntries[0];
				int mlodIdx = lodEntry.MlodIndex;
				if ( mlodIdx < 0 ) continue;

				MeshLod? mlod = null;
				RcolContainer mlodRcol = rcol;
				int mlodChunkIndex = -1;

				if ( mlodIdx < rcol.ChunkEntries.Count )
				{
					mlod = rcol.ChunkEntries[mlodIdx].Chunk as MeshLod;
					if ( mlod != null ) mlodChunkIndex = mlodIdx;
				}

				if ( mlod == null )
				{
					int extIdx = mlodIdx - rcol.ChunkEntries.Count;
					if ( extIdx >= 0 && extIdx < rcol.ExternalReferences.Length )
					{
						var extKey = rcol.ExternalReferences[extIdx];
						var extEntry = package.Find( extKey );
						if ( extEntry != null )
						{
							mlodRcol = package.GetResource<RcolContainer>( extEntry.Value );
							mlod = mlodRcol.GetChunk<MeshLod>();
							if ( mlod != null )
							{
								for ( int ci = 0; ci < mlodRcol.ChunkEntries.Count; ci++ )
								{
									if ( mlodRcol.ChunkEntries[ci].Chunk == mlod )
									{
										mlodChunkIndex = ci;
										break;
									}
								}
							}
						}
					}
				}

				if ( mlod == null || mlod.Meshes.Count == 0 ) continue;
				var lodMesh = mlod.Meshes[0];
				int meshIndexOffset = mlodChunkIndex + 1;

				int vrtfIdx = lodMesh.VertexFormatIndex >= 0 ? lodMesh.VertexFormatIndex + meshIndexOffset : -1;
				int vbufIdx = lodMesh.VertexBufferIndex >= 0 ? lodMesh.VertexBufferIndex + meshIndexOffset : -1;

				// Resolve VRTF
				VertexFormat? vrtf = null;
				if ( vrtfIdx >= 0 && vrtfIdx < mlodRcol.ChunkEntries.Count )
					vrtf = mlodRcol.ChunkEntries[vrtfIdx].Chunk as VertexFormat;
				if ( vrtf == null )
				{
					int extIdx = vrtfIdx - mlodRcol.ChunkEntries.Count;
					if ( extIdx >= 0 && extIdx < mlodRcol.ExternalReferences.Length )
					{
						var extKey = mlodRcol.ExternalReferences[extIdx];
						var extEntry = package.Find( extKey );
						if ( extEntry != null )
						{
							var extRcol = package.GetResource<RcolContainer>( extEntry.Value );
							vrtf = extRcol.GetChunk<VertexFormat>();
						}
					}
				}

				if ( vrtf == null )
				{
					Console.WriteLine( $"MODL {entry.Key}: VRTF not found (vrtfIdx={vrtfIdx})" );
					continue;
				}

				Console.WriteLine( $"MODL {entry.Key}: Stride={vrtf.Stride}, Elements={vrtf.Elements.Count}" );
				foreach ( var el in vrtf.Elements )
					Console.WriteLine( $"  {el.Usage} idx={el.UsageIndex} fmt={el.Format} off={el.Offset}" );

				// Resolve VBUF
				VertexBuffer? vbuf = null;
				if ( vbufIdx >= 0 && vbufIdx < mlodRcol.ChunkEntries.Count )
					vbuf = mlodRcol.ChunkEntries[vbufIdx].Chunk as VertexBuffer;

				if ( vbuf != null && lodMesh.VertexCount > 0 )
				{
					// Read raw bytes at the normal offset for first vertex
					var normEl = vrtf.Elements.FirstOrDefault( x => x.Usage == ElementUsage.Normal );
					if ( normEl.Format != 0 || normEl.Usage == ElementUsage.Normal )
					{
						int byteSize = VertexFormat.ByteSizeFromFormat( normEl.Format );
						long vertStart = lodMesh.StreamOffset;
						Console.WriteLine( $"  Normal at offset {normEl.Offset}, format {normEl.Format}, byteSize {byteSize}" );
						Console.WriteLine( $"  VBUF buffer length: {vbuf.Buffer.Length}, StreamOffset: {vertStart}" );

						for ( int vi = 0; vi < Math.Min( 3, lodMesh.VertexCount ); vi++ )
						{
							long pos = vertStart + vi * vrtf.Stride + normEl.Offset;
							if ( pos + byteSize <= vbuf.Buffer.Length )
							{
								var rawBytes = new byte[byteSize];
								Array.Copy( vbuf.Buffer, pos, rawBytes, 0, byteSize );
								Console.WriteLine( $"  Vertex[{vi}] normal raw: {BitConverter.ToString( rawBytes )}" );
							}
						}
					}

					// Also decode vertex and show result
					var verts = vbuf.GetVertices( vrtf, lodMesh.StreamOffset, Math.Min( 3, lodMesh.VertexCount ) );
					for ( int vi = 0; vi < verts.Length; vi++ )
					{
						var n = verts[vi].Normal;
						var p = verts[vi].Position;
						Console.WriteLine( $"  Vertex[{vi}] pos=[{(p != null ? string.Join(",", p.Select( x => x.ToString( "F3" ) )) : "null")}] norm=[{(n != null ? string.Join(",", n.Select( x => x.ToString( "F3" ) )) : "null")}]" );
					}
				}

				return; // Just diagnose one MODL
			}
			catch ( Exception e )
			{
				Console.WriteLine( $"Exception: {e.Message}" );
			}
		}
	}
}
