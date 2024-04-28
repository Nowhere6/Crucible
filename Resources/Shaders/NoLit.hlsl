#include "Common.hlsl"

VertexOut VS(VertexIn vin)
{
  VertexOut vout;
  // Transform to homogeneous clip space.
  float4 posW = mul(float4(vin.pL, 1.0f), mtxW);
  vout.pos = mul(posW, mtxVP);
  vout.uv = vin.uv;
  return vout;
}

float4 PS(VertexOut pin) : SV_Target
{
  float4 c = tex0.Sample(Ani, pin.uv);
  c = EncodeSRGB(c);
  return c;
}