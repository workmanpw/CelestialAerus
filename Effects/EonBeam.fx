sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
float3 uColor;
float3 uSecondaryColor;
float uOpacity;
float uSaturation;
float uRotation;
float uTime;
float4 uSourceRect;
float2 uWorldPosition;
float uDirection;
float3 uLightSource;
float2 uImageSize0;
float2 uImageSize1;
matrix uWorldViewProjection;
float4 uShaderSpecificData;

float Time;
float BeamLength;
float BeamWidth;
float BeamIntensity;
float4 BeamCoreColor;
float4 BeamEdgeColor;
float BeamPulseSpeed;
float BeamFlickers;
float BeamFlickerIntensity;
float BeamSpinSpeed;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

VertexShaderOutput VertexShaderFunction(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    output.Position = mul(input.Position, uWorldViewProjection);
    output.Color = input.Color;
    output.TextureCoordinates = input.TextureCoordinates;
    return output;
}

float4 PixelShaderFunction(VertexShaderOutput input) : COLOR0
{
    float4 color = input.Color;
    float2 coords = input.TextureCoordinates;
    float4 texColor = tex2D(uImage0, coords);

    // Radial falloff from the sprite centre. The old version sliced the
    // texture horizontally (abs(coords.x - 0.5)), which painted a hard
    // bright band straight down the middle of every sprite it touched.
    float2 centered = coords - 0.5;
    float radial = saturate(length(centered) * 2.0);

    // Soft bloom: strongest at the centre, fading smoothly to the rim.
    float beamGradient = saturate(1.0 - radial);
    beamGradient = beamGradient * beamGradient * (3.0 - 2.0 * beamGradient);

    // Energy still flows across the blade; using both axes keeps the
    // motion diagonal instead of locked to a single vertical line.
    float flow = coords.x + coords.y;
    float pulseFlow = sin(Time * BeamPulseSpeed + flow * 12.0) * 0.5 + 0.5;
    float helix = sin(flow * 18.0 - Time * BeamSpinSpeed * 5.0);
    helix = helix * helix;

    float4 beamColor = lerp(BeamEdgeColor, BeamCoreColor, beamGradient);
    beamColor.rgb = lerp(beamColor.rgb, float3(1.0, 1.0, 1.0), beamGradient * 0.35);

    float energy = (0.6 + pulseFlow * 0.4) * max(BeamIntensity, 0.0);
    beamColor *= energy;
    beamColor.rgb += BeamCoreColor.rgb * helix * beamGradient * 0.35;

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (beamColor.rgb - texColor.rgb) * beamGradient;
    finalColor.a = texColor.a;

    return finalColor * color;
}

technique Technique1
{
    pass CelestialPass
    {
        VertexShader = compile vs_2_0 VertexShaderFunction();
        PixelShader = compile ps_2_0 PixelShaderFunction();
    }
}
