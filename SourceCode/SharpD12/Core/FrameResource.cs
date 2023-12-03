using SharpDX;
using SharpDX.Direct3D12;

namespace SharpD12
{
  public class FrameResource
  {
    public long fenceValue = 0;
    public CommandAllocator cmdAllocator;
    public Resource backBuffer;
    public PassConstants passConst;
    public CpuDescriptorHandle rtvHandle;
    public CpuDescriptorHandle passCpuHandle;
    public CpuDescriptorHandle objectCpuHandle0;
    public GpuDescriptorHandle passGpuHandle;
    public GpuDescriptorHandle objectGpuHandle0;

    public static Resource depthBuffer;
    public static CpuDescriptorHandle dsvHandle;
    public static DescriptorHeap rtvDescHeap;
    public static DescriptorHeap dsvDescHeap;
    public static DescriptorHeap cbvSrvUavDescHeap;
    public static UploadBuffer<PassConstants> passBuffer;
    public static UploadBuffer<ObjectConstants> objectBuffer;
  }

  public struct PassConstants
  {
    public Matrix viewProj;
  }
}
