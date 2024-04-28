using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D12;

namespace SharpD12;

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

public class RenderPassResource : IDisposable
{
  public enum GBufferType
  {
    FULL_0,
    FULL_1,
    HALF_0,
    HALF_1
  }

  Resource resource;
  public ushort srvIndex;
  public ushort rtvIndex;

  private RenderPassResource(Device dx12Device)
  {
    // TODO
  }

  public void Dispose()
  {
    DescHeapManager.RemoveView(srvIndex, ViewType.SRV);
    DescHeapManager.RemoveView(rtvIndex, ViewType.RTV);
    resource.Dispose();
    resource = null;
  }

  //////////////////////////////////////////////////
  /// STATIC ///////////////////////////////////////
  //////////////////////////////////////////////////

  private static RenderPassResource[] gBuffers;

  public static void Rebuild(int width, int height)
  {
    // Release old resources
    int count = gBuffers.Length;
    for (int i = 0; i < count; i++)
    {
      gBuffers[i].Dispose();
      gBuffers[i] = null;
    }

    // Create new resources
    // TODO
  }
}