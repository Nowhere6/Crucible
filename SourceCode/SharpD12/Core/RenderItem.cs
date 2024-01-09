using static SharpD12.AppConstants;

namespace SharpD12
{
  public abstract class RenderItemBase
  {
    public SuperObjectConsts objectConst; // Object constants.
    protected byte dirtyFrameCount = SwapChainSize;
    /// <summary> Check if this needs update. Auto decease dirty count if needs.</summary>
    public bool NeedUpdate()
    {
      bool need = dirtyFrameCount > 0;
      if (need) dirtyFrameCount--;
      return need;
    }
  }

  public class StaticRenderItem : RenderItemBase
  {
    public StaticMesh mesh;
    public string albedoTex;
  }

  public class UIRenderItem : RenderItemBase
  {
    /// <summary> Bigger z_order later drawing. </summary>
    public byte zOrder;
    public UIMesh mesh;
    public string tex;
  }
}
