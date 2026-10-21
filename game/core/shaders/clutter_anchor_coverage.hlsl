#ifndef CLUTTER_ANCHOR_COVERAGE_HLSL
#define CLUTTER_ANCHOR_COVERAGE_HLSL
// Included inside the native cull CS after its visibility and LOD helpers.
int ClutterAnchorCoverageEnabled < Attribute("ClutterAnchorCoverageEnabled"); Default(0); >;
StructuredBuffer<uint> ClutterAnchorNeighbors < Attribute("ClutterAnchorNeighbors"); >;
float3 ClutterAnchorShape < Attribute("ClutterAnchorShape"); Default3(0,0,0); >;
float ClutterAnchorHeightScale < Attribute("ClutterAnchorHeightScale"); Default(1); >;
// Bounded diagnostic: 1 is the conservative margin, 0 tests approximate unpadded coverage.
float ClutterAnchorWindMarginScale < Attribute("ClutterAnchorWindMarginScale"); Default(1); >;
// Approximate far coverage: 0 preserves strict matching; 1 allows exposed tips and partial width overlap.
float ClutterAnchorApproximation < Attribute("ClutterAnchorApproximation"); Default(0); >;
float4x4 ClutterAnchorWorldToProjection < Attribute("ClutterAnchorWorldToProjection"); >;
float3 ClutterAnchorCameraForward < Attribute("ClutterAnchorCameraForward"); >;
float3 ClutterAnchorCameraRight < Attribute("ClutterAnchorCameraRight"); >;
float3 ClutterAnchorCameraUp < Attribute("ClutterAnchorCameraUp"); >;
Texture2D<float4> ClutterAnchorContactTexture < Attribute("ClutterAnchorContactTexture"); SrgbRead(false); >;
Texture2D<float4> ClutterAnchorPreviousContactTexture < Attribute("ClutterAnchorPreviousContactTexture"); SrgbRead(false); >;
SamplerState ClutterAnchorContactSampler < Filter(LINEAR); AddressU(CLAMP); AddressV(CLAMP); >;
float4 ClutterAnchorContactRegion < Attribute("ClutterAnchorContactRegion"); >;
float4 ClutterAnchorPreviousContactRegion < Attribute("ClutterAnchorPreviousContactRegion"); >;
int ClutterAnchorContactEnabled < Attribute("ClutterAnchorContactEnabled"); Default(0); >;
int ClutterAnchorPreviousContactEnabled < Attribute("ClutterAnchorPreviousContactEnabled"); Default(0); >;

