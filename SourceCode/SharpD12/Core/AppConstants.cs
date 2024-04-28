using SharpDX;
using SharpDX.Direct3D;

namespace SharpD12;

public static class AppConstants
{
  /// <summary>Best anisotropy level value considering both performance and quality.</summary>
  public const int AnisotropyLevel = 4;

  public const int SwapChainSize = 3;

  /// <summary>11_0 feature level in DX12 can support GPU down to GeForce 400 series!</summary>
  public const FeatureLevel DX12FeatureLevel = FeatureLevel.Level_11_0;

  public static readonly Color4 CleanColor = new Color4(0.2f, 0.21f, 0.2f, 0f);

  // 1 Unit = 1 km

  public const int MaxStaticRenderItems = 1 << 10;

  public const int MaxUIRenderItems = 1 << 8;
}