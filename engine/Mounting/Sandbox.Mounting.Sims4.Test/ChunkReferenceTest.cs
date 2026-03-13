using Sims4Reader;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Sims4MountTest;

/// <summary>
/// Tests for ChunkReference resolution (Public/Private/Delayed) in RCOL containers.
/// </summary>
[TestClass]
public class ChunkReferenceTest
{
	[TestMethod]
	public void GetTgiIndex_NullReference_ReturnsMinusOne()
	{
		Assert.AreEqual( -1, ChunkReference.GetTgiIndex( 0x00000000 ) );
		Assert.AreEqual( -1, ChunkReference.GetTgiIndex( 0x30000000 ) ); // Delayed null
		Assert.AreEqual( -1, ChunkReference.GetTgiIndex( 0x10000000 ) ); // Private null
	}

	[TestMethod]
	public void GetTgiIndex_ValidReference_ReturnsZeroBased()
	{
		Assert.AreEqual( 0, ChunkReference.GetTgiIndex( 0x00000001 ) );
		Assert.AreEqual( 1, ChunkReference.GetTgiIndex( 0x00000002 ) );
		Assert.AreEqual( 4, ChunkReference.GetTgiIndex( 0x10000005 ) ); // Private, index 4
		Assert.AreEqual( 0, ChunkReference.GetTgiIndex( 0x30000001 ) ); // Delayed, index 0
	}

	[TestMethod]
	public void GetReferenceType_ReturnsCorrectType()
	{
		Assert.AreEqual( ChunkReferenceType.Public, ChunkReference.GetReferenceType( 0x00000002 ) );
		Assert.AreEqual( ChunkReferenceType.Private, ChunkReference.GetReferenceType( 0x10000005 ) );
		Assert.AreEqual( ChunkReferenceType.Delayed, ChunkReference.GetReferenceType( 0x30000001 ) );
	}

	[TestMethod]
	public void ResolveChunkIndex_Public_ReturnsDirectIndex()
	{
		Assert.AreEqual( 1, ChunkReference.ResolveChunkIndex( 0x00000002, 5 ) );
	}

	[TestMethod]
	public void ResolveChunkIndex_Private_AddsPublicChunksOffset()
	{
		// Private ref index=0 with publicChunks=2 → absolute index 2
		Assert.AreEqual( 2, ChunkReference.ResolveChunkIndex( 0x10000001, 2 ) );
		// Private ref index=4 with publicChunks=2 → absolute index 6
		Assert.AreEqual( 6, ChunkReference.ResolveChunkIndex( 0x10000005, 2 ) );
	}

	[TestMethod]
	public void ResolveChunkIndex_Delayed_ReturnsMinusOne()
	{
		Assert.AreEqual( -1, ChunkReference.ResolveChunkIndex( 0x30000001, 2 ) );
	}

	[TestMethod]
	public void IsDelayed_ReturnsTrueOnlyForDelayedType()
	{
		Assert.IsTrue( ChunkReference.IsDelayed( 0x30000001 ) );
		Assert.IsFalse( ChunkReference.IsDelayed( 0x00000002 ) );
		Assert.IsFalse( ChunkReference.IsDelayed( 0x10000001 ) );
	}

	[TestMethod]
	public void ModlRcol_LodReferences_ResolveToCorrectChunkTypes()
	{
		var path = TestHelper.GetPackagePath();
		using var pkg = DbpfPackage.Open( path );

		int totalLods = 0;
		int resolvedToMlod = 0;
		int delayedLods = 0;

		foreach ( var entry in pkg.FindAll( ResourceType.Model ).Take( 50 ) )
		{
			try
			{
				var rcol = pkg.GetResource<RcolContainer>( entry );
				var modl = rcol.GetChunk<ModlChunk>();
				if ( modl == null ) continue;

				foreach ( var lod in modl.LodEntries )
				{
					totalLods++;
					uint mlodRef = lod.MlodChunkRef;

					if ( ChunkReference.IsDelayed( mlodRef ) )
					{
						delayedLods++;
						int extIdx = ChunkReference.GetTgiIndex( mlodRef );
						if ( extIdx >= 0 && extIdx < rcol.ExternalReferences.Length )
						{
							var extKey = rcol.ExternalReferences[extIdx];
							var extEntry = pkg.Find( extKey );
							if ( extEntry != null )
							{
								var extRcol = pkg.GetResource<RcolContainer>( extEntry.Value );
								if ( extRcol.GetChunk<MeshLod>() != null )
									resolvedToMlod++;
							}
						}
					}
					else
					{
						int absIdx = ChunkReference.ResolveChunkIndex( mlodRef, rcol.PublicChunks );
						if ( absIdx >= 0 && absIdx < rcol.ChunkEntries.Count )
						{
							if ( rcol.ChunkEntries[absIdx].Chunk is MeshLod )
								resolvedToMlod++;
						}
					}
				}
			}
			catch { }
		}

		Console.WriteLine( $"LODs: {totalLods}, resolved to MLOD: {resolvedToMlod}, delayed: {delayedLods}" );
		Assert.IsTrue( totalLods > 0, "No LOD entries found." );
		Assert.IsTrue( resolvedToMlod > 0, "No LOD references resolved to MLOD chunks." );
	}
}
