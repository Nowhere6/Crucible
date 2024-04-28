/*
  This is the common hlsl header.
  
  You can create your own headers,
  but you could only reference them from relative paths.

  Notice:
  mtx = matrix
*/

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

/*
  These are static samplers.
  Some materials need the ability to choose the clamp or wrap address mode.
  Thus, use "Tri" or "Ani" instead, and define "VARIATION_CLAMP_WRAP" in your shader,
  then the engine will create two materials for it.
  (As for programmers, a material means a pipeline state in DX12)
*/
SamplerState Point : register(s0);
SamplerState TriClamp : register(s1);
SamplerState TriWrap : register(s2);
SamplerState AniClamp : register(s3);
SamplerState AniWrap : register(s4);
SamplerComparisonState cmp : register(s5);
#ifdef CLAMP_ADDRESS
#define Tri TriClamp
#define Ani AniClamp
#else
#define Tri TriWrap
#define Ani AniWrap
#endif

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

Texture2D tex0 : register(t0);
Texture2D tex1 : register(t1);

float4 EncodeSRGB(float4 color)
{
  float4 result;
  result.rgb =  1.055*pow(color.rgb, 1.0/2.4) - 0.055;
  result.a = color.a;
  return result;
}