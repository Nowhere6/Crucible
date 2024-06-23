using System;
using System.IO;
using System.Collections.Generic;
using SharpDX.IO;
using SharpDX.WIC;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using D12Device = SharpDX.Direct3D12.Device;
using Microsoft.VisualBasic.Logging;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Crucible;

public class TextureInfo
{
  public readonly int Width;
  public readonly int MipSliceCount;
  public readonly int ArraySliceCount;
  public readonly bool IsCubemap;
  public readonly Format DXGIFormat;
  public readonly int PixelSize;
  public readonly int MipZeroSize;
  public readonly int MipSliceSize;
  public readonly int TotalSize;

  public bool HasMips => MipSliceCount > 1;

  public TextureInfo(int width, bool HasMips, bool isCubemap, Format dXGIFormat, int arraySliceCount = 1)
  {
    Width = width;
    float power = MathF.Log2(width);
    if (power != (int)power) throw new ArgumentException($"Width should be 2^n.");
    MipSliceCount = HasMips ? (int)power + 1 : 1;
    ArraySliceCount = arraySliceCount;
    IsCubemap = isCubemap;
    if (isCubemap) arraySliceCount = 6;
    DXGIFormat = dXGIFormat;
    switch (dXGIFormat)
    {
      case Format.R32_Float:
      case Format.R8G8B8A8_UNorm:
      case Format.R10G10B10A2_UNorm:
        PixelSize = 4;
        break;
      case Format.R8_UNorm:
        PixelSize = 1;
        break;
      default:
        throw new ArgumentException($"Format <{DXGIFormat}> is not supported.");
    }
    MipZeroSize = width * width * PixelSize;
    MipSliceSize = HasMips ? (((1 << 2 * MipSliceCount) - 1) / 3) * PixelSize : MipZeroSize;
    TotalSize = MipSliceSize * arraySliceCount;
  }
}

public class Texture
{
  public readonly TextureInfo Info;
  public DefaultBuffer<byte> buffer;
  static Guid WIC_R8 = PixelFormat.Format8bppGray;
  static Guid WIC_RGBA32 = PixelFormat.Format32bppRGBA;
  static Format DXGI_R8 = Format.R8_UNorm;
  static Format DXGI_RGBA32 = Format.R8G8B8A8_UNorm;

  public ushort SrvIdx { get; private set; }

  /// <summary>
  /// Create an empty texture.
  /// </summary>
  private Texture(D12Device device, TextureInfo info)
  {
    Info = info;
    buffer = new DefaultBuffer<byte>(device, 0, BufferType.Texture, true, info);

    ShaderResourceViewDescription desc = new ShaderResourceViewDescription()
    {
      Shader4ComponentMapping = GetMapping(),
      Format = info.DXGIFormat,
    };
    if (info.IsCubemap)
    {
      desc.Dimension = ShaderResourceViewDimension.TextureCube;
      desc.Texture2DArray.MipLevels = info.MipSliceCount;
    }
    else
    {
      desc.Dimension = ShaderResourceViewDimension.Texture2D;
      desc.Texture2D.MipLevels = info.MipSliceCount;
    }
    SrvIdx = DescHeapManager.CreateView(device, buffer.Heap, desc, ViewType.SRV);
  }

