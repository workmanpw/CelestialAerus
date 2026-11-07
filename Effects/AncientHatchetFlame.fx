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
float FlameIntensity;
float FlameDistortion;
float4 FlameCoreColor;
float4 FlameEdgeColor;
float AxeSpinSpeed;

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

    float widthProfile = 0.45 + coords.y * 0.55;
    float xCentered = (coords.x - 0.5) / widthProfile;

    float turb = sin(coords.y * 10.0 + Time * AxeSpinSpeed + coords.x * 5.0);
    float2 dCoord = coords;
    dCoord.x += turb * FlameDistortion * 0.06;

    float fg = max(1.0 - coords.y, 0.0);
    float flameGradient = fg * fg;

    float h = max(1.0 - abs(xCentered) * 2.0, 0.0);
    float horiz = h * h;

    float flameMask = flameGradient * horiz;

    float flicker = sin(Time * 9.0 + coords.x * 12.0 + coords.y * 7.0) * 0.09 + 0.91;
    flameMask *= max(flicker, 0.0);

    float4 flameColor = lerp(FlameEdgeColor, FlameCoreColor, flameMask);
    float coreF = flameMask * flameMask * flameMask;
    flameColor.rgb = lerp(flameColor.rgb, float3(1.0, 0.98, 0.92), coreF * 0.55);
    flameColor *= max(FlameIntensity, 0.0);

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (flameColor.rgb - texColor.rgb) * flameMask * texColor.a;
    finalColor.a = texColor.a;

    float2 gVec = coords - float2(0.5, 0.62);
    float glow = 1.0 - (gVec.x * gVec.x + gVec.y * gVec.y) * 2.9;
    glow = max(glow, 0.0);
    finalColor.rgb += flameColor.rgb * (glow * glow) * 0.35;

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
