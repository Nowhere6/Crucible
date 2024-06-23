using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using D12Device = SharpDX.Direct3D12.Device;
using D12Resource = SharpDX.Direct3D12.Resource;

namespace Crucible;

public enum BufferType
{
  Texture,
  ConstantBuffer,
  VertexOrIndexBuffer
}

/// <summary> Generic upload heap wrapper class. </summary>
public class UploadBuffer<T> : IDisposable where T : struct
{
  public readonly BufferType bufferType;
  public readonly int ElementSize;
  public readonly int Size;
  public D12Resource Heap;
  readonly int count;
  readonly IntPtr mappedPtr;
  readonly long gpuAddr;

  public UploadBuffer(D12Device dx12Device, int elementCount, BufferType contentType, TextureInfo info = null)
  {
    if(contentType == BufferType.Texture)
    {
      bufferType = BufferType.Texture;
      Size = info.TotalSize;
      var resDesc = ResourceDescription.Texture2D(info.DXGIFormat, info.Width, info.Width, (short)info.ArraySliceCount, (short)info.MipSliceCount);
      var props = new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0);
      var state = ResourceStates.CopySource;
      Heap = dx12Device.CreateCommittedResource(props, HeapFlags.None, resDesc, state);
    }
    else
    {
      bufferType = contentType;
      ElementSize = Utilities.SizeOf<T>();
      if (contentType == BufferType.ConstantBuffer) ElementSize = ConstantBufferAlignUp(ElementSize);
      count = elementCount;
      Size = ElementSize * elementCount;
      var resDesc = ResourceDescription.Buffer(Size);
      var props = new HeapProperties(HeapType.Upload);
      var state = ResourceStates.GenericRead;
      Heap = dx12Device.CreateCommittedResource(props, HeapFlags.None, resDesc, state);
      gpuAddr = Heap.GPUVirtualAddress;
      mappedPtr = Heap.Map(0);
    }
  }

  public void Dispose() => Heap.Dispose();

  public long GetGPUAddress(int index = 0) => gpuAddr + index * ElementSize;

  public void Write(int DestIndex, ref T data)
  {
    if (bufferType == BufferType.Texture)
      throw new NotSupportedException("Only CB/IB/VB buffer can invoke this.");
    Utilities.Write<T>(mappedPtr + DestIndex * ElementSize, ref data);
  }

  public void Write(int DestIndex, T[] data, int srcIndex = 0, int srcCount = 0)
  {
    if (srcIndex + srcCount > data.Length)
      throw new ArgumentOutOfRangeException(nameof(srcCount));
    if (srcIndex == 0 && srcCount == 0)
      srcCount = data.Length;

    if (bufferType == BufferType.ConstantBuffer)
    {
      for (int i = 0; i < srcCount; i++)
      {
        Utilities.Write<T>(mappedPtr + (DestIndex + i) * ElementSize, data, srcIndex + i, 1);
      }
    }
    else if (bufferType == BufferType.VertexOrIndexBuffer)
    {
      Utilities.Write<T>(mappedPtr + DestIndex * ElementSize, data, srcIndex, srcCount);
    }
    else
    {
      throw new NotSupportedException("Only CB/IB/VB buffer can invoke this.");
    }
  }

  /// <summary>
  /// Align up for constant buffers. <br/>
  /// Constant buffer elements need to be multiples of 256 bytes.
  /// This is because the hardware can only view constant data 
  /// at m*256 byte offsets and of n*256 byte lengths. 
  /// </summary>
  static int ConstantBufferAlignUp(int size) => size + 255 & ~255;
}

/// <summary> Generic default heap wrapper class. </summary>
public class DefaultBuffer<T> : IDisposable where T : struct
{
  public readonly BufferType bufferType;
  public readonly TextureInfo TexInfo;
  public UploadBuffer<T> middleBuffer;
  public readonly bool ReadOnly;
  public readonly int Size;
  public D12Resource Heap;
  bool dirty;

  public DefaultBuffer(D12Device dx12Device, int bytes, BufferType bufferType, bool isReadonly, TextureInfo info = null)
  {
    // Initialize values and create upload buffer.
    if (bufferType == BufferType.Texture)
    {
      if (typeof(T) != typeof(byte)) throw new ArgumentException("T should be byte.");
      if (info == null) throw new ArgumentException("TextureInfo is null.");
    }
    middleBuffer = new UploadBuffer<T>(dx12Device, bytes / Utilities.SizeOf<T>(), bufferType, info);
    this.Size = middleBuffer.Size;
    this.bufferType = bufferType;
    this.ReadOnly = isReadonly;
    this.TexInfo = info;
    this.dirty = false;

    // Create defualt heap.
    var props = new HeapProperties(HeapType.Default);
    var state = ResourceStates.Common;
    ResourceDescription desc;
    if (bufferType == BufferType.Texture)
    {
      desc = ResourceDescription.Texture2D(info.DXGIFormat, info.Width, info.Width, (short)info.ArraySliceCount, (short)info.MipSliceCount);
    }
    else
    {
      desc = ResourceDescription.Buffer(bytes);
    }
    Heap = dx12Device.CreateCommittedResource(props, HeapFlags.None, desc, state);

    // Subscribe update event.
    DefaultBufferUpdater.Register(UpdateAction);
  }

  public void Dispose()
  {
    DefaultBufferUpdater.UnRegister(UpdateAction);
    middleBuffer?.Dispose();
    Heap.Dispose();
  }

  private void UpdateAction(GraphicsCommandList cmd, long targetFence)
  {
    if (dirty) dirty = false;
    else return;

    // Before barrier
    cmd.ResourceBarrier(new ResourceTransitionBarrier(Heap, ResourceStates.Common, ResourceStates.CopyDestination));
    if (bufferType == BufferType.Texture)
    {
      for (int i = 0; i < TexInfo.MipSliceCount; i++)
      {
        cmd.CopyTextureRegion(new TextureCopyLocation(Heap, i), 0, 0, 0, new TextureCopyLocation(middleBuffer.Heap, i), null);
      }
    }
    else
    {
      cmd.CopyBufferRegion(Heap, 0, middleBuffer.Heap, 0, middleBuffer.Size);
    }

    // After barrier
    cmd.ResourceBarrier(new ResourceTransitionBarrier(Heap, ResourceStates.CopyDestination, ResourceStates.Common));

    // Release middle heap after update of read-only buffer.
    if (ReadOnly)
    {
      DefaultBufferUpdater.UnRegister(UpdateAction);
      DelayReleaseManager.Enqueue(targetFence, middleBuffer);
      middleBuffer = null;
    }
  }

  public void Write(int DestIndex, ref T data)
  {
    WriteCheck();
    if (bufferType == BufferType.Texture)
      throw new NotSupportedException("Should be none-texture buffer.");
    middleBuffer.Write(DestIndex, ref data);
  }

  public void Write(int DestIndex, T[] data, int srcIndex = 0, int srcCount = 0)
  {
    WriteCheck();
    if (bufferType == BufferType.Texture)
      throw new NotSupportedException("Should be none-texture buffer.");
    if (srcIndex == 0 && srcCount == 0)
      srcCount = data.Length;
    middleBuffer.Write(DestIndex, data, srcIndex, srcCount);
  }

  public void TextureWrite(byte[] data, int arrayIndex = 0)
  {
    WriteCheck();
    if (bufferType != BufferType.Texture)
      throw new NotSupportedException("Should be texture buffer.");

    var nativePtr = new NativePtr(data);
    IntPtr ptr = nativePtr;
    for (int mip = 0; mip < TexInfo.MipSliceCount; mip++)
    {
      int rowPixelCount = TexInfo.Width >> mip;
      int rowPitch = rowPixelCount * TexInfo.PixelSize;
      int depthPitch = rowPixelCount * rowPitch;
      middleBuffer.Heap.WriteToSubresource(arrayIndex * TexInfo.MipSliceCount + mip, null, ptr, rowPitch, depthPitch);
      ptr += depthPitch;
    }
    nativePtr.Free();
  }

  void WriteCheck()
  {
    if (ReadOnly && middleBuffer == null)
      throw new NotSupportedException("Read-only buffer cannot be written in two frames.");
    dirty = true;
  }
}

static public class DefaultBufferUpdater
{
  static event Action<GraphicsCommandList, long> UpdateActions;

  public static void Register(Action<GraphicsCommandList, long> update) => UpdateActions += update;

  public static void UnRegister(Action<GraphicsCommandList, long> update) => UpdateActions -= update;

  /// <summary>Invoke this to update all default buffers at the beginning of rendering command list.</summary>
  public static void UpdateAll(GraphicsCommandList cmd, long targetFence) => UpdateActions?.Invoke(cmd, targetFence);
}

static public class DelayReleaseManager
{
  private class Item
  {
    public long targetFence;
    public IDisposable item;
    public Item(long targetFence, IDisposable item)
    {
      this.targetFence = targetFence;
      this.item = item;
    }
  }

  private static Queue<Item> releaseQueue = new Queue<Item>();

  /// <summary>
  /// Enqueue an element for delay release. <br/>
  /// <b> NEVER hold enqueued element outside of DelayReleaseManager, otherwise memory leaks.</b>
  /// </summary>
  /// <param name="itemTargetFence"> Target fence of frame which item stayed in last. </param>
  public static void Enqueue(long itemTargetFence, IDisposable item) => releaseQueue.Enqueue(new Item(itemTargetFence, item));

  public static void Update(long completedFence)
  {
    if (completedFence == 0) return;
    while (releaseQueue.TryPeek(out Item obj))
    {
      // FIFO means if one element dequeued is uncompleted, remains also.
      if (completedFence < obj.targetFence) break;
      releaseQueue.Dequeue();
      obj.item.Dispose();
    }
  }
}