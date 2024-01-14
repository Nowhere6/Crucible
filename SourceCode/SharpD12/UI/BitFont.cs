using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using static System.BitConverter;
using SharpDX;
using Device = SharpDX.Direct3D12.Device;
using SharpDX.Direct2D1;

namespace SharpD12.UI
{
  public static class BitFont
  {
    private class CharData
    {
      public readonly Vector2 uvSize;
      public readonly Vector2 uvPos;
      public readonly Vector2 offset;
      public readonly float advance;
      public readonly Vector2 size;

      public CharData(Vector2 uvPos, Vector2 uvSize, Vector2 size, Vector2 offset, float advance)
      {
        this.uvSize = uvSize;
        this.uvPos = uvPos;
        this.offset = offset;
        this.advance = advance;
        this.size = size;
      }
    }

    const char FallbackChar = '?';
    const char LineBreakChar = '\n';
    const char WhitespaceChar = ' ';

    static Dictionary<char, CharData> fontData;
    static int fontSize;
    static float bitmapWidth;
    static float uvPerPixel;
    static float lineHeight;
    static float baseHeight;
    static string bitmapName;
    public static int FontSize { get => fontSize; }
    public static string Name { get; private set; }

    /// <summary> Initialize bitmap font, can reinitialize on runtime. </summary>
    public static void Reinitialize(Device dx12Device, string resFolder)
    {
      // Dispose old data.
      fontData = new Dictionary<char, CharData>();
      if (bitmapName != null)
        Texture.DeleteTexture(bitmapName);

      // Get path.
      string fntPath = null;
      string pngPath = null;
      DirectoryInfo dirInfo = new DirectoryInfo(resFolder);
      List<FileInfo> fileInfo = dirInfo.GetFiles().ToList();
      foreach (FileInfo file in fileInfo)
      {
        if (file.Name.Contains(".png"))
          pngPath = file.FullName;
        else if (file.Name.Contains(".fnt"))
          fntPath = file.FullName;
      }
      if (fileInfo.Count != 2 || fntPath is null || pngPath is null)
      {
        throw new ArgumentException($"The bit-font folder should contain only two files: binary and  *.png file.");
      }

      // Deserialize binary data.
      using (var reader = new FileStream(fntPath, FileMode.Open, FileAccess.Read))
      {
        byte[] buffer = new byte[6];
        int posJump = 0;

        // header
        posJump = 3;
        reader.Position += posJump;
        reader.Read(buffer, 0, 1);
        if (buffer[0] != 3)
        {
          throw new ArgumentException($"Binary file must be version 3.");
        }

        // Please see: bmfont1.14a/doc/file_format.html
        // block: info
        posJump = 1;
        reader.Position += posJump;
        reader.Read(buffer, 0, 6);
        fontSize = ToInt16(buffer, 4);
        int fontNameSize = ToInt32(buffer, 0) - 14;
        buffer = new byte[fontNameSize];
        posJump = 14 - 2; // "- 2" for fontSize field.
        reader.Position += posJump;
        reader.Read(buffer, 0, fontNameSize);
        Name = Encoding.UTF8.GetString(buffer, 0, fontNameSize - 1); // "- 1" for "\0"

        // block: common
        posJump = 1;
        reader.Position += posJump;
        reader.Read(buffer, 0, 10);
        posJump = ToInt32(buffer, 0) - 6;
        reader.Position += posJump;
        lineHeight = ToInt16(buffer, 4);
        baseHeight = ToInt16(buffer, 6);
        bitmapWidth = ToInt16(buffer, 8);
        uvPerPixel = 1f / bitmapWidth;

        // block: pages
        posJump = 1;
        reader.Position += posJump;
        reader.Read(buffer, 0, 4);
        posJump = ToInt32(buffer, 0);
        reader.Position += posJump;

        // block: chars
        posJump = 1;
        reader.Position += posJump;
        reader.Read(buffer, 0, 4);
        int size_chars = ToInt32(buffer, 0);
        int maxIndex = size_chars - 1;
        buffer = new byte[size_chars];
        reader.Read(buffer, 0, size_chars);
        int charCount = size_chars / 20;
        for (int offset = 0; offset < maxIndex; offset += 20)
        {
          char cha = Encoding.UTF32.GetString(buffer, offset, 4)[0];
          Vector2 uvPos = new Vector2(ToInt16(buffer, offset + 4), ToInt16(buffer, offset + 6)) * uvPerPixel;
          Vector2 size = new Vector2(ToInt16(buffer, offset + 8), ToInt16(buffer, offset + 10));
          Vector2 uvSize = size * uvPerPixel;
          Vector2 pixelOffset = new Vector2(ToInt16(buffer, offset + 12), ToInt16(buffer, offset + 14));
          float advance = ToInt16(buffer, offset + 16);
          CharData data = new CharData(uvPos, uvSize, size, pixelOffset, advance);
          fontData.Add(cha, data);
        }

        // end of file
        if (reader.Position != reader.Length)
        {
          throw new ArgumentException($"Kerning pairs are not supported.");
        }
        if (!fontData.ContainsKey(FallbackChar))
        {
          throw new ArgumentException($"Font must contains '{FallbackChar}' as fallback char.");
        }
        if (!fontData.ContainsKey(WhitespaceChar))
        {
          throw new ArgumentException($"Font must contains '{WhitespaceChar}' as whitespace char.");
        }
      }

      // Load bitmap.
      Texture.Load_PNG_R8_NoMip(dx12Device, pngPath, Name);
      bitmapName = Name;
    }

    /// <summary> Create triangle strip vertex buffer for text.<br/><b>Only '\n' can be used as line break char!</b></summary>
    /// <param name="size">Set size = 0 to use original font size.</param>
    public static UIVertex[] Text2Mesh(string text, int size = 0, bool wordWrap = false, int textPixelWidth = 0)
    {
      if (fontData == null)
        throw new InvalidOperationException("BitFont class should be initialized before using it");
      // Get scale factor.
      float scale = 1;
      if(size > 8)
        scale = (float)size / fontSize;
      else if(size > 0)
        throw new ArgumentException("Font size must >= 9.");
      // Prepare data.
      int count = text.Length;
      Vector2 currPos = Vector2.Zero;
      float newLineHeight = lineHeight * scale;
      UIVertex[] vertices = new UIVertex[count * 4];
      // Generate vertex buffer.
      for (int charIndex = 0; charIndex < count; charIndex++)
      {
        int offset = charIndex * 4;
        char cha = text[charIndex];
        if (cha == LineBreakChar)
        {
          vertices[offset].pos = currPos;
          vertices[offset + 1].pos = currPos;
          vertices[offset + 2].pos = currPos;
          vertices[offset + 3].pos = currPos;
          currPos.X = 0;
          currPos.Y += newLineHeight;
          continue;
        }
        CharData charData;
        if (fontData.TryGetValue(cha, out charData) == false)
          charData = fontData[FallbackChar];
        if (wordWrap && currPos.X + charData.offset.X + charData.size.X > textPixelWidth)
        {
          currPos.X = 0;
          currPos.Y += newLineHeight;
        }
        vertices[offset].pos = currPos + charData.offset * scale;
        vertices[offset].uv = charData.uvPos;
        vertices[offset + 1].pos = vertices[offset].pos + new Vector2(charData.size.X, 0) * scale;
        vertices[offset + 1].uv = vertices[offset].uv + new Vector2(charData.uvSize.X, 0);
        vertices[offset + 2].pos = vertices[offset].pos + new Vector2(0, charData.size.Y) * scale;
        vertices[offset + 2].uv = vertices[offset].uv + new Vector2(0, charData.uvSize.Y);
        vertices[offset + 3].pos = vertices[offset].pos + charData.size * scale;
        vertices[offset + 3].uv = vertices[offset].uv + charData.uvSize;
        currPos.X += charData.advance * scale;
      }
      return vertices;
    }
  }
}