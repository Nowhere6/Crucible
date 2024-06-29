using System;
using SharpDX;
using SharpDX.Direct3D12;
using SharpDX.Mathematics.Interop;

namespace Crucible;

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

public enum GBufferType : int
{
  GBuffer0,
  GBuffer1,
  TempBuffer0,
  Count
}

public static class RenderPassResource
{
  public class PassResource
  {
    public readonly Resource resource;
    public readonly ushort srvIndex;
    public readonly ushort rtvIndex;

    public PassResource(Resource _resource, ushort _srvIndex, ushort _rtvIndex)
    {
      resource = _resource;
      srvIndex = _srvIndex;
      rtvIndex = _rtvIndex;
    }
  }

  private static PassResource[] buffers;

  public static PassResource Get(GBufferType type) => buffers[(int)type];

  public static void Reinitialize(Device device, int width, int height)
  {
    // Dispose old resources
    if (buffers == null)
    {
      buffers = new PassResource[(int)GBufferType.Count];
    }
    else
    {
      foreach (var res in buffers)
      {
        res.resource.Dispose();
        DescHeapManager.RemoveView(res.srvIndex, ViewType.SRV);
        DescHeapManager.RemoveView(res.rtvIndex, ViewType.RTV);
      }
    }
    // Create new resources
    var props = new HeapProperties(HeapType.Default);
    var format = SharpDX.DXGI.Format.R8G8B8A8_UNorm;
    var desc = ResourceDescription.Texture2D(format, width, height);
    desc.Flags = ResourceFlags.AllowRenderTarget;
    var rtvDesc = new RenderTargetViewDescription
    {
      Format = format,
      Dimension = RenderTargetViewDimension.Texture2D
    };
    var srvDesc = new ShaderResourceViewDescription()
    {
      Shader4ComponentMapping = Texture.GetMapping(),
      Format = format,
      Dimension = ShaderResourceViewDimension.Texture2D,
      Texture2D = { MipLevels = 1 }
    };
    for (int i = 0; i < (int)GBufferType.Count; i++)
    {
      var cleanValue = new ClearValue() {Format = format, Color = new RawVector4(0,0,0,1)};
      var resource = device.CreateCommittedResource(props, HeapFlags.None, desc, ResourceStates.RenderTarget, cleanValue);
      var srvIndex = DescHeapManager.CreateView(device, resource, srvDesc, ViewType.SRV);
      var rtvIndex = DescHeapManager.CreateView(device, resource, rtvDesc, ViewType.RTV);
      buffers[i] = new PassResource(resource, srvIndex, rtvIndex);
    }
  }

  public static void CLeanAll(GraphicsCommandList cmd)
  {
    foreach (var res in buffers)
    {
      cmd.ClearRenderTargetView(DescHeapManager.GetCPUHandle(res.rtvIndex, ViewType.RTV), Color4.Black);
    }
  }
}