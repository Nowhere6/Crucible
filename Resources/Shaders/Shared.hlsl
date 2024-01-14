// mtx = matrix

struct VertexIn
{
  float3 pL : POSITION;
  float3 nL : NORMAL;
  float3 tL : TANGENT;
  float2 uv : TEXCOORD;

  uint idI : SV_InstanceID;
  uint idV : SV_VertexID;
};

struct VertexOut
{
  float4 pos : SV_POSITION;
  float2 uv : TEXCOORD;
};

struct UIVertexIn
{
  float2 pos : POSITION;
  float2 uv : TEXCOORD;
};

struct UIVertexOut
{
  float4 pos : SV_POSITION;
  float2 uv : TEXCOORD;
};

struct UIPixelIn
{
  float4 pos : SV_POSITION;
  float2 uv : TEXCOORD;
  uint id : SV_PrimitiveID;
};

SamplerState Point : register(s0);
SamplerState Trilinear : register(s1);
SamplerState Anisotropic : register(s2);

#define TexP(tex, uv) tex.Sample(Point, uv)       // Point-Clamp (Post-processing)
#define TexT(tex, uv) tex.Sample(Trilinear, uv)   // Trilinear-Clamp (Post-processing / UI)
#define Tex(tex, uv)  tex.Sample(Anisotropic, uv) // Anisotropic-Wrap (Mesh)

cbuffer SuperObjectConsts : register(b0)
{
  float4x4 mtxW;
  float4 color;
};

cbuffer SuperPassConsts : register(b1)
{
  float4x4 mtxVP;
  float4 viewportSize; // w, h, 1/w, 1/h
};

Texture2D texAlbedo : register(t0);

float4 EncodeSRGB(float4 color)
{
  float4 result;
  result.rgb =  1.055*pow(color.rgb, 1.0/2.4) - 0.055;
  result.a = color.a;
  return result;
}