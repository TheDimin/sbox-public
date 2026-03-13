using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Sims4MountTest;

[TestClass]
public class DiagnosticTypeTest
{
	[TestMethod]
	public void Diagnostic_ChunkReferenceResolution_MatchesChunkTypes()
	{
		var path = TestHelper.GetPackagePath();
		using var pkg = DbpfPackage.Open( path );

		var modlEntries = pkg.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		int totalMeshes = 0;
		int resolvedMatd = 0;
		int failedMatd = 0;
		int totalLods = 0;
		int resolvedLods = 0;

		foreach ( var entry in modlEntries )
		{
			try
			{
				var rcol = pkg.GetResource<RcolContainer>( entry );
				var modl = rcol.GetChunk<ModlChunk>();
				if ( modl == null ) continue;

				Console.WriteLine( $"\nMODL 0x{entry.Key.Instance:X16} (PublicChunks={rcol.PublicChunks})" );
				for ( int i = 0; i < rcol.ChunkEntries.Count; i++ )
				{
					var ce = rcol.ChunkEntries[i];
					string marker = i < rcol.PublicChunks ? "PUB" : "PRI";
					Console.WriteLine( $"  [{i}] {marker} {ce.Chunk.GetType().Name}" );
				}

				foreach ( var lodEntry in modl.LodEntries )
				{
					totalLods++;
					uint mlodRef = lodEntry.MlodChunkRef;
					var refType = ChunkReference.GetReferenceType( mlodRef );
					int localIdx = ChunkReference.GetTgiIndex( mlodRef );
					bool isDelayed = ChunkReference.IsDelayed( mlodRef );

					Console.Write( $"  LOD {lodEntry.Id}: ref=0x{mlodRef:X8} type={refType} idx={localIdx}" );

					if ( isDelayed )
					{
						Console.Write( " → Delayed" );
						if ( localIdx >= 0 && localIdx < rcol.ExternalReferences.Length )
						{
							var extKey = rcol.ExternalReferences[localIdx];
							var extEntry = pkg.Find( extKey );
							Console.Write( $" ext=0x{(uint)extKey.Type:X8} found={extEntry != null}" );
							if ( extEntry != null )
							{
								var extRcol = pkg.GetResource<RcolContainer>( extEntry.Value );
								var mlod = extRcol.GetChunk<MeshLod>();
								if ( mlod != null )
								{
									resolvedLods++;
									Console.Write( $" → MLOD ({mlod.Meshes.Count} meshes)" );
								}
							}
						}
					}
					else
					{
						int absIdx = ChunkReference.ResolveChunkIndex( mlodRef, rcol.PublicChunks );
						Console.Write( $" → abs={absIdx}" );
						if ( absIdx >= 0 && absIdx < rcol.ChunkEntries.Count )
						{
							var chunk = rcol.ChunkEntries[absIdx].Chunk;
							Console.Write( $" → {chunk.GetType().Name}" );
							if ( chunk is MeshLod mlod )
							{
								resolvedLods++;
								Console.Write( $" ({mlod.Meshes.Count} meshes)" );
							}
						}
					}
					Console.WriteLine();
				}

				// Try full model loading
				var resolved = ModlModelLoader.LoadModel( pkg, entry );
				foreach ( var lod in resolved.Lods )
				{
					foreach ( var mesh in lod.Meshes )
					{
						totalMeshes++;
						if ( mesh.Material != null )
						{
							resolvedMatd++;
							Console.WriteLine( $"    {lod.LodId} mesh 0x{mesh.NameHash:X8}: MATD resolved ({mesh.TextureKeys.Count} textures)" );
						}
						else
						{
							failedMatd++;
							Console.WriteLine( $"    {lod.LodId} mesh 0x{mesh.NameHash:X8}: NO MATD" );
						}
					}
				}
			}
			catch ( Exception ex )
			{
				Console.WriteLine( $"  ERROR: {ex.Message}" );
			}
		}

		Console.WriteLine( $"\n=== Summary ===" );
		Console.WriteLine( $"Total LODs: {totalLods}, Resolved: {resolvedLods}" );
		Console.WriteLine( $"Total meshes: {totalMeshes}" );
		Console.WriteLine( $"MATD resolved: {resolvedMatd}" );
		Console.WriteLine( $"MATD failed: {failedMatd}" );
		if ( totalMeshes > 0 )
			Console.WriteLine( $"Resolution rate: {resolvedMatd * 100 / totalMeshes}%" );

		Assert.IsTrue( totalMeshes > 0, "Should have resolved at least some meshes" );
	}
}
