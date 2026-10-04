#include "input_controller.hlsl"

Texture2D<float4> PickWorld : register(t0);
Texture2D<float4> PickNormal : register(t1);
Texture2D<float4> PickRight : register(t2);
Texture2D<float4> PickUp : register(t3);
Texture1D<float4> IniParams : register(t120);
RWBuffer<float4> State : register(u0);
RWBuffer<float4> Capture : register(u1);
RWBuffer<float4> Control : register(u2);

// State: seven motion records, two controller records, one input-age record.
[numthreads(1, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id != 0))
        return;

    float4 cursor = IniParams[240];
    float4 frame = IniParams[241];
    float4 tuning = IniParams[242];
    bool enabled = frame.w > 0.5 && JF_IsFinite2(cursor.zw)
        && all(cursor.zw > 0.0);
    bool seen = IniParams[243].x > 0.5;
    // Leaving the captured mesh or switching menus cancels an old grab.
    if (!enabled || !seen)
    {
        for (uint i = 0; i < 10; ++i)
            State[i] = 0.0;
        for (uint j = 0; j < 9; ++j)
            Capture[j] = 0.0;
        for (uint k = 0; k < 4; ++k)
            Control[k] = 0.0;
        // Enabling while the drag key is held must not synthesize a press.
        State[7] = float4(0.0, 0.0, frame.x > 0.5, 0.0);
        return;
    }

    JF_PickRecord pick = (JF_PickRecord)0;
    float4 world = PickWorld.Load(int3(0, 0, 0));
    float4 normal = PickNormal.Load(int3(0, 0, 0));
    pick.Valid = world.w > 0.5 && JF_IsFinite3(world.xyz);
    pick.ProjectId = uint4(1, 0, 0, 0);
    pick.ObjectId = 1;
    pick.SourceDraw = 1;
    pick.WorldPosition = world.xyz;
    pick.SurfaceNormal = normal.xyz;
    pick.ScreenRight = PickRight.Load(int3(0, 0, 0)).xyz;
    pick.ScreenUp = PickUp.Load(int3(0, 0, 0)).xyz;
    // Match displayed-screen up, not the upside-down native character target.
    if (IniParams[243].y > 0.5)
        pick.ScreenUp = -pick.ScreenUp;
    pick.Depth = normal.w;

    JF_InputControllerState controller =
        JF_DecodeInputControllerState(State[7], State[8]);
    JF_CapturedPick captured = JF_DecodeCapturedPick(
        Capture[0], Capture[1], Capture[2], Capture[3], Capture[4],
        Capture[5], Capture[6], Capture[7], Capture[8]);
    JF_MotionState motion = JF_DecodeMotionState(
        State[0], State[1], State[2], State[3], State[4], State[5], State[6]);

    JF_InputFrame input = (JF_InputFrame)0;
    input.CursorPixels = cursor.xy;
    input.ViewportPixels = cursor.zw;
    input.DragHeld = frame.x > 0.5 && JF_IsFinite2(cursor.xy)
        && all(cursor.xy >= 0.0) && all(cursor.xy <= cursor.zw);
    input.DeltaSeconds = frame.z;

    float3 previousOffset = motion.Position;
    uint previousGeneration = motion.CaptureGeneration;
    JF_UpdateInputController(controller, captured, input, pick);
    JF_GroupParameters parameters = (JF_GroupParameters)0;
    parameters.ObjectId = 1;
    parameters.SchemaVersion = JF_SCHEMA_VERSION;
    parameters.Valid = 1;
    parameters.Radius = clamp(JF_FiniteOr(tuning.x, 0.12), 0.01, 0.5);
    parameters.Strength = 0.7;
    parameters.Falloff = 2.0;
    parameters.VolumeResponse = 1.0;
    parameters.DragScale = clamp(JF_FiniteOr(tuning.z, 0.75), 0.01, 3.0);
    parameters.MaxOffset = clamp(JF_FiniteOr(tuning.y, 0.04), 0.0, 0.15);
    parameters.TargetFollowSeconds = 0.025;
    parameters.HoldFrequencyHz = 8.0;
    parameters.HoldDampingRatio = 0.85;
    parameters.ReleaseFrequencyHz = 3.0;
    parameters.ReleaseDampingRatio = 0.24;
    parameters.ReleaseImpulse = 0.15;
    parameters.MouseXSign = 1.0;
    parameters.MouseYSign = -1.0;
    JF_StepMotion(motion, 1, input, captured, parameters);
    if (motion.CaptureGeneration != previousGeneration)
        previousOffset = motion.Position;

    JF_EncodeMotionState(motion,
        State[0], State[1], State[2], State[3], State[4], State[5], State[6]);
    JF_EncodeInputControllerState(controller, State[7], State[8]);
    JF_EncodeCapturedPick(captured,
        Capture[0], Capture[1], Capture[2], Capture[3], Capture[4],
        Capture[5], Capture[6], Capture[7], Capture[8]);
    State[9] = float4(0.0, pick.Valid, motion.Active, motion.WasHeld);
    Control[0] = float4(motion.Position, motion.Active);
    Control[1] = float4(motion.Anchor, parameters.Radius);
    Control[2] = float4(previousOffset, parameters.Falloff);
    Control[3] = float4(parameters.VolumeResponse, 0.0, 0.0, 0.0);
}
