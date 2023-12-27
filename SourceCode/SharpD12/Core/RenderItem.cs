using SharpDX;
using static SharpD12.AppConstants;

namespace SharpD12
{
  public class RenderItem
  {
    public short dirtyFrameCount = SwapChainSize;

    // Object constants.
    public ObjectConstants objectConst;

    public StaticMesh mesh;
  }

  public struct ObjectConstants
  {
    public Matrix world;
  }
}
