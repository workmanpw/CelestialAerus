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
float FireIntensity;
float4 FireColor;
float FireDecay;
float FireEmbers;

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

    float pulse = sin(Time * TrailPulseSpeed - coords.y * 5.0);
    trailGradient *= pulse * TrailPulseIntensity + (1.0 - TrailPulseIntensity);

    float4 trailColor = TrailStartColor;

    float fc = 1.0 - coords.y * FireDecay;
    float flick = sin(Time * 11.0 + coords.x * 18.0) * 0.18 + 0.82;
    trailColor.rgb += FireColor.rgb * trailGradient * max(fc, 0.0) * flick * FireIntensity;

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (trailColor.rgb - texColor.rgb) * trailGradient;
    finalColor.a = texColor.a;

    finalColor.rgb *= 1.0 - TrailFades * Time * TrailFadeSpeed * 0.1;

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