  public void Write(Byte[] data, int arraySliceIndex = 0)
  {
    if (data.Length != Info.MipSliceSize)
      throw new ArgumentException($"The input data size should match the mip slice size.");
    if (arraySliceIndex < 0 || arraySliceIndex > Info.MipSliceCount - 1)
      throw new ArgumentException($"Array Index <{arraySliceIndex}> is invalid.");
    buffer.TextureWrite(data, arraySliceIndex);
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
      return DescHeapManager.GetGPUHandle(tex.SrvIdx, ViewType.SRV);
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

  public static void Load_CubemapRGBA32AutoMip(D12Device device, string folderLoc, string name)
  {
    if (!Directory.Exists(folderLoc))
      throw new ArgumentException();
    string[] files = Directory.GetFiles(folderLoc);
    if (files.Length != 6)
      throw new ArgumentException();
    string extension = Path.GetExtension(files[0]);
    string[] locations = new string[6]
    {
      Path.Combine(folderLoc, "posx", extension),
      Path.Combine(folderLoc, "negx", extension),
      Path.Combine(folderLoc, "posy", extension),
      Path.Combine(folderLoc, "negy", extension),
      Path.Combine(folderLoc, "posz", extension),
      Path.Combine(folderLoc, "negz", extension)
    };
    byte[][] arraySlices = new byte[6][];
    TextureInfo info = default;
    for (int i = 0; i < 6; i++)
    {
      CreateBitmap(out var bitmap, out int width, locations[i], WIC_RGBA32);
      if (i == 0)
      {
        info = new TextureInfo(width, true, true, DXGI_RGBA32);
      }
      else if (width != info.Width) throw new ArgumentException();
      CreateDataArray(out byte[] tempData, bitmap, info);
      GenerateMipmaps(bitmap, tempData, WIC_RGBA32, info);
      arraySlices[i] = tempData;
    }
    // Register texture.
    Texture texture = new Texture(device, info);
    for (int i = 0; i < 6; i++) texture.Write(arraySlices[i], i);
    texCollection.Add(name, texture);
  }

  public static void Load_RGBA32AutoMip(D12Device device, string loc, string name)
  {
    CreateBitmap(out var bitmap, out int width, loc, WIC_RGBA32);
    TextureInfo info = new TextureInfo(width, true, false, DXGI_RGBA32);
    CreateDataArray(out byte[] data, bitmap, info);
    GenerateMipmaps(bitmap, data, WIC_RGBA32, info);
    // Register texture.
    Texture texture = new Texture(device, info);
    texture.Write(data);
    texCollection.Add(name, texture);
  }

  public static void Load_R8NoMip(D12Device device, string loc, string name)
  {
    CreateBitmap(out var bitmap, out int width, loc, WIC_R8);
    TextureInfo info = new TextureInfo(width, false, false, DXGI_R8);
    CreateDataArray(out byte[] data, bitmap, info);
    // Register texture.
    Texture texture = new Texture(device, info);
    texture.Write(data);
    texCollection.Add(name, texture);
  }

  /// <summary>
  /// Specifies how memory gets routed by a shader resource view (SRV). The target channel order is RGBA (R is the least channel).
  /// </summary>
  public static int GetMapping(byte r = 0, byte g = 1, byte b = 2, byte a = 3) => r | g << 3 | b << 6 | a << 9 | 1 << 12;

  static void FastDecodeSRGB(ref byte value)
  {
    float output = (float)value / byte.MaxValue;
    output = MathF.Pow(((output + 0.055f) / 1.055f), 2.4f);
    output = MathF.Round(output * byte.MaxValue);
    value = (byte)output;
  }

  static void CreateBitmap(out Bitmap bitmap, out int width, string loc, Guid targetFormat)
  {
    string extension = Path.GetExtension(loc);
    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
      throw new ArgumentException($"The extension of a texture only could be png/jpg/jpeg. File Location={loc}");
    // Create bitmap object.
    var stream = new WICStream(factory, loc, NativeFileAccess.Read);
    BitmapDecoder decoder = extension == ".png" ? new PngBitmapDecoder(factory) : new JpegBitmapDecoder(factory);
    decoder.Initialize(stream, DecodeOptions.CacheOnDemand);
    var rawFrame = decoder.GetFrame(0);
    FormatConverter convertedFrame = null;
    bool isSameFormat = rawFrame.PixelFormat == targetFormat;
    if (!isSameFormat)
    {
      convertedFrame = new FormatConverter(factory);
      convertedFrame.Initialize(rawFrame, targetFormat);
    }
    bitmap = new Bitmap(factory, isSameFormat ? rawFrame : convertedFrame, BitmapCreateCacheOption.CacheOnDemand);
    stream.Dispose();
    decoder.Dispose();
    rawFrame.Dispose();
    convertedFrame?.Dispose();
    // Verify the width.
    width = bitmap.Size.Width;
    if (width != bitmap.Size.Height)
      throw new ArgumentException($"The bitmap isn't a square. File Location={loc}");
    if (width > 4096)
      throw new ArgumentException($"The width cannot be larger than 4096.");
  }

  /// <summary>
  /// Acquire parameters of the bitmap and verify them.
  /// </summary>
  static void CreateDataArray(out byte[] data, Bitmap bitmap, TextureInfo info)
  {
    data = new byte[info.TotalSize];
    NativePtr nativePtr = new NativePtr(data);
    // Grayscale textures also need to be decoded the SRGB color.
    DecodeSRGBforMip0(bitmap, nativePtr, info);
    nativePtr.Free();
  }

  /// <summary>
  /// Decode the SRGB color for the mip 0.
  /// <br/>
  /// <b>Do this before generating mip maps.</b>
  /// </summary>
  static void DecodeSRGBforMip0(Bitmap bitmap, IntPtr ptr, TextureInfo info)
  {
    var bitLock = bitmap.Lock(BitmapLockFlags.Write | BitmapLockFlags.Read);
    unsafe
    {
      byte* bitData = (byte*)bitLock.Data.DataPointer;
      for (int offset = 0; offset < info.MipZeroSize; offset += info.PixelSize)
      {
        FastDecodeSRGB(ref bitData[offset]);
        if (info.PixelSize == 1) continue;
        if (info.PixelSize == 4)
        {
          FastDecodeSRGB(ref bitData[offset + 1]);
          FastDecodeSRGB(ref bitData[offset + 2]);
        }
        else throw new ArgumentException();
      }
    }
    bitLock.Dispose();
    bitmap.CopyPixels(info.Width * info.PixelSize, ptr, info.MipZeroSize);
  }

  static void GenerateMipmaps(Bitmap bitmap, Byte[] data, Guid WICFormat, TextureInfo info)
  {
    NativePtr nativePtr = new NativePtr(data);
    IntPtr ptr = nativePtr;
    ptr += info.MipZeroSize;
    for (int mip = 1; mip < info.MipSliceCount; mip++)
    {
      int currWidth = info.Width >> mip;
      int currMipSize = info.MipZeroSize >> (2 * mip);
      var scaler = new BitmapScaler(factory);
      // Fant resampling mode is the default mode generting mip-maps in Texconv tool.
      scaler.Initialize(bitmap, currWidth, currWidth, BitmapInterpolationMode.Fant);
      if (scaler.PixelFormat != WICFormat)
      {
        var converter = new FormatConverter(factory);
        converter.Initialize(scaler, WICFormat);
        converter.CopyPixels(currWidth * info.PixelSize, ptr, currMipSize);
        converter.Dispose();
      }
      else
      {
        scaler.CopyPixels(currWidth * info.PixelSize, ptr, currMipSize);
      }
      scaler.Dispose();
      ptr += currMipSize;
    }
    nativePtr.Free();
  }
}
