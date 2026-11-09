HEADER
{
	Description = "Research glTF descriptor driven deformation";
}
MODES
{
	Default();
}
COMMON
{
	#include "system.fxc"
	#include "common.fxc"
}
CS
{
	struct InputDescriptor
	{
		uint ByteOffset; uint ByteStride; uint ComponentType; uint Components;
		uint Normalized; uint Role; uint SemanticIndex; uint Target;
	};
	struct OutputDescriptor
	{
		uint Source; uint ByteOffset; uint Components; uint Role;
		uint SemanticIndex; uint Padding0; uint Padding1; uint Padding2;
	};
	struct MatrixRows { float4 Row0; float4 Row1; float4 Row2; float4 Row3; };
	ByteAddressBuffer g_InputBytes < Attribute("InputBytes"); >;
	StructuredBuffer<InputDescriptor> g_Inputs < Attribute("InputDescriptors"); >;
	StructuredBuffer<OutputDescriptor> g_Outputs < Attribute("OutputDescriptors"); >;
	StructuredBuffer<uint> g_SourceVertices < Attribute("SourceVertices"); >;
	StructuredBuffer<MatrixRows> g_Palette < Attribute("Palette"); >;
	StructuredBuffer<MatrixRows> g_Placement < Attribute("Placement"); >;
	StructuredBuffer<float> g_Weights < Attribute("MorphWeights"); >;
	RWByteAddressBuffer g_OutputBytes < Attribute("OutputBytes"); >;
	uint g_VertexCount < Attribute("VertexCount"); >;
	uint g_ReverseWinding < Attribute("ReverseWinding"); >;
	uint g_VertexStride < Attribute("VertexStride"); >;
	uint g_InputCount < Attribute("InputCount"); >;
	uint g_BaseAttributeCount < Attribute("BaseAttributeCount"); >;
	uint g_OutputCount < Attribute("OutputCount"); >;
	uint g_JointCount < Attribute("JointCount"); >;
	uint g_GenerateFlatNormals < Attribute("GenerateFlatNormals"); >;

	float Component(InputDescriptor d, uint vertex, uint component)
	{
		uint size = (d.ComponentType == 5120 || d.ComponentType == 5121) ? 1 : (d.ComponentType == 5122 || d.ComponentType == 5123) ? 2 : 4;
		uint offset = d.ByteOffset + vertex * d.ByteStride + component * size;
		uint word = g_InputBytes.Load(offset & ~3u);
		uint bits = word >> ((offset & 3u) * 8u);
		if (d.ComponentType == 5126) return asfloat(word);
		if (d.ComponentType == 5125) return float(word);
		if (d.ComponentType == 5121) return d.Normalized != 0 ? float(bits & 255u) / 255.0 : float(bits & 255u);
		if (d.ComponentType == 5123) return d.Normalized != 0 ? float(bits & 65535u) / 65535.0 : float(bits & 65535u);
		if (d.ComponentType == 5120)
		{
			float value = float(int(bits << 24u) >> 24);
			return d.Normalized != 0 ? max(value / 127.0, -1.0) : value;
		}
		float value = float(int(bits << 16u) >> 16);
		return d.Normalized != 0 ? max(value / 32767.0, -1.0) : value;
	}
	float4 Read(uint descriptor, uint vertex)
	{
		InputDescriptor d = g_Inputs[descriptor]; float4 value = 0;
		for (uint c = 0; c < min(d.Components,4u); ++c) value[c] = Component(d,vertex,c);
		return value;
	}
	uint Find(uint role, uint semantic)
	{
		for (uint i = 0; i < g_BaseAttributeCount; ++i) if (g_Inputs[i].Role == role && g_Inputs[i].SemanticIndex == semantic) return i;
		return 0xffffffffu;
	}
	// For generated per-corner base tangents, baseVertex is outputVertex while
	// morph deltas still use the original source vertex. All other fields use
	// the same source vertex for both; Padding0 == 0 preserves the old ABI.
	float4 MorphedWithBaseVertex(uint role, uint semantic, uint source, uint baseVertex, uint vertex, float4 fallback)
	{
		float4 value = source == 0xffffffffu ? fallback : Read(source,baseVertex);
		if (role == 3 && source != 0xffffffffu && g_Inputs[source].Components == 3) value.w = 1;
		for (uint i = g_BaseAttributeCount; i < g_InputCount; ++i)
		{
			InputDescriptor d = g_Inputs[i];
			if (d.Role == role && d.SemanticIndex == semantic) value += Read(i,vertex) * g_Weights[d.Target];
		}
		return value;
	}
	float4 Morphed(uint role, uint semantic, uint source, uint vertex, float4 fallback)
	{
		return MorphedWithBaseVertex(role,semantic,source,vertex,vertex,fallback);
	}
	float3 LocalPosition(uint vertex) { return Morphed(0,0,Find(0,0),vertex,0).xyz; }
	MatrixRows Transform(uint vertex)
	{
		if (g_JointCount == 0) return g_Palette[0];
		MatrixRows matrix; matrix.Row0=0; matrix.Row1=0; matrix.Row2=0; matrix.Row3=0;
		for (uint i = 0; i < g_BaseAttributeCount; ++i)
		{
			InputDescriptor weights = g_Inputs[i]; if (weights.Role != 6) continue;
			uint joints = Find(5, weights.SemanticIndex);
			float4 joint = Read(joints,vertex); float4 weight = Read(i,vertex);
			for (uint c = 0; c < 4; ++c)
			{
				if (weight[c] == 0) continue;
				// Indices and nonnegative finite weights were validated from the
				// identical raw descriptors before creating this instance.
				uint index = uint(joint[c]); if (index >= g_JointCount) continue;
				MatrixRows palette = g_Palette[index];
				matrix.Row0 += palette.Row0 * weight[c]; matrix.Row1 += palette.Row1 * weight[c];
				matrix.Row2 += palette.Row2 * weight[c]; matrix.Row3 += palette.Row3 * weight[c];
			}
		}
		return matrix;
	}
	float3 Position(float3 p, MatrixRows m) { return p.x*m.Row0.xyz+p.y*m.Row1.xyz+p.z*m.Row2.xyz+m.Row3.xyz; }
	float3 Direction(float3 v, MatrixRows m) { return v.x*m.Row0.xyz+v.y*m.Row1.xyz+v.z*m.Row2.xyz; }
	MatrixRows LinearComposition(MatrixRows a, MatrixRows b)
	{
		MatrixRows result; result.Row0=float4(Direction(a.Row0.xyz,b),0); result.Row1=float4(Direction(a.Row1.xyz,b),0);
		result.Row2=float4(Direction(a.Row2.xyz,b),0); result.Row3=0; return result;
	}
	float Maximum(float3 v) { return max(abs(v.x),max(abs(v.y),abs(v.z))); }
	float3 Unit(float3 value, float3 fallback)
	{
		float maximum = Maximum(value); if (maximum == 0 || !isfinite(maximum)) return fallback;
		float3 scaled = value / maximum; float lengthSquared = dot(scaled,scaled);
		return lengthSquared > 0 && isfinite(lengthSquared) ? scaled * rsqrt(lengthSquared) : fallback;
	}
	float3 Perpendicular(float3 normal)
	{
		return Unit(cross(normal,abs(normal.x)<.7 ? float3(1,0,0) : float3(0,1,0)),float3(0,1,0));
	}
	float3 Normal(float3 normal, MatrixRows matrix, out float handedness)
	{
		// Scale before cofactors: near-zero but invertible authored scales must
		// not be mistaken for singular matrices by determinant underflow.
		float maximum=max(Maximum(matrix.Row0.xyz),max(Maximum(matrix.Row1.xyz),Maximum(matrix.Row2.xyz)));
		if (maximum==0 || !isfinite(maximum)) { handedness=1; return float3(1,0,0); }
		float3 a=matrix.Row0.xyz/maximum, b=matrix.Row1.xyz/maximum, c=matrix.Row2.xyz/maximum;
		float3 ca=cross(b,c), cb=cross(c,a), cc=cross(a,b); float determinant=dot(a,ca);
		handedness=determinant<0 ? -1 : 1;
		float3 cofactor=normal.x*ca+normal.y*cb+normal.z*cc;
		// Exact singularity has no unique inverse-transpose normal. Choose a
		// deterministic finite direction; positions still collapse exactly.
		return Unit(cofactor*handedness,Unit(Direction(normal,matrix),float3(1,0,0)));
	}
	[numthreads(64,1,1)]
	void MainCs(uint3 id : SV_DispatchThreadID)
	{
		uint outputVertex=id.x; if (outputVertex>=g_VertexCount) return;
		uint vertex=g_SourceVertices[outputVertex]; MatrixRows matrix=Transform(vertex); MatrixRows placement=g_Placement[0];
		MatrixRows linear=LinearComposition(matrix,placement);
		float3 localNormal=Morphed(1,0,Find(1,0),vertex,float4(0,0,1,0)).xyz;
		if (g_GenerateFlatNormals!=0)
		{
			uint corner=(outputVertex/3u)*3u;
			float3 a=LocalPosition(g_SourceVertices[corner]);
			float3 b=LocalPosition(g_SourceVertices[corner+1u]);
			float3 c=LocalPosition(g_SourceVertices[corner+2u]);
			localNormal=Unit(cross(Unit(b-a,float3(1,0,0)),Unit(c-a,float3(0,1,0))),float3(0,0,1));
		}
		float handedness; float3 worldNormal=Normal(localNormal,linear,handedness);
		if (g_GenerateFlatNormals!=0)
		{
			// Each corner can have different joint weights. A single bind-space
			// normal transformed per vertex is not the normal of the deformed face.
			uint corner=(outputVertex/3u)*3u;
			uint ia=g_SourceVertices[corner], ib=g_SourceVertices[corner+1u], ic=g_SourceVertices[corner+2u];
			float3 a=Position(Position(LocalPosition(ia),Transform(ia)),placement);
			float3 b=Position(Position(LocalPosition(ib),Transform(ib)),placement);
			float3 c=Position(Position(LocalPosition(ic),Transform(ic)),placement);
			float3 face=cross(Unit(b-a,float3(1,0,0)),Unit(c-a,float3(0,1,0)));
			// The bound index buffer swaps corners 1 and 2 when this flag is set.
			if (g_ReverseWinding!=0) face=-face;
			worldNormal=Unit(face,worldNormal);
		}
		for (uint i=0; i<g_OutputCount; ++i)
		{
			OutputDescriptor field=g_Outputs[i]; float4 value;
			if (field.Role==0) value=float4(Position(Position(LocalPosition(vertex),matrix),placement),1);
			else if (field.Role==1) value=float4(worldNormal,0);
			else if (field.Role==2)
			{
				// Missing tangent directions must be constructed perpendicular to the
				// deformed normal, never from an arbitrary possibly parallel axis.
				// Also ignore authored tangents when base normals are absent; retain
				// generated corner frames regardless of source-normal availability.
				if ((Find(1,0)==0xffffffffu || field.Source==0xffffffffu) && field.Padding0!=1u) value=float4(Perpendicular(worldNormal),handedness);
				else
				{
					value=MorphedWithBaseVertex(2,0,field.Source,field.Padding0 == 1u ? outputVertex : vertex,vertex,float4(1,0,0,1));
					float3 tangent=Direction(value.xyz,linear); tangent-=worldNormal*dot(tangent,worldNormal);
					value=float4(Unit(tangent,Perpendicular(worldNormal)),value.w*handedness);
				}
			}
			else value=Morphed(field.Role,field.SemanticIndex,field.Source,vertex,field.Role==3 ? float4(1,1,1,1) : 0);
			if (field.Role==3) value=saturate(value);
			uint offset=outputVertex*g_VertexStride+field.ByteOffset;
			for (uint component=0; component<field.Components; ++component) g_OutputBytes.Store(offset+component*4u,asuint(value[component]));
		}
	}
}
