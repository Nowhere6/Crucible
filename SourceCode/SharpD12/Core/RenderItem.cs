using SharpDX;

namespace SharpD12
{
  public class RenderItem
  {
    public short dirtyFrameCount = 3;

    // Object constants.
    public ObjectConstants objectConst;

    public StaticMesh mesh;
  }

  public struct ObjectConstants
  {
    public Matrix world;
  }
}
