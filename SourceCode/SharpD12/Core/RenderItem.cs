using SharpDX;
using static SharpD12.AppConstants;

namespace SharpD12
{
  public class RenderItem
  {
    public short dirtyFrameCount = SwapChainSize;
    public ObjectConstants objectConst; // Object constants.
    public StaticMesh mesh;
    public string albedoTex;
  }

  public struct ObjectConstants
  {
    public Matrix world;
  }
}
