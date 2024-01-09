using System;
using System.IO;
using System.Collections.Generic;
using SharpDX.IO;
using SharpDX.WIC;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

namespace SharpD12
{


  public class Texture
  {
    public int PixelWidth { get; private set; }
    public int TotalSize { get; private set; }
    public int MipCount { get; private set; }
    public int SrvIdx { get; private set; }

    public DefaultBuffer<byte> buffer;

    /// <summary>
    /// Create an empty texture.
    /// </summary>
    private Texture(Device device, Format format, int pixelSize, int totalSize, int pixelWidth, int mips)
    {
      if (mips < 1)
        throw new ArgumentException("The count of mipmaps must be greater than 0.");
      this.PixelWidth = pixelWidth;
      this.TotalSize = totalSize;
      this.MipCount = mips;
      buffer = new DefaultBuffer<byte>(device, totalSize, BufferDataType.Tex, format, pixelSize, pixelWidth, mips);

      ShaderResourceViewDescription desc = new ShaderResourceViewDescription()
      {
        Shader4ComponentMapping = GetMapping(),
        Format = format,
        Dimension = ShaderResourceViewDimension.Texture2D,
        Texture2D = { MipLevels = mips }
      };
      SrvIdx = SRV_Heap.CreateSRV(device, buffer.defaultHeap, desc);
    }

    public void Write(Byte[] data)
    {
      if (data.Length != TotalSize)
        throw new ArgumentException($"The size of input data should match the size of textue.");
      buffer.TextureWrite(data);
    }

    /////////////////////////////////////////////////////////
    ///                      Static                       ///
    /////////////////////////////////////////////////////////

    static ImagingFactory factory = new ImagingFactory();
    static Dictionary<string, Texture> texCollection = new Dictionary<string, Texture>();

    public static GpuDescriptorHandle GetHandle(string name)
    {
      if (texCollection.TryGetValue(name, out var tex))
      {
        return SRV_Heap.GetHandle_GPU(tex.SrvIdx);
      }
      else
      {
        throw new ArgumentException($"Texture named \"{name}\" does not exist.");
      }
    }

    public static void DeleteTexture(string name)
    {
      if (texCollection.TryGetValue(name, out Texture texture))
      {
        texCollection.Remove(name);
      }
      else
        throw new ArgumentException($"Texture named \"{name}\" does not exist.");
    }

    public static void Load_PNG_RGBA32_AutoMip(Device device, string loc, string name)
    {
      if (Path.GetExtension(loc) != ".png")
        throw new ArgumentException($"Bitmap isn't png format. loc={loc}");

      // Create bitmap object.
      Guid WIC_Format = PixelFormat.Format32bppRGBA;
      Format DXGI_Format = Format.R8G8B8A8_UNorm;
      int pixelSize = 4;
      var stream = new WICStream(factory, loc, NativeFileAccess.Read);
      var decoder = new PngBitmapDecoder(factory);
      decoder.Initialize(stream, DecodeOptions.CacheOnDemand);
      var rawFrame = decoder.GetFrame(0);
      var convertedFrame = new FormatConverter(factory);
      convertedFrame.Initialize(rawFrame, WIC_Format);
      var bitmap = new Bitmap(factory, convertedFrame, BitmapCreateCacheOption.CacheOnDemand);
      stream.Dispose();
      decoder.Dispose();
      rawFrame.Dispose();
      convertedFrame.Dispose();

      // Acquire parameters and verify them.
      int pixelWidth = bitmap.Size.Width;
      int size_mip0 = pixelWidth * pixelWidth * pixelSize;
      float power = MathF.Log2(pixelWidth);
      int mips = (int)power + 1;
      if (pixelWidth != bitmap.Size.Height)
        throw new ArgumentException($"Bitmap isn't square. loc={loc}");
      if (power != (int)power)
        throw new ArgumentException($"Width of bitmap is't power of two. loc={loc}");
      if (pixelWidth > 4096)
        throw new ArgumentException($"Width of bitmap is larger than 4096. loc={loc}");

      // Create byte array, acquire its address.
      int totalSize = 0;
      for (int w = pixelWidth; w > 0; w = w >> 1)
        totalSize += w * w * pixelSize;
      Byte[] data = new byte[totalSize];
      var nativePtr = new NativePtr(data);
      var ptr = nativePtr.Get();

      // Decode SRGB color before create scaler to make result right.
      var bitLock = bitmap.Lock(BitmapLockFlags.Write | BitmapLockFlags.Read);
      unsafe
      {
        byte* bitData = (byte*)bitLock.Data.DataPointer;
        for (int offset = 0; offset < size_mip0; offset += pixelSize)
        {
          FastDecodeSRGB(ref bitData[offset]);
          FastDecodeSRGB(ref bitData[offset + 1]);
          FastDecodeSRGB(ref bitData[offset + 2]);
        }
      }
      bitLock.Dispose();
      bitmap.CopyPixels(pixelWidth * pixelSize, ptr, size_mip0);
      ptr += size_mip0;

      // Generate mipmaps.
      for (int mip = 1; mip < mips; mip++)
      {
        int currPixelWidth = pixelWidth >> mip;
        int size_mip_i = currPixelWidth * currPixelWidth * pixelSize;
        var scaler = new BitmapScaler(factory);
        // Fant resampling mode is default mode to generate mip-maps in Texconv tool.
        scaler.Initialize(bitmap, currPixelWidth, currPixelWidth, BitmapInterpolationMode.Fant);
        if (scaler.PixelFormat != WIC_Format)
        {
          var converter = new FormatConverter(factory);
          converter.Initialize(scaler, WIC_Format);
          converter.CopyPixels(currPixelWidth * pixelSize, ptr, size_mip_i);
          converter.Dispose();
        }
        else
        {
          scaler.CopyPixels(currPixelWidth * pixelSize, ptr, size_mip_i);
        }
        scaler.Dispose();
        ptr += size_mip_i;
      }
      nativePtr.Free();

      // Register texture.
      Texture texture = new Texture(device, DXGI_Format, pixelSize, totalSize, pixelWidth, mips);
      texture.Write(data);
      texCollection.Add(name, texture);
    }

