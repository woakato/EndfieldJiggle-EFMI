#include "deformation_field.hlsl"

Buffer<float4> Control : register(t119);
cbuffer NativeCamera : register(b0)
{
    float4 Native[82];
};

#ifndef CAMERA_INDEX
#define CAMERA_INDEX 44
#endif
#ifndef OFFSET_INDEX
#define OFFSET_INDEX 0
#endif

#ifdef SURFACE_FRAME
struct FrameOutput
{
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
#ifndef NORMAL_ONLY
    float3 tangent : TEXCOORD1;
#endif
};

FrameOutput main(float3 position : POSITION, float3 normal : NORMAL
#ifndef NORMAL_ONLY
    , float3 tangent : TANGENT
#endif
)
{
    FrameOutput output;
    output.position = float4(position, 1.0);
    output.normal = normal;
#ifdef NORMAL_ONLY
    float3 axis = abs(normal.x) < 0.8 ? float3(1, 0, 0) : float3(0, 1, 0);
    float3 tangent = normalize(cross(normal, axis));
#else
    output.tangent = tangent;
#endif
    if (Control[0].w > 0.5)
    {
        float3 world = position + Native[44].xyz;
        float3 delta = JF_EvaluateGrabField(world, Control[1].xyz,
            Control[0].xyz, Control[1].w, Control[2].w, Control[3].x);
        float3 bitangent = cross(normal, tangent);
        float3 newTangent;
        float3 discarded;
        JF_ReconstructSurfaceFrame(world, normal, tangent, bitangent,
            Control[1].xyz, Control[0].xyz, Control[1].w,
            Control[2].w, Control[3].x, 1.0, delta,
            output.normal, newTangent, discarded);
#ifndef NORMAL_ONLY
        output.tangent = newTangent;
#endif
    }
    return output;
}
#else
float4 main(float3 position : POSITION) : SV_Position
{
    float3 displaced = position;
    if (Control[0].w > 0.5)
        displaced += JF_EvaluateGrabField(position + Native[CAMERA_INDEX].xyz,
            Control[1].xyz, Control[OFFSET_INDEX].xyz,
            Control[1].w, Control[2].w, Control[3].x);
    return float4(displaced, 1.0);
}
#endif
