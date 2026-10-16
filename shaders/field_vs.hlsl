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
    float3 tangent = JF_SafeNormalize(cross(normal, axis), float3(0, 0, 1));
#else
    output.tangent = tangent;
#endif
    if (Control[0].w > 0.5 && any(Control[0].xyz != 0.0))
    {
        // Differentiate in anchor-local space: world-space millimeter samples
        // collapse to the same float at large camera coordinates.
        float3 relative = position - (Control[1].xyz - Native[CAMERA_INDEX].xyz);
        float radius = max(Control[1].w, 1e-6);
        if (dot(relative, relative) < radius * radius)
        {
            float3 delta = JF_EvaluateGrabField(relative, 0.0,
                Control[0].xyz, Control[1].w, Control[2].w, Control[3].x);
            float3 bitangent = cross(normal, tangent);
            float3 newTangent;
            float3 discarded;
            JF_ReconstructSurfaceFrame(relative, normal, tangent, bitangent,
                0.0, Control[0].xyz, Control[1].w,
                Control[2].w, Control[3].x, 1.0, delta,
                output.normal, newTangent, discarded);
#ifndef NORMAL_ONLY
            output.tangent = newTangent;
#endif
        }
    }
    return output;
}
#else
float4 main(float3 position : POSITION) : SV_Position
{
    float3 displaced = position;
    if (Control[0].w > 0.5 && any(Control[OFFSET_INDEX].xyz != 0.0))
    {
        float3 relative = position - (Control[1].xyz - Native[CAMERA_INDEX].xyz);
        displaced += JF_EvaluateGrabField(relative, 0.0, Control[OFFSET_INDEX].xyz,
            Control[1].w, Control[2].w, Control[3].x);
    }
    return float4(displaced, 1.0);
}
#endif
