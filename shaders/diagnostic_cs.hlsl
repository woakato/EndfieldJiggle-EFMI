Buffer<float4> State : register(t0);
Buffer<float4> Control : register(t1);
Texture2D<float4> PickWorld : register(t2);
Texture2D<float4> PickNormal : register(t3);
Buffer<float4> Capture : register(t4);
Texture1D<float4> IniParams : register(t120);
RWBuffer<float4> Diagnostic : register(u0);

// Read-only telemetry; original interaction resources are never written.
[numthreads(1, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    Diagnostic[0] = IniParams[244]; // native/color draws, session complete, drag
    Diagnostic[1] = IniParams[245]; // cursor pixels, window width/height
    Diagnostic[2] = PickWorld.Load(int3(0, 0, 0)); // world XYZ, hit marker
    Diagnostic[3] = State[9]; // reserved, pick valid, motion active, was held
    Diagnostic[4] = Control[0]; // offset XYZ, active marker
    Diagnostic[5] = State[7]; // input-controller state, previous held input
    Diagnostic[6] = IniParams[246]; // enabled, cancelled, picker started, DPI
    Diagnostic[7] = PickNormal.Load(int3(0, 0, 0)); // normal XYZ, depth
    Diagnostic[8] = Capture[0]; // captured-pick validity and generation
    Diagnostic[9] = IniParams[247]; // radius, max offset, Y flip, time
    Diagnostic[10] = float4(1014, 2, 1, 0); // date/revision/success marker
}
