using SharpDX;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpD12
{
  using SharpDX.Direct3D12;
  using SharpDX.IO;
  using SharpDX.Mathematics.Interop;
  using SharpDX.WIC;
  using System.Windows.Forms;
  using static ProgramDefinedConstants;
  public class Texture
  {
    public int widthPixels { get; private set; }
    public int Mipmaps { get; private set; }
    public int bytes { get; private set; }
    public CpuDescriptorHandle cpuDescriptor;
    public GpuDescriptorHandle gpuDescriptor;


    public DefaultBuffer<byte> buffer;

    /// <summary>
    /// Create an empty texture.
    /// </summary>
    public Texture(Device device, int bytes, int widthPixels, int mips)
    {
      this.widthPixels = widthPixels;
      this.Mipmaps = mips;
      this.bytes = bytes;
      buffer = new DefaultBuffer<byte>(device, bytes, true, widthPixels, mips);
    }

    public void Write(Byte[] data)
    {
      if (data.Length != bytes)
        throw new ArgumentException($"Texture has {buffer.Size} bytes, but data for writing only has {data.Length} bytes.");
      buffer.TextureWrite(data);
    }
  }

  public static class TextureManager
  {
    static ImagingFactory factory = new ImagingFactory();
    static public Dictionary<string, Texture> textures = new Dictionary<string, Texture>();

    public static void LoadPNG(Device device, string loc, string name)
    {
      if (Path.GetExtension(loc) != ".png")
        throw new ArgumentException($"Bitmap isn't png format. loc={loc}");

      // Create bitmap object.
      var stream = new WICStream(factory, loc, NativeFileAccess.Read);
      var decoder = new PngBitmapDecoder(factory);
      decoder.Initialize(stream, DecodeOptions.CacheOnDemand);
      var rawFrame = decoder.GetFrame(0);
      var convertedFrame = new FormatConverter(factory);
      convertedFrame.Initialize(rawFrame, PixelFormat.Format32bppRGBA);
      var bitmap = new Bitmap(factory, convertedFrame, BitmapCreateCacheOption.CacheOnDemand);
      stream.Dispose();
      decoder.Dispose();
      rawFrame.Dispose();
      convertedFrame.Dispose();

      // Acquire parameters and verify them.
      int width = bitmap.Size.Width;
      float power = MathF.Log2(width);
      int mips = (int)power + 1;
      if (width != bitmap.Size.Height)
        throw new ArgumentException($"Bitmap isn't square. loc={loc}");
      if (power - (int)power != 0)
        throw new ArgumentException($"Width of bitmap is't power of two. loc={loc}");
      if (width > 4096)
        throw new ArgumentException($"Width of bitmap is larger than 4096. loc={loc}");

      // Decode SRGB color before create scaler to make result right.
      var bitmapLock = bitmap.Lock(BitmapLockFlags.Write);
      var addr = bitmapLock.Data.DataPointer;
      int originalPixelCount = width * width;
      for (int i = 0; i < originalPixelCount; i++)
      {
        for (int j = 0; j < 3; j++)
        {
          byte value = DecodeSRGB(Utilities.Read<byte>(addr + j));
          Utilities.Write<byte>(addr + j, ref value);
        }
        addr += 4;
      }
      bitmapLock.Dispose();

      // Read png data into byte array.
      int pixelCount = 0;
      for (int w = width; w > 0; w = w >> 1)
      {
        pixelCount += w * w;
      }
      Byte[] data = new byte[pixelCount * 4];
      var nativePtr = new NativePtr(data);
      var ptr = nativePtr.Get();
      bitmap.CopyPixels(width * 4, ptr, originalPixelCount * 4);
      ptr += originalPixelCount * 4;

      // Generate mipmaps.
      for (int mip = 1; mip < mips; mip++)
      {
        int w = width >> mip;
        int size = w * w * 4;
        var scaler = new BitmapScaler(factory);
        // Fant resampling mode is default mode to generate mip-maps in Texconv tool.
        scaler.Initialize(bitmap, w, w, BitmapInterpolationMode.Fant);
        if (scaler.PixelFormat != PixelFormat.Format32bppRGBA)
        {
          var converter = new FormatConverter(factory);
          converter.Initialize(scaler, PixelFormat.Format32bppRGBA);
          converter.CopyPixels(w * 4, ptr, size);
          converter.Dispose();
        }
        else
        {
          scaler.CopyPixels(w * 4, ptr, size);
        }
        scaler.Dispose();
        ptr += size;
      }
      nativePtr.Free();

      // Decode SRGB (Wrong!)
      //for (int i = 0; i < pixelCount; i++)
      //{
      //  data[4 * i] = DecodeSRGB(data[4 * i]);
      //  data[4 * i + 1] = DecodeSRGB(data[4 * i + 1]);
      //  data[4 * i + 2] = DecodeSRGB(data[4 * i + 2]);
      //}

      // Register texture.
      Texture texture = new Texture(device, data.Length, width, mips);
      texture.Write(data);
      ShaderResourceViewDescription desc = new ShaderResourceViewDescription()
      {
        Shader4ComponentMapping = GetMapping(),
        Format = Format.R8G8B8A8_UNorm,
        Dimension = ShaderResourceViewDimension.Texture2D,
        Texture2D = { MipLevels = mips }
      };
      device.CreateShaderResourceView(texture.buffer.defaultHeap, desc, FrameResource.srvDescHeap.CPUDescriptorHandleForHeapStart + (MaxRenderItems + 1) * SwapChainSize * SD12Engine.CSUSize);
      texture.cpuDescriptor = FrameResource.srvDescHeap.CPUDescriptorHandleForHeapStart + (MaxRenderItems + 1) * SwapChainSize * SD12Engine.CSUSize;
      texture.gpuDescriptor = FrameResource.srvDescHeap.GPUDescriptorHandleForHeapStart + (MaxRenderItems + 1) * SwapChainSize * SD12Engine.CSUSize;
      textures.Add(name, texture);
    }

    /// <summary>
    /// Specifies how memory gets routed by a shader resource view (SRV). The target channel order is RGBA (R is the least channel).
    /// </summary>
    static int GetMapping(byte r = 0, byte g = 1, byte b = 2, byte a = 3) => r | g << 3 | b << 6 | a << 9 | 1 << 12;

    static byte DecodeSRGB(byte value)
    {
      float output = (float)value / byte.MaxValue;
      output = MathF.Pow(((output + 0.055f) / 1.055f), 2.4f);
      output = MathF.Round(output * byte.MaxValue);
      return (byte)output;
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
          MaxAnisotropy = ProgramDefinedConstants.AnisotropyLevel
        }
      };
  }
}
