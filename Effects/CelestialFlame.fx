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
float FlameSpeed;

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

    float widthProfile = 0.55 + coords.y * 0.45;
    float xCentered = (coords.x - 0.5) / widthProfile;

    float turb = sin(coords.y * 7.0 + Time * FlameSpeed + coords.x * 6.0) * FlameDistortion * 0.09;
    float2 dCoord = coords;
    dCoord.x += turb;

    float fg = max(1.0 - coords.y, 0.0);
    float flameGradient = fg * fg;

    float h = max(1.0 - abs(xCentered) * 2.2, 0.0);
    float horiz = h * h;

    float flameMask = flameGradient * horiz;

    float flicker = sin(Time * FlameSpeed + coords.x * 12.0 + coords.y * 5.0) * 0.11 + 0.89;
    flameMask *= max(flicker, 0.0);

    float4 hotCore = float4(1.0, 1.0, 0.96, 1.0);
    float4 midColor = lerp(FlameEdgeColor, FlameCoreColor, flameMask);
    float cf = flameMask * flameMask;
    float4 flameColor = lerp(midColor, hotCore, cf * 0.5);
    flameColor *= max(FlameIntensity, 0.0);

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (flameColor.rgb - texColor.rgb) * flameMask * texColor.a;
    finalColor.a = texColor.a;

    float2 gVec = coords - float2(0.5, 0.55);
    float g = max(1.0 - (gVec.x * gVec.x + gVec.y * gVec.y) * 2.56, 0.0);
    finalColor.rgb += flameColor.rgb * (g * g) * 0.4;

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
