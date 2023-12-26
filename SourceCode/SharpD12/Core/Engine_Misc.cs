using Accessibility;
using SharpDX.D3DCompiler;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SharpD12
{
  /// <summary>
  /// Help D3DCompiler to open HLSL header.<br/>
  /// Only support relative location for "#include" currently.
  /// </summary>
  public class HLSLInclude : SharpDX.D3DCompiler.Include
  {
    string rootDir;

    public IDisposable Shadow { get; set; }

    public HLSLInclude(string rootFolder) => rootDir = rootFolder;

    ~HLSLInclude() => Dispose();

    public void Close(Stream stream) => stream?.Dispose();

    public void Dispose() => Shadow?.Dispose();

    public Stream Open(IncludeType type, string fileName, Stream parentStream)
    {
      string includeDir = Path.Combine(rootDir, fileName);
      return new FileStream(includeDir, FileMode.Open, FileAccess.Read);
    }
  }

  /// <summary>
  /// Native pointer wrapper class, <b>should free GC handle manually by call Free().</b>
  /// </summary>
  public class NativePtr
  {
    GCHandle handle;
    IntPtr ptr;

    public NativePtr(byte[] array)
    {
      handle = GCHandle.Alloc(array, GCHandleType.Pinned);
      ptr = Marshal.UnsafeAddrOfPinnedArrayElement(array, 0);
    }

    ~NativePtr()
    {
      if (handle.IsAllocated == true)
      {
        throw new InvalidOperationException("GC Handle has not be freed.");
      }
    }

    /// <summary>
    /// Free GC handle.
    /// </summary>
    public void Free() => handle.Free();

    public IntPtr Get() => ptr;
  }

  public static class PathHelper
  {
    static readonly string ResourceRootPath;
    
    static PathHelper()
    {
      const string resourceFolderName = "Resources";
      string currentPath = Environment.CurrentDirectory;
      while(currentPath is not null)
      {
        string searchPath = Path.Combine(currentPath, resourceFolderName);
        if (Directory.Exists(searchPath))
        {
          ResourceRootPath = searchPath;
          return;
        }
        currentPath = Directory.GetParent(currentPath)?.FullName;
      }
      throw new DirectoryNotFoundException("\"Resources\" folder does not exist.");
    }

    static public string GetPath(string name)
    {
      return Path.Combine(ResourceRootPath, name);
    }
  }
}
