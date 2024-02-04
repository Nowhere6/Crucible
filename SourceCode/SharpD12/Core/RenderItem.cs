using System;
using static SharpD12.AppConstants;

namespace SharpD12
{
  public abstract class RenderItemBase : IDisposable
  {
    public SuperObjectConsts objectConst; // Object constants.
    protected byte dirtyFrameCount = SwapChainSize;

    public abstract void Dispose();

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

    public override void Dispose()
    {
      throw new NotImplementedException();
    }
  }

  public class UIRenderItem : RenderItemBase
  {
    /// <summary> Bigger z_order later drawing. </summary>
    public byte zOrder;
    public UIMesh mesh;
    public string tex;

    public override void Dispose() => mesh.Dispose();
  }
}
