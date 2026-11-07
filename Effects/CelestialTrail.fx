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
float TrailLength;
float TrailWidth;
float4 TrailStartColor;
float4 TrailEndColor;
float TrailFades;
float TrailFadeSpeed;
float TrailPulseSpeed;
float TrailPulseIntensity;
float TrailGlows;
float4 TrailGlowColor;

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

    float2 centered = coords - 0.5;
    float radial = saturate(length(centered) * 2.0);

    // Radial glow instead of a hard centre stripe.
    float trailGradient = saturate(1.0 - radial);
    trailGradient *= trailGradient;

    float pulse = sin(Time * TrailPulseSpeed - coords.y * 6.0) * TrailPulseIntensity + (1.0 - TrailPulseIntensity);
    pulse = max(pulse, 0.0);
    trailGradient *= pulse;

    float shimmer = sin(Time * 23.0 + coords.y * 40.0 + coords.x * 18.0) * 0.06 + 0.94;
    trailGradient *= max(shimmer, 0.0);

    float4 trailColor = lerp(TrailStartColor, TrailEndColor, coords.y);

    float core = saturate(1.0 - radial);
    core = core * core * core;
    trailColor = lerp(trailColor, float4(1.0, 1.0, 1.0, 1.0), core * 0.45 * pulse);

    float glowAmount = core * 0.4 * max(TrailGlows, 0.0);
    trailColor = lerp(trailColor, TrailGlowColor, glowAmount * pulse);

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (trailColor.rgb - texColor.rgb) * trailGradient;
    finalColor.a = texColor.a;

    float fadeVal = max(1.0 - Time * TrailFadeSpeed * 0.1, 0.0);
    finalColor.rgb *= 1.0 - max(TrailFades, 0.0) * (1.0 - fadeVal);

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
