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
float GlowRadius;
float4 GlowInnerColor;
float4 GlowOuterColor;
float GlowPulseSpeed;
float GlowPulseIntensity;
float GlowRotates;
float GlowRotationSpeed;

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

    float2 coord = coords - float2(0.5, 0.5);
    float centerDist = length(coord);

    float rg = 1.0 - centerDist / max(GlowRadius * 0.02, 0.001);
    rg = max(rg, 0.0);
    float radialGradient = rg * rg;

    float pulse = sin(Time * GlowPulseSpeed) * GlowPulseIntensity + (1.0 - GlowPulseIntensity);
    radialGradient *= max(pulse, 0.0);

    float nebula = sin(coord.x * 20.0 + coord.y * 18.0 + Time * 0.8) * 0.5 + 0.5;
    radialGradient *= 0.85 + nebula * 0.15;

    float4 glowColor = lerp(GlowInnerColor, GlowOuterColor, min(centerDist * 2.2, 1.0));
    float ch = max(1.0 - centerDist * 3.0, 0.0);
    glowColor.rgb = lerp(glowColor.rgb, float3(1.0, 1.0, 1.0), ch * ch * 0.5);

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (glowColor.rgb - texColor.rgb) * radialGradient;
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
