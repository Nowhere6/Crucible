using System;
using SharpDX.Direct3D12;
using System.Collections.Generic;

namespace SharpD12;

/// <summary>Descriptor types.</summary>
public enum ViewType
{
  // Stored in srvUavDescHeap.
  CBV,
  SRV,
  UAV,
  // Stored in rtvDescHeap.
  RTV,
  // stored in dsvDescHeap.
  DSV
}

/// <summary> Descriptor heaps manager. </summary>
public static class DescHeapManager
{
  const int MaxSrvUavCount = 1024;
  const int MaxRtvCount = 16;
  const int MaxDsvCount = 1;
  static DescriptorHeap srvUavDescHeap;
  static DescriptorHeap rtvDescHeap;
  static DescriptorHeap dsvDescHeap;
  static Queue<ushort> srvUavAvaliableIndex = new Queue<ushort>();
  static Queue<ushort> rtvAvaliableIndex = new Queue<ushort>();
  static Queue<ushort> dsvAvaliableIndex = new Queue<ushort>();
  static CpuDescriptorHandle srvUavCPUHandle_0;
  static GpuDescriptorHandle srvUavGPUHandle_0;
  static CpuDescriptorHandle rtvCPUHandle_0;
  static GpuDescriptorHandle rtvGPUHandle_0;
  static CpuDescriptorHandle dsvCPUHandle_0;
  static GpuDescriptorHandle dsvGPUHandle_0;

  public static void Initialize(SharpDX.Direct3D12.Device dx12Device)
  {
    if (srvUavDescHeap != null || rtvDescHeap != null || dsvDescHeap != null)
    {
      throw new Exception("Descriptor heaps can be initialized only once.");
    }

    for (ushort i = 0; i < MaxSrvUavCount; i++)
      srvUavAvaliableIndex.Enqueue(i);
    for (ushort i = 0; i < MaxRtvCount; i++)
      rtvAvaliableIndex.Enqueue(i);
    for (ushort i = 0; i < MaxDsvCount; i++)
      dsvAvaliableIndex.Enqueue(i);

    var heapType = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView;
    var heapDesc = new DescriptorHeapDescription { Type = heapType, DescriptorCount = MaxSrvUavCount, Flags = DescriptorHeapFlags.ShaderVisible };
    srvUavDescHeap = dx12Device.CreateDescriptorHeap(heapDesc);
    srvUavCPUHandle_0 = srvUavDescHeap.CPUDescriptorHandleForHeapStart;
    srvUavGPUHandle_0 = srvUavDescHeap.GPUDescriptorHandleForHeapStart;
    heapType = DescriptorHeapType.RenderTargetView;
    heapDesc = new DescriptorHeapDescription { Type = heapType, DescriptorCount = MaxRtvCount };
    rtvDescHeap = dx12Device.CreateDescriptorHeap(heapDesc);
    rtvCPUHandle_0 = rtvDescHeap.CPUDescriptorHandleForHeapStart;
    rtvGPUHandle_0 = rtvDescHeap.GPUDescriptorHandleForHeapStart;
    heapType = DescriptorHeapType.DepthStencilView;
    heapDesc = new DescriptorHeapDescription { Type = heapType, DescriptorCount = MaxRtvCount };
    dsvDescHeap = dx12Device.CreateDescriptorHeap(heapDesc);
    dsvCPUHandle_0 = dsvDescHeap.CPUDescriptorHandleForHeapStart;
    dsvGPUHandle_0 = dsvDescHeap.GPUDescriptorHandleForHeapStart;
  }

  public static void BindSrvUavHeap(GraphicsCommandList cmd) => cmd.SetDescriptorHeaps(srvUavDescHeap);

  public static CpuDescriptorHandle GetCPUHandle(ushort idx, ViewType viewType)
  {
    var CheckIdx = (ushort max) => { if (idx < 0 || idx >= max) throw new ArgumentOutOfRangeException(nameof(idx)); };
    switch (viewType)
    {
      case ViewType.CBV:
      case ViewType.SRV:
      case ViewType.UAV:
        CheckIdx(MaxSrvUavCount);
        return srvUavCPUHandle_0 + SD12Engine.CSUSize * idx;
      case ViewType.RTV:
        CheckIdx(MaxRtvCount);
        return rtvCPUHandle_0 + SD12Engine.RTVSize * idx;
      case ViewType.DSV:
      default:
        CheckIdx(MaxDsvCount);
        return dsvCPUHandle_0 + SD12Engine.RTVSize * idx;
    }
  }

  public static GpuDescriptorHandle GetGPUHandle(ushort idx, ViewType viewType)
  {
    var CheckIdx = (ushort max) => { if (idx < 0 || idx >= max) throw new ArgumentOutOfRangeException(nameof(idx)); };
    switch (viewType)
    {
      case ViewType.CBV:
      case ViewType.SRV:
      case ViewType.UAV:
        CheckIdx(MaxSrvUavCount);
        return srvUavGPUHandle_0 + SD12Engine.CSUSize * idx;
      case ViewType.RTV:
        CheckIdx(MaxRtvCount);
        return rtvGPUHandle_0 + SD12Engine.RTVSize * idx;
      case ViewType.DSV:
      default:
        CheckIdx(MaxDsvCount);
        return dsvGPUHandle_0 + SD12Engine.RTVSize * idx;
    }
  }

  public static ushort CreateView(Device dx12Device, Resource res, object viewDesc, ViewType viewType)
  {
    ushort idx;
    switch (viewType)
    {
      case ViewType.CBV:
      case ViewType.SRV:
      case ViewType.UAV:
        if (srvUavAvaliableIndex.TryDequeue(out idx))
        {
          if (viewType == ViewType.CBV)
            dx12Device.CreateConstantBufferView(viewDesc as ConstantBufferViewDescription?, GetCPUHandle(idx, viewType));
          else if (viewType == ViewType.SRV)
            dx12Device.CreateShaderResourceView(res, viewDesc as ShaderResourceViewDescription?, GetCPUHandle(idx, viewType));
          else
            // TODO: UAV Counter is unimplemented.
            dx12Device.CreateUnorderedAccessView(res, null, viewDesc as UnorderedAccessViewDescription?, GetCPUHandle(idx, viewType));
          return idx;
        }
        break;
      case ViewType.RTV:
        if (rtvAvaliableIndex.TryDequeue(out idx))
        {
          dx12Device.CreateRenderTargetView(res, viewDesc as RenderTargetViewDescription?, GetCPUHandle(idx, viewType));
          return idx;
        }
        break;
      case ViewType.DSV:
      default:
        if (dsvAvaliableIndex.TryDequeue(out idx))
        {
          dx12Device.CreateDepthStencilView(res, viewDesc as DepthStencilViewDescription?, GetCPUHandle(idx, viewType));
          return idx;
        }
        break;
    }
    throw new Exception($"Create descriptor failed.");
  }

  public static void RemoveView(ushort idx, ViewType viewType)
  {
    switch (viewType)
    {
      case ViewType.CBV:
      case ViewType.SRV:
      case ViewType.UAV:
        if (srvUavAvaliableIndex.Contains(idx)) break;
        srvUavAvaliableIndex.Enqueue(idx); return;
      case ViewType.RTV:
        if (rtvAvaliableIndex.Contains(idx)) break;
        rtvAvaliableIndex.Enqueue(idx); return;
      case ViewType.DSV:
      default:
        if (srvUavAvaliableIndex.Contains(idx)) break;
        dsvAvaliableIndex.Enqueue(idx); return;
    }
    throw new ArgumentException($"{viewType.ToString()} descriptor deletion failed.");
  }
}