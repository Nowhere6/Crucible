using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Crucible;

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
      throw new InvalidOperationException("GC Handle should be freed manually.");
    }
  }

  /// <summary>
  /// Free GC handle.
  /// </summary>
  public void Free() => handle.Free();

  public static implicit operator IntPtr(NativePtr nativePtr) => nativePtr.ptr;
}

public static class PathHelper
{
  static readonly string ResourceRootPath;

  static PathHelper()
  {
    const string resourceFolderName = "Resources";
    string currentPath = Environment.CurrentDirectory;
    while (currentPath is not null)
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

  static public string GetPath(string name) => Path.Combine(ResourceRootPath, name);
}