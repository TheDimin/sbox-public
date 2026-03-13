using Sims4Reader;
using Sims4Reader.Image;

namespace Sims4MountTest;

[TestClass]
public class ImageResourceTest
{
	[TestMethod]
	public void DstImage_ParsesAndConverts()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.DstImage ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No DST image resources found in package." );

		var dst = package.GetResource<DstImage>( entry );
		Assert.IsTrue( dst.Width > 0, "Expected DST image width > 0." );
		Assert.IsTrue( dst.Height > 0, "Expected DST image height > 0." );

		var dds = dst.ToDds();
		Assert.IsNotNull( dds, "Expected ToDds() to return non-null." );
		Assert.IsTrue( dds.Length > 128, "Expected DDS output to be larger than the 128-byte header." );

		// DDS magic bytes: "DDS " = 0x20534444
		uint magic = BitConverter.ToUInt32( dds, 0 );
		Assert.AreEqual( 0x20534444u, magic, "Expected DDS magic bytes at start of output." );
	}

	[TestMethod]
	public void RleImage_ParsesAndConverts()
	{
		foreach ( var packagePath in TestHelper.GetAllPackagePaths() )
		{
			using var package = DbpfPackage.Open( packagePath );

			var entry = package.FindAll( ResourceType.RleImage ).FirstOrDefault();
			if ( entry.Key.Type == ResourceType.Unknown )
				entry = package.FindAll( ResourceType.RleImageAlt ).FirstOrDefault();
			if ( entry.Key.Type == ResourceType.Unknown )
				continue;

			var rle = package.GetResource<RleImage>( entry );
			Assert.IsTrue( rle.Width > 0, "Expected RLE image width > 0." );
			Assert.IsTrue( rle.Height > 0, "Expected RLE image height > 0." );

			var dds = rle.ToDds();
			Assert.IsNotNull( dds, "Expected ToDds() to return non-null." );
			Assert.IsTrue( dds.Length > 128, "Expected DDS output to be larger than the 128-byte header." );

			uint magic = BitConverter.ToUInt32( dds, 0 );
			Assert.AreEqual( 0x20534444u, magic, "Expected DDS magic bytes at start of output." );
			return;
		}

		Assert.Fail( "No RLE image resources found in any package." );
	}

	[TestMethod]
	public void DstImage_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.DstImage ).Take( 5 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No DST image resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var dst = package.GetResource<DstImage>( entry );
			var dds = dst.ToDds();
			if ( dds != null && dds.Length > 0 )
				successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one DST image should convert to DDS." );
	}

	[TestMethod]
	public void ImageEntries_AreDetectedByIsImage()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int imageCount = 0;
		foreach ( var entry in package.Entries )
		{
			if ( entry.Key.Type.IsImage() )
				imageCount++;
		}

		Assert.IsTrue( imageCount > 0, "Expected at least one image entry detected by IsImage()." );
	}

	[TestMethod]
	public void GetBytes_ImageEntry_ReturnsNonEmptyData()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var imageEntry = package.Entries.FirstOrDefault( e => e.Key.Type.IsImage() );
		if ( imageEntry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No image entries found in package." );

		var bytes = package.GetBytes( imageEntry );
		Assert.IsTrue( bytes.Length > 0, "Expected image entry data to be non-empty." );
	}
}
