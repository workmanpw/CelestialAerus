matrix uWorldViewProjection;

float Time;
float4 TrailStartColor;
float4 TrailEndColor;
float TrailPulseSpeed;
float TrailPulseIntensity;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float3 TextureCoordinates : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float3 TextureCoordinates : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    output.Position = mul(input.Position, uWorldViewProjection);
    output.Color = input.Color;
    output.TextureCoordinates = input.TextureCoordinates;
    return output;
}

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    float along = min(max(input.TextureCoordinates.x, 0.0), 1.0);

    // VertexStrip provides pixel-space across coordinates:
    //   y = 0 on one edge, y = full width on the other, z = full width.
    // Normalize before shaping the cross-section, otherwise the ribbon
    // collapses into a single-pixel line.
    float fullWidth = max(input.TextureCoordinates.z, 0.0001);
    float across = abs(input.TextureCoordinates.y / fullWidth - 0.5) * 2.0;
    float edge = max(1.0 - across, 0.0);
    // `along` runs from the tail (0) to the projectile (1). Adding the
    // position phase makes the pulse travel from the projectile down the trail.
    float pulse = sin(Time * TrailPulseSpeed + along * 8.0) * TrailPulseIntensity + (1.0 - TrailPulseIntensity);
    pulse = max(pulse, 0.0);
    float4 trailColor = lerp(TrailStartColor, TrailEndColor, along);
    return float4(trailColor.rgb * input.Color.rgb, input.Color.a * edge * edge * pulse);
}

technique Technique1
{
    pass CelestialPass
    {
        VertexShader = compile vs_2_0 VertexShaderFunction();
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