    public static void Load_PNG_R8_NoMip(Device device, string loc, string name)
    {
      if (Path.GetExtension(loc) != ".png")
        throw new ArgumentException($"Bitmap isn't png format. loc={loc}");

      // Create bitmap object.
      Guid WIC_Format = PixelFormat.Format8bppGray;
      Format DXGI_Format = Format.R8_UNorm;
      int pixelSize = 1;
      var stream = new WICStream(factory, loc, NativeFileAccess.Read);
      var decoder = new PngBitmapDecoder(factory);
      decoder.Initialize(stream, DecodeOptions.CacheOnDemand);
      var rawFrame = decoder.GetFrame(0);
      if (rawFrame.PixelFormat != PixelFormat.Format8bppGray)
        throw new Exception("Load_PNG_R8_NoMip() only read 8-bit grayscale image.");
      var bitmap = new Bitmap(factory, rawFrame, BitmapCreateCacheOption.CacheOnDemand);
      stream.Dispose();
      decoder.Dispose();
      rawFrame.Dispose();

      // Acquire parameters and verify them.
      int pixelWidth = bitmap.Size.Width;
      int size_mip0 = pixelWidth * pixelWidth;
      float power = MathF.Log2(pixelWidth);
      if (pixelWidth != bitmap.Size.Height)
        throw new ArgumentException($"Bitmap isn't square. loc={loc}");
      if (power != (int)power)
        throw new ArgumentException($"Width of bitmap is't power of two. loc={loc}");
      if (pixelWidth > 4096)
        throw new ArgumentException($"Width of bitmap is larger than 4096. loc={loc}");

      // Create byte array, acquire its address.
      Byte[] data = new byte[size_mip0];
      var nativePtr = new NativePtr(data);
      var ptr = nativePtr.Get();

      // Decode SRGB color before create scaler to make result right.
      //var bitLock = bitmap.Lock(BitmapLockFlags.Write | BitmapLockFlags.Read);
      //unsafe
      //{
      //  byte* bitData = (byte*)bitLock.Data.DataPointer;
      //  for (int offset = 0; offset < size_mip0; offset += 4)
      //  {
      //    FastDecodeSRGB(ref bitData[offset]);
      //    FastDecodeSRGB(ref bitData[offset + 1]);
      //    FastDecodeSRGB(ref bitData[offset + 2]);
      //  }
      //}
      //bitLock.Dispose();
      bitmap.CopyPixels(pixelWidth, ptr, size_mip0);
      //ptr += size_mip0;
      nativePtr.Free();

      // Register texture.
      Texture texture = new Texture(device, DXGI_Format, pixelSize, size_mip0, pixelWidth, 1);
      texture.Write(data);
      texCollection.Add(name, texture);
    }

