using SharpDX;
using SharpDX.Direct3D12;

namespace SharpD12
{
  public class FrameResource
  {
    public CommandAllocator cmdAllocator;
    public Resource backBuffer;
    public ushort rtvIndex;

    public static Resource depthBuffer;
    public static ushort dsvIndex;
    public static UploadBuffer<SuperPassConsts> passBuffer;
    public static UploadBuffer<SuperObjectConsts> staticRenderItemObjectBuffer;
    public static UploadBuffer<SuperObjectConsts> uiRenderItemObjectBuffer;
  }

  public struct SuperPassConsts
  {
    public Matrix viewProj;
    public Vector4 viewportSize; // w, h, 1/w, 1/h
  }

  public struct SuperObjectConsts
  {
    public Matrix world;
    public Vector4 color;
  }
}