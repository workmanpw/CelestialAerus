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
float SwirlIntensity;
float4 SwirlColor1;
float4 SwirlColor2;
float4 SwirlColor3;
float SwirlSpeed;
float SwirlArms;

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
    float radius = length(coord);

    // Spiral arms via angle approximation (cheaper than atan2)
    float angle = coord.y / (abs(coord.x) + 0.001);
    float arms = max(SwirlArms, 1.0);
    float armPattern = sin(angle * arms + radius * 7.0 - Time * SwirlSpeed * 1.5);
    armPattern = armPattern * armPattern;

    float dg = max(1.0 - radius * 2.0, 0.0);
    float discGradient = dg * dg;

    float4 galaxyColor = lerp(SwirlColor1, SwirlColor2, min(radius * 2.5, 1.0));
    galaxyColor = lerp(galaxyColor, SwirlColor3, armPattern * 0.7);

    float cg = max(1.0 - radius * 4.0, 0.0);
    galaxyColor.rgb += float3(1.0, 1.0, 1.0) * (cg * cg) * 0.6;

    galaxyColor *= max(SwirlIntensity, 0.0) * (0.55 + armPattern * 0.45);

    float4 finalColor;
    finalColor.rgb = texColor.rgb + (galaxyColor.rgb - texColor.rgb) * discGradient;
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