    /// <summary>
    /// Specifies how memory gets routed by a shader resource view (SRV). The target channel order is RGBA (R is the least channel).
    /// </summary>
    static int GetMapping(byte r = 0, byte g = 1, byte b = 2, byte a = 3) => r | g << 3 | b << 6 | a << 9 | 1 << 12;

    static void FastDecodeSRGB(ref byte value)
    {
      float output = (float)value / byte.MaxValue;
      output = MathF.Pow(((output + 0.055f) / 1.055f), 2.4f);
      output = MathF.Round(output * byte.MaxValue);
      value = (byte)output;
    }
  }

  /// <summary> Manage the only SRV heap in game engine. </summary>
  public static class SRV_Heap
  {
    const int MaxSRVCount = 1024;

    static DescriptorHeap descHeap;
    static Queue<int> avaliableIndex = new Queue<int>();
    static CpuDescriptorHandle cpuHandle_0;
    static GpuDescriptorHandle gpuHandle_0;

    public static void Initialize(SharpDX.Direct3D12.Device dx12Device)
    {
      if (descHeap != null)
      {
        throw new Exception("SRV heap can be initialized only once.");
      }

      for (int i = 0; i < MaxSRVCount; i++)
        avaliableIndex.Enqueue(i);

      var heapType = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView;
      var cbvHeapDesc = new DescriptorHeapDescription { Type = heapType, DescriptorCount = MaxSRVCount, Flags = DescriptorHeapFlags.ShaderVisible };
      descHeap = dx12Device.CreateDescriptorHeap(cbvHeapDesc);
      cpuHandle_0 = descHeap.CPUDescriptorHandleForHeapStart;
      gpuHandle_0 = descHeap.GPUDescriptorHandleForHeapStart;
    }

    public static void Bind(GraphicsCommandList cmd) => cmd.SetDescriptorHeaps(descHeap);

    public static CpuDescriptorHandle GetHandle_CPU(int idx)
    {
      if (idx >= MaxSRVCount)
        throw new ArgumentOutOfRangeException(nameof(idx));
      return cpuHandle_0 + SD12Engine.CSUSize * idx;
    }

    public static GpuDescriptorHandle GetHandle_GPU(int idx)
    {
      if (idx >= MaxSRVCount)
        throw new ArgumentOutOfRangeException(nameof(idx));
      return gpuHandle_0 + SD12Engine.CSUSize * idx;
    }

    public static int CreateSRV(Device dx12Device, Resource res, ShaderResourceViewDescription srvDesc)
    {
      if (avaliableIndex.TryDequeue(out int idx))
      {
        dx12Device.CreateShaderResourceView(res, srvDesc, GetHandle_CPU(idx));
        return idx;
      }
      else
        throw new Exception($"SRV heap exhausted.");
    }

    public static void DeleteSRV(int idx)
    {
      if (avaliableIndex.Contains(idx))
        throw new ArgumentException("Target SRV does not exist.");
      avaliableIndex.Enqueue(idx);
    }
  }

  public static class StandardSampler
  {
    public static readonly StaticSamplerDescription[] value = new StaticSamplerDescription[]
      {
        new StaticSamplerDescription() // Point-Clamp (Post-processing)
        {
          ShaderRegister = 0,
          MaxLOD = 0,
          Filter = Filter.MinMagMipPoint,
          AddressUVW = TextureAddressMode.Clamp,
          ShaderVisibility = ShaderVisibility.All,
        },
        new StaticSamplerDescription() // Trilinear-Clamp (Post-processing / UI)
        {
          ShaderRegister = 1,
          MaxLOD = float.MaxValue,
          Filter = Filter.MinMagMipLinear,
          AddressUVW = TextureAddressMode.Clamp,
          ShaderVisibility = ShaderVisibility.All,
        },
        new StaticSamplerDescription() // Anisotropic-Wrap (Mesh)
        {
          ShaderRegister = 2,
          MaxLOD = float.MaxValue,
          Filter = Filter.Anisotropic,
          AddressUVW = TextureAddressMode.Wrap,
          ShaderVisibility = ShaderVisibility.All,
          MaxAnisotropy = AppConstants.AnisotropyLevel
        }
      };
  }
}