bool ClutterAnchorContactClear(float2 root, Texture2D<float4> field, float4 region, int enabled)
{
    if (enabled == 0) return true;
    if (!all(isfinite(region)) || region.z <= 0.0) return false;
    float2 uv = (root-region.xy)*region.z;
    if (!all(isfinite(uv))) return false;
    if (any(uv < 0.0) || any(uv >= 1.0)) return true;
    float3 contact = field.SampleLevel(ClutterAnchorContactSampler,uv,0).xyz;
    // Explicit field-component tolerance, not a bound on final blade displacement.
    return all(isfinite(contact)) && all(abs(contact) <= 0.001);
}
bool ClutterAnchorRootClear(float3 root)
{
    return ClutterAnchorContactClear(root.xy,ClutterAnchorContactTexture,ClutterAnchorContactRegion,ClutterAnchorContactEnabled)
        && ClutterAnchorContactClear(root.xy,ClutterAnchorPreviousContactTexture,ClutterAnchorPreviousContactRegion,ClutterAnchorPreviousContactEnabled);
}
bool ClutterAnchorProject(float3 point,out float2 projected)
{
    float4 clip = mul(ClutterAnchorWorldToProjection,float4(point,1));
    projected = 0;
    if (!all(isfinite(clip)) || clip.w <= 0.00001) return false;
    projected = clip.xy / clip.w;
    return all(isfinite(projected));
}
// XYZ endpoint motion contracts the foreground estimate and expands the rear.
// The card is porous: this is a controlled coverage approximation, not occlusion proof.
bool ClutterAnchorRectangle(ClutterInstanceData t,bool foreground,out float4 rectangle,out float3 root,out float height)
{
    root = float3(t.Row0.w,t.Row1.w,t.Row2.w);
    float3 vertical = float3(t.Row0.z,t.Row1.z,t.Row2.z);
    float scale = length(vertical);
    height = ClutterAnchorShape.x*scale*ClutterAnchorHeightScale;
    rectangle = 0;
    if (!all(isfinite(root)) || !all(isfinite(vertical)) || !isfinite(height) || scale <= 0.00001 || height <= 0.00001) return false;
    float width = ClutterAnchorShape.y*scale;
    float3 top = root + vertical/scale*height;
    float3 centre = (root+top)*0.5;
    float uncertainty = 2.0*sqrt(height*height+width*width*0.25)*sin(ClutterAnchorShape.z*0.5)*ClutterAnchorWindMarginScale;
    float2 bottomScreen,topScreen,leftScreen,rightScreen,marginScreen,centreScreen;
    if (!ClutterAnchorProject(root,bottomScreen) || !ClutterAnchorProject(top,topScreen)
        || !ClutterAnchorProject(centre-ClutterAnchorCameraRight*width*0.5,leftScreen)
        || !ClutterAnchorProject(centre+ClutterAnchorCameraRight*width*0.5,rightScreen)
        || !ClutterAnchorProject(centre,centreScreen)
        || !ClutterAnchorProject(centre+(ClutterAnchorCameraRight+ClutterAnchorCameraUp)*uncertainty,marginScreen)) return false;
    float2 margin = abs(marginScreen-centreScreen);
    rectangle = float4(min(leftScreen.x,rightScreen.x),min(bottomScreen.y,topScreen.y),max(leftScreen.x,rightScreen.x),max(bottomScreen.y,topScreen.y));
    float direction = foreground ? 1.0 : -1.0;
    rectangle.xy += margin*direction;
    rectangle.zw -= margin*direction;
    return all(isfinite(rectangle)) && all(rectangle.zw > rectangle.xy);
}
float ClutterAnchorCoverage(uint id,ClutterInstanceData rear)
{
    if (!isfinite(ClutterAnchorApproximation) || ClutterAnchorApproximation < 0.0 || ClutterAnchorApproximation > 1.0
        || !isfinite(ClutterAnchorWindMarginScale) || ClutterAnchorWindMarginScale < 0.0 || ClutterAnchorWindMarginScale > 1.0
        || !isfinite(ClutterAnchorHeightScale) || ClutterAnchorHeightScale < 0.25 || ClutterAnchorHeightScale > 1.0
        || !all(isfinite(ClutterAnchorShape)) || any(ClutterAnchorShape.xy <= 0.0)
        || !all(isfinite(ClutterAnchorCameraForward)) || !all(isfinite(ClutterAnchorCameraRight)) || !all(isfinite(ClutterAnchorCameraUp))) return 1.0;
    float4 rearRect; float3 rearRoot; float rearHeight;
    if (!ClutterAnchorRectangle(rear,false,rearRect,rearRoot,rearHeight) || !ClutterAnchorRootClear(rearRoot)) return 1.0;
    float3x4 rearMatrix = float3x4(rear.Row0,rear.Row1,rear.Row2);
    float rearScale = max(length(float3(rear.Row0.x,rear.Row1.x,rear.Row2.x)),
        max(length(float3(rear.Row0.y,rear.Row1.y,rear.Row2.y)),length(float3(rear.Row0.z,rear.Row1.z,rear.Row2.z))));
    float threshold = ClutterLodSwitches.z*rearScale;
    if (!isfinite(threshold) || threshold <= 0.0) return 1.0;
    float farProgress = smoothstep(1.33,2.5,LodMetric(mul(rearMatrix,float4(ClutterModelBoundsCenter,1)))/threshold);
    float strongest = 0.0;
    [loop] for (uint neighbor=0;neighbor<8;neighbor++)
    {
        uint anchorId = ClutterAnchorNeighbors[id*8+neighbor];
        if (anchorId >= (uint)InstanceCount || anchorId == id) continue;
        float4 sphere = AllInstanceSpheres[anchorId];
        if (!all(isfinite(sphere)) || sphere.w < 0.0) continue;
        float scale = ClutterModelRadius > 0.000001 ? sphere.w/ClutterModelRadius : 1.0;
        float metric = LodMetric(sphere.xyz);
        if (BlendBoundary(metric,scale) >= 0 || ComputeLod(metric,scale) != 2) continue;
        float radius = sphere.w+ClutterVertexBoundsLocalMargins.z*scale+ClutterVertexBoundsWorldMargins.z;
        if (!SphereInFrustumMatrix(sphere.xyz,radius,ClutterAnchorWorldToProjection)) continue;
        if (ClutterDrawDistance > 0.0 && length(sphere.xyz-ClutterDrawCameraPosition)-radius >= ClutterDrawDistance) continue;
        ClutterInstanceData anchor = AllInstances[anchorId];
        if (!isfinite(anchor.Alpha) || anchor.Alpha < 0.999999) continue;
        float4 anchorRect; float3 anchorRoot; float anchorHeight;
        if (!ClutterAnchorRectangle(anchor,true,anchorRect,anchorRoot,anchorHeight)) continue;
        float depth = dot(rearRoot-anchorRoot,ClutterAnchorCameraForward);
        float limit = 2.0*anchorHeight;
        if (depth <= 0.0 || depth >= limit || !ClutterAnchorRootClear(anchorRoot)) continue;
        float2 rearSize = rearRect.zw-rearRect.xy;
        float overlap = max(0.0,min(anchorRect.z,rearRect.z)-max(anchorRect.x,rearRect.x))/rearSize.x;
        float exposedTop = max(0.0,rearRect.w-anchorRect.w)/rearSize.y;
        float exposedBase = max(0.0,anchorRect.y-rearRect.y)/rearSize.y;
        float approximation = ClutterAnchorApproximation;
        float confidence = smoothstep(lerp(0.8,0.35,approximation),lerp(0.95,0.65,approximation),overlap)
            * (1.0-smoothstep(lerp(0.02,0.2,approximation),lerp(0.2,0.6,approximation),exposedTop))
            * (1.0-smoothstep(lerp(0.0,0.1,approximation),lerp(0.1,0.4,approximation),exposedBase))
            * smoothstep(0.0,anchorHeight*0.1,depth)
            * (1.0-smoothstep(limit*0.8,limit,depth));
        float fullExclusion = lerp(0.6,0.25,farProgress);
        confidence = lerp(confidence,smoothstep(0.1,fullExclusion,confidence),approximation);
        strongest = max(strongest,confidence);
    }
    // Reuse the complete native blend protection and its smooth outer fade.
    float distanceFade = 1.0-ClutterLod2Coverage(float3x4(rear.Row0,rear.Row1,rear.Row2),ClutterModelBoundsCenter,
        ClutterLodCameraPos,ClutterLodTanHalfFov,ClutterLodViewportWidth,ClutterLodOrthoWidth,ClutterLodSwitches,
        ClutterLodBlendWidth,ClutterLodCount,0.0,float4(0,0,0,0));
    return saturate(1.0-strongest*distanceFade);
}
#endif
