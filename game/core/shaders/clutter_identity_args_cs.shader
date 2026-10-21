HEADER
{
	DevShader = true;
	Description = "Clutter source identity indirect argument update";
}
MODES
{
	Default();
}
FEATURES
{
}
COMMON
{
	#include "system.fxc"
	#include "common.fxc"
}
CS
{
	struct DrawArguments
	{
		uint indexCount;
		uint instanceCount;
		uint firstIndex;
		int baseVertex;
		uint firstInstance;
	};
	RWStructuredBuffer<uint> ClutterSourceIdentityCounts < Attribute( "ClutterSourceIdentityCounts" ); >;
	RWStructuredBuffer<DrawArguments> ClutterIdentityDrawArgs < Attribute( "ClutterIdentityDrawArgs" ); >;
	int ClutterIdentityArgsLod < Attribute( "ClutterIdentityArgsLod" ); >;
	int ClutterIdentityDrawCount < Attribute( "ClutterIdentityDrawCount" ); >;

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 threadId : SV_DispatchThreadID )
	{
		if ( threadId.x >= (uint)ClutterIdentityDrawCount ) return;
		// Every material range consumes the same paired transform/source-ID bucket.
		ClutterIdentityDrawArgs[threadId.x].instanceCount = ClutterSourceIdentityCounts[ClutterIdentityArgsLod];
	}
}
