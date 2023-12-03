﻿using SharpDX;
using SharpDX.Direct3D;

namespace SharpD12
{
  public static class ProgramDefinedConstants
  {
    /// <summary>
    /// Best anisotropy level value that is good for both performance and quality.
    /// </summary>
    public const int AnisotropyLevel = 4;

    public const int SwapChainSize = 3;

    /// <summary>
    /// 11_0 feature level in DX12 can support GPU down to GeForce 400 series!
    /// </summary>
    public const FeatureLevel DX12FeatureLevel = FeatureLevel.Level_11_0;

    public static readonly Color4 CleanColor = new Color4(0.38f, 0.4f, 0.4f, 0f);
    public static readonly Vector4 CleanColorV = new Vector4(0.38f, 0.4f, 0.4f, 0f);
    
    public const float GameUnit2Meter = 1000f;

    public const int MaxRenderItems = 1 << 10;
  }
}
