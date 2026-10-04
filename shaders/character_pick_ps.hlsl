struct NativePick
{
    float4 position : SV_Position;
    float3 world : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float3 right : TEXCOORD2;
    float3 up : TEXCOORD3;
};

struct PickOutput
{
    float4 world : SV_Target0;
    float4 normal : SV_Target1;
    float4 right : SV_Target2;
    float4 up : SV_Target3;
};

PickOutput main(NativePick input)
{
    PickOutput output;
    output.world = float4(input.world, 1);
    output.normal = float4(input.normal, input.position.z);
    output.right = float4(input.right, 0);
    output.up = float4(input.up, 0);
    return output;
}
