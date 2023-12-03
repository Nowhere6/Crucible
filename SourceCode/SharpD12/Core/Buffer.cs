using System;
using SharpDX;
using SharpDX.DXGI;

namespace SharpD12
{
  using SharpDX.Direct3D12;
  using System.Runtime.InteropServices;

  public enum UploadBufferType
  {
    CB, // Constant buffer
    Tex, // Texture
    VBIB // Vertex buffer or index buffer
  }
  /// <summary>
  /// Upload heap manager.
  /// </summary>
  /// <typeparam name="T">Element type.</typeparam>
  public class UploadBuffer<T> where T : struct
  {
    readonly UploadBufferType bufferType;
    readonly int elementSize;
    readonly int totalSize;
    readonly int count;
    readonly IntPtr mappedPtr;
    readonly long gpuAddr;
    public Resource uploadHeap;

    public int ElementSize { get => elementSize; }
    public int Size { get => totalSize; }

    /// <summary>
    /// Create upload buffer for constant/vertex/index buffer.
    /// </summary>
    public UploadBuffer(Device dx12Device, int elementCount, bool isConstantBuffer)
    {
      elementSize = Utilities.SizeOf<T>();
      bufferType = isConstantBuffer ? UploadBufferType.CB : UploadBufferType.VBIB;
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
    public UploadBuffer(Device dx12Device, int widthPixels, int mipmaps)
    {
      bufferType = UploadBufferType.Tex;
      var resDesc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, widthPixels, widthPixels, 1, (short)mipmaps);
      var props = new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0);
      var state = ResourceStates.GenericRead;
      uploadHeap = dx12Device.CreateCommittedResource(props, HeapFlags.None, resDesc, state);
      gpuAddr = uploadHeap.GPUVirtualAddress;
    }

    ~UploadBuffer()
    {
      // Mapping data will be invalid automatically after ID3D12Resource is disposed.
      Utilities.Dispose<Resource>(ref uploadHeap);
    }

    public long GetGPUAddress(int index = 0) => gpuAddr + index * elementSize;

    public void Write(int DestIndex, ref T data) => Utilities.Write<T>(mappedPtr + DestIndex * elementSize, ref data);

    public void Write(int DestIndex, T[] data) => Utilities.Write<T>(mappedPtr + DestIndex * elementSize, data, 0, data.Length);

    public void Write(int DestIndex, T[] data, int srcIndex, int srcCount) => Utilities.Write<T>(mappedPtr + DestIndex * elementSize, data, srcIndex, srcCount);

    /// <summary>
    /// Align up for constant buffers.
    /// </summary>
    int CbAlignUp(int size) => size + 255 & ~255;
  }

  /// <summary>
  /// Default heap manager.
  /// </summary>
  /// <typeparam name="T">Element type.</typeparam>
  public class DefaultBuffer<T> where T : struct
  {
    bool needCopyRegion = false;
    readonly int widthPixels;
    readonly int mipmaps;
    public UploadBuffer<T> intermediateBuffer;
    public Resource defaultHeap;
    public readonly bool isTexture;

    public int Size => intermediateBuffer.Size;

    public DefaultBuffer(Device dx12Device, int bytes, bool isTexture, int widthPixels = 0, int mipmaps = 0)
    {
      if (isTexture && typeof(T) != typeof(byte))
        throw new ArgumentException("This default buffer accommodate texture, but T is not byte.");
      if (isTexture && (widthPixels <= 0 || mipmaps <= 0))
        throw new ArgumentException("This default buffer accommodate texture, but widthBytes or mipmaps is invalid.");

      this.isTexture = isTexture;
      this.widthPixels = widthPixels;
      this.mipmaps = mipmaps;

      if (isTexture)
      {
        intermediateBuffer = new UploadBuffer<T>(dx12Device, widthPixels, mipmaps);
      }
      else
      {
        intermediateBuffer = new UploadBuffer<T>(dx12Device, bytes / Utilities.SizeOf<T>(), false);
      }

      var props = new HeapProperties(HeapType.Default);
      var state = ResourceStates.GenericRead;
      ResourceDescription desc;
      if (isTexture)
      {
        desc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, widthPixels, widthPixels, 1, (short)mipmaps);
      }
      else
      {
        desc = ResourceDescription.Buffer(bytes);
      }
      defaultHeap = dx12Device.CreateCommittedResource(props, HeapFlags.None, desc, state);

      // Subscribe update event.
      DefaultHeapManager.Subscribe(UpdateAction);
    }

    private void UpdateAction(GraphicsCommandList cmd)
    {
      if (needCopyRegion)
        needCopyRegion = false;
      else
        return;

      // Before barrier
      cmd.ResourceBarrier(new ResourceTransitionBarrier(intermediateBuffer.uploadHeap, ResourceStates.GenericRead, ResourceStates.CopySource));
      cmd.ResourceBarrier(new ResourceTransitionBarrier(defaultHeap, ResourceStates.GenericRead, ResourceStates.CopyDestination));
      if (isTexture)
      {
        for ( int i = 0; i < mipmaps; i++)
        {
          cmd.CopyTextureRegion(new TextureCopyLocation(defaultHeap, i), 0, 0, 0, new TextureCopyLocation(intermediateBuffer.uploadHeap, i), null);
        }
      }
      else
      {
        cmd.CopyBufferRegion(defaultHeap, 0, intermediateBuffer.uploadHeap, 0, intermediateBuffer.Size);
      }

      // After barrier
      cmd.ResourceBarrier(new ResourceTransitionBarrier(intermediateBuffer.uploadHeap, ResourceStates.CopySource, ResourceStates.GenericRead));
      cmd.ResourceBarrier(new ResourceTransitionBarrier(defaultHeap, ResourceStates.CopyDestination, ResourceStates.GenericRead));
    }

    public void Write(int DestIndex, ref T data)
    {
      needCopyRegion = true;
      intermediateBuffer.Write(DestIndex, ref data);
    }

    public void Write(int DestIndex, T[] data)
    {
      needCopyRegion = true;
      intermediateBuffer.Write(DestIndex, data);
    }

    public void Write(int DestIndex, T[] data, int srcIndex, int srcCount)
    {
      needCopyRegion = true;
      intermediateBuffer.Write(DestIndex, data, srcIndex, srcCount);
    }

    public void TextureWrite(byte[] data)
    {
      needCopyRegion = true;
      if (!isTexture)
        throw new NotSupportedException("Only texture default buffer can invoke this function.");

      int width = widthPixels;
      var nativePtr = new NativePtr(data);
      var ptr = nativePtr.Get();
      for (int i = 0; i < mipmaps; i++)
      {
        int bytes = width * width * 4;
        intermediateBuffer.uploadHeap.WriteToSubresource(i, null, ptr, width * 4, bytes);
        width /= 2;
        ptr += bytes;
      }
      nativePtr.Free();
    }
  }

  public static class DefaultHeapManager
  {
    private static event Action<GraphicsCommandList> updateActions;

    public static void Subscribe(Action<GraphicsCommandList> action) => updateActions += action;

    /// <summary>
    /// Update all default heaps, <b>should be invoked only once before drawing.</b>
    /// </summary>
    public static void UpdateAll(GraphicsCommandList cmd) => updateActions?.Invoke(cmd);
  }
}