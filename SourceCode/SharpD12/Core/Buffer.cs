using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using D12Device = SharpDX.Direct3D12.Device;
using D12Resource = SharpDX.Direct3D12.Resource;

namespace SharpD12;

public enum BufferDataType
{
  CB, // Constant buffer
  Tex, // Texture
  VBIB // Vertex buffer or index buffer
}

/// <summary> Generic upload heap wrapper class. </summary>
public class UploadBuffer<T> : IDisposable where T : struct
{
  readonly BufferDataType bufferType;
  readonly int elementSize;
  readonly int totalSize;
  readonly int count;
  readonly IntPtr mappedPtr;
  readonly long gpuAddr;
  public D12Resource uploadHeap;

  public int ElementSize { get => elementSize; }
  public int Size { get => totalSize; }

  /// <summary>
  /// Create upload buffer for constant/vertex/index buffer.
  /// </summary>
  public UploadBuffer(D12Device dx12Device, int elementCount, bool isConstantBuffer)
  {
    elementSize = Utilities.SizeOf<T>();
    bufferType = isConstantBuffer ? BufferDataType.CB : BufferDataType.VBIB;
    // Constant buffer elements need to be multiples of 256 bytes.
    // This is because the hardware can only view constant data 
    // at m*256 byte offsets and of n*256 byte lengths. 
    if (isConstantBuffer)
      elementSize = CbAlignUp(elementSize);
    count = elementCount;
    totalSize = elementSize * count;
    var resDesc = ResourceDescription.Buffer(totalSize);
    var props = new HeapProperties(HeapType.Upload);
    var state = ResourceStates.GenericRead;
    uploadHeap = dx12Device.CreateCommittedResource(props, HeapFlags.None, resDesc, state);
    gpuAddr = uploadHeap.GPUVirtualAddress;
    mappedPtr = uploadHeap.Map(0);
  }

  /// <summary>
  /// Create special intermediate buffer for texture.<br/>
  /// <b>Texture resource cannot be created on upload heap.</b>
  /// </summary>
  public UploadBuffer(D12Device dx12Device, Format format, int widthPixels, int mipmaps)
  {
    bufferType = BufferDataType.Tex;
    var resDesc = ResourceDescription.Texture2D(format, widthPixels, widthPixels, 1, (short)mipmaps);
    var props = new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0);
    var state = ResourceStates.CopySource;
    uploadHeap = dx12Device.CreateCommittedResource(props, HeapFlags.None, resDesc, state);
  }

  public void Dispose() => uploadHeap.Dispose();

  public long GetGPUAddress(int index = 0) => gpuAddr + index * elementSize;

  public void Write(int DestIndex, ref T data)
  {
    if (bufferType == BufferDataType.Tex)
      throw new NotSupportedException("Only CB/IB/VB buffer can invoke this.");
    Utilities.Write<T>(mappedPtr + DestIndex * elementSize, ref data);
  }

  public void Write(int DestIndex, T[] data, int srcIndex = 0, int srcCount = 0)
  {
    if (srcIndex + srcCount > data.Length)
      throw new ArgumentOutOfRangeException(nameof(srcCount));
    if (srcIndex == 0 && srcCount == 0)
      srcCount = data.Length;

    if (bufferType == BufferDataType.CB)
    {
      for (int i = 0; i < srcCount; i++)
      {
        Utilities.Write<T>(mappedPtr + (DestIndex + i) * elementSize, data, srcIndex + i, 1);
      }
    }
    else if (bufferType == BufferDataType.VBIB)
    {
      Utilities.Write<T>(mappedPtr + DestIndex * elementSize, data, srcIndex, srcCount);
    }
    else
    {
      throw new NotSupportedException("Only CB/IB/VB buffer can invoke this.");
    }
  }

  /// <summary>
  /// Align up for constant buffers.
  /// </summary>
  static int CbAlignUp(int size) => size + 255 & ~255;
}

/// <summary> Generic default heap wrapper class. </summary>
public class DefaultBuffer<T> : IDisposable where T : struct
{
  public UploadBuffer<T> middleBuffer;
  public readonly BufferDataType bufferType;
  public D12Resource defaultHeap;
  readonly int pixelWidth;
  readonly bool readOnly;
  readonly int pixelSize;
  readonly int mipCount;
  readonly int size;
  bool dirty;

  public int Size => size;

  public DefaultBuffer(D12Device dx12Device, int bytes, BufferDataType bufferType, bool isReadonly, Format format = Format.R8G8B8A8_UNorm, int pixelSize = 0, int pixelWidth = 0, int mipmaps = 0)
  {
    // initialize values and create upload buffer.
    this.bufferType = bufferType;
    this.readOnly = isReadonly;
    this.dirty = false;
    if (bufferType == BufferDataType.Tex)
    {
      if (typeof(T) != typeof(byte))
        throw new ArgumentException("This default buffer accommodate texture, but T is not byte.");
      if (pixelWidth <= 0 || mipmaps <= 0)
        throw new ArgumentException("This default buffer accommodate texture, but widthBytes or mipmaps is invalid.");
      this.pixelWidth = pixelWidth;
      this.pixelSize = pixelSize;
      this.mipCount = mipmaps;
      middleBuffer = new UploadBuffer<T>(dx12Device, format, pixelWidth, mipmaps);
    }
    else middleBuffer = new UploadBuffer<T>(dx12Device, bytes / Utilities.SizeOf<T>(), bufferType == BufferDataType.CB);
    this.size = middleBuffer.Size;

    // Create defualt heap.
    var props = new HeapProperties(HeapType.Default);
    var state = ResourceStates.GenericRead;
    ResourceDescription desc;
    if (bufferType == BufferDataType.Tex)
    {
      desc = ResourceDescription.Texture2D(format, pixelWidth, pixelWidth, 1, (short)mipmaps);
    }
    else
    {
      desc = ResourceDescription.Buffer(bytes);
    }
    defaultHeap = dx12Device.CreateCommittedResource(props, HeapFlags.None, desc, state);

    // Subscribe update event.
    DefaultBufferUpdater.Register(UpdateAction);
  }

  public void Dispose()
  {
    DefaultBufferUpdater.UnRegister(UpdateAction);
    if (middleBuffer != null) middleBuffer.Dispose();
    defaultHeap.Dispose();
  }

  private void UpdateAction(GraphicsCommandList cmd, long targetFence)
  {
    if (dirty) dirty = false;
    else return;

    // Before barrier
    cmd.ResourceBarrier(new ResourceTransitionBarrier(defaultHeap, ResourceStates.GenericRead, ResourceStates.CopyDestination));
    if (bufferType == BufferDataType.Tex)
    {
      for (int i = 0; i < mipCount; i++)
      {
        cmd.CopyTextureRegion(new TextureCopyLocation(defaultHeap, i), 0, 0, 0, new TextureCopyLocation(middleBuffer.uploadHeap, i), null);
      }
    }
    else
    {
      cmd.CopyBufferRegion(defaultHeap, 0, middleBuffer.uploadHeap, 0, middleBuffer.Size);
    }

    // After barrier
    cmd.ResourceBarrier(new ResourceTransitionBarrier(defaultHeap, ResourceStates.CopyDestination, ResourceStates.GenericRead));

    // Release middle heap after update of read-only buffer.
    if (readOnly)
    {
      DefaultBufferUpdater.UnRegister(UpdateAction);
      DelayReleaseManager.Enqueue(targetFence, middleBuffer);
      middleBuffer = null;
    }
  }

  public void Write(int DestIndex, ref T data)
  {
    WriteCheck();
    if (bufferType == BufferDataType.Tex)
      throw new NotSupportedException("Only default buffer not of texture can invoke this.");
    middleBuffer.Write(DestIndex, ref data);
  }

  public void Write(int DestIndex, T[] data, int srcIndex = 0, int srcCount = 0)
  {
    WriteCheck();
    if (bufferType == BufferDataType.Tex)
      throw new NotSupportedException("Only default buffer not of texture can invoke this.");
    if (srcIndex == 0 && srcCount == 0)
      srcCount = data.Length;
    middleBuffer.Write(DestIndex, data, srcIndex, srcCount);
  }

  public void TextureWrite(byte[] data)
  {
    WriteCheck();
    if (bufferType != BufferDataType.Tex)
      throw new NotSupportedException("Only default buffer of texture can invoke this.");

    int width = pixelWidth;
    var nativePtr = new NativePtr(data);
    var ptr = nativePtr.Get();
    for (int mip = 0; mip < mipCount; mip++)
    {
      width >>= mip;
      int rowPitch = width * pixelSize;
      int depthPitch = rowPitch * width;
      middleBuffer.uploadHeap.WriteToSubresource(mip, null, ptr, rowPitch, depthPitch);
      ptr += depthPitch;
    }
    nativePtr.Free();
  }

  void WriteCheck()
  {
    if (readOnly && middleBuffer == null)
      throw new NotSupportedException("Middle buffer of read-only default buffer has already released.");
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