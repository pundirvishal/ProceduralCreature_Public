#ifndef DEFORM_TENTACLE_INCLUDED
#define DEFORM_TENTACLE_INCLUDED

// Struct for the point data; matches C# LogicalTentacle.TentaclePoint
struct TentaclePoint
{
    float2 position;
    float2 prevPosition;
};

// ComputeBuffer containing all tentacle point data
StructuredBuffer<TentaclePoint> _PointsBuffer;

// Shader properties (passed from C# Material) - these are globally accessible
// (Removed from explicit inputs to custom function in previous steps to avoid redefinition errors)
// If you still have global declarations of these properties in your main Shader Graph file
// and also as inputs to the custom function, ensure they are only inputs to the function.

// FIX: Define PI directly and use it. This is the most reliable way to handle PI.
#define MY_CUSTOM_PI 3.14159265359f 

// Catmull-Rom spline function (HLSL version)
float2 GetCatmullRomPosition(float t, float2 p0, float2 p1, float2 p2, float2 p3)
{
    float t2 = t * t;
    float t3 = t2 * t;

    return 0.5f * (
        (2.0f * p1) +
        (-p0 + p2) * t +
        (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
        (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
    );
}

// Main deformation function called by Shader Graph
void DeformTentacle_float(
    uint InstanceID, float2 UV, float2 UV1, // Existing inputs from Shader Graph
    // All shader properties are explicitly inputs to the custom function
    float PointsPerTentacle_SG,
    float StartWidth_SG,
    float EndWidth_SG,
    float PointsWidth_SG,
    float PointsHeight_SG,
    float RadialSegments_SG,
    float IsCircular_SG,
    out float3 WorldPosition,
    out float isPoint)
{
    // Use the values passed as inputs to the function
    float actualPointsPerTentacle = PointsPerTentacle_SG;
    float actualStartWidth = StartWidth_SG;
    float actualEndWidth = EndWidth_SG;
    float actualPointsWidth = PointsWidth_SG;
    float actualPointsHeight = PointsHeight_SG;
    float actualRadialSegments = RadialSegments_SG;
    float isCircularFlag = IsCircular_SG;

    isPoint = 0.0; // Default to ribbon type for isPoint output (0.0 for ribbon, 1.0 for circular)

    uint basePointIndex = InstanceID * (uint) actualPointsPerTentacle;

    float meshLengthProgress = UV.y;

    float scaledProgress = meshLengthProgress * (actualPointsPerTentacle - 1.0);
    uint p1_base_idx = floor(scaledProgress);
    float t_in_segment = frac(scaledProgress);

    uint p0_idx = max(0, (int) p1_base_idx - 1);
    uint p1_idx = min(p1_base_idx, (uint) actualPointsPerTentacle - 1);
    uint p2_idx = min((uint) actualPointsPerTentacle - 1, p1_base_idx + 1);
    uint p3_idx = min((uint) actualPointsPerTentacle - 1, p1_base_idx + 2);

    float2 p0_sim = _PointsBuffer[basePointIndex + p0_idx].position;
    float2 p1_sim = _PointsBuffer[basePointIndex + p1_idx].position;
    float2 p2_sim = _PointsBuffer[basePointIndex + p2_idx].position;
    float2 p3_sim = _PointsBuffer[basePointIndex + p3_idx].position;

    float2 splinePos2D = GetCatmullRomPosition(t_in_segment, p0_sim, p1_sim, p2_sim, p3_sim);

    float2 tangent2D = normalize(p2_sim - p1_sim);
    if (length(tangent2D) < 0.0001f)
        tangent2D = float2(1, 0);

    if (isCircularFlag > 0.5) // This is a circular cross-section
    {
        isPoint = 1.0; // Indicate circular cross-section for the output flag

        float3 tangent3D = float3(tangent2D.x, tangent2D.y, 0.0);
        float3 arbitraryUp = float3(0, 0, 1);
        
        float3 normal3D = normalize(cross(tangent3D, arbitraryUp));
        if (length(normal3D) < 0.0001f)
            normal3D = float3(0, 1, 0);

        float3 binormal3D = normalize(cross(normal3D, tangent3D));
        if (length(binormal3D) < 0.0001f)
            binormal3D = float3(1, 0, 0);

        float currentRadius = lerp(actualStartWidth, actualEndWidth, meshLengthProgress);

        float angle = UV.x * 2.0f * MY_CUSTOM_PI; // FIX: Using MY_CUSTOM_PI here

        float3 crossSectionOffset = (normal3D * cos(angle) + binormal3D * sin(angle)) * currentRadius;

        WorldPosition = float3(splinePos2D, 0.0) + crossSectionOffset;
    }
    else // This is a ribbon cross-section
    {
        isPoint = 0.0; // Indicate ribbon cross-section for the output flag

        float side = UV.x * 2.0 - 1.0;
        float currentWidth = lerp(actualStartWidth, actualEndWidth, meshLengthProgress);
        float halfWidth = currentWidth * 0.5;

        float2 normal2D_ribbon = float2(-tangent2D.y, tangent2D.x);

        WorldPosition = float3(splinePos2D.x + normal2D_ribbon.x * halfWidth * side,
                               splinePos2D.y + normal2D_ribbon.y * halfWidth * side,
                               0.0);
    }
}

#endif // DEFORM_TENTACLE_INCLUDED