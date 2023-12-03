#include "Shared.hlsl"
// mtx = matrix

VertexOut VS(VertexIn vin)
{
  VertexOut vout;
  // Transform to homogeneous clip space.
  float4 posW = mul(float4(vin.pL, 1.0f), mtxW);
  vout.pH = mul(posW, mtxVP);
  vout.uv = vin.uv;
  return vout;
}

float4 PS(VertexOut pin) : SV_Target
{
  return Tex(texAlbedo, pin.uv);
  //return float4(pin.uv.x, pin.uv.x, pin.uv.x, 0);

}