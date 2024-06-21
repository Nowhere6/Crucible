using System.Collections.Generic;
using System.Numerics;

namespace Crucible.UI;

static class UIManager
{
  public static void SortUI(List<UIRenderItem> items) => items.Sort((a, b) => a.zOrder - b.zOrder);
}

/// <summary>Coordinate: <b>left-top = (0,0), right-bottom = (1,1)</b></summary>
public abstract class UIElement
{
  public bool UsePixelSize = true;
  /// <summary>Range: 0~1</summary>
  public Vector2 Archor;
  /// <summary>Range: 0~1 or 0~w(h)</summary>
  public Vector4 Size;
}

public class Text : UIElement
{
}

public class Panel : UIElement
{
}

public class Button : UIElement
{
}

public class CheckBox : UIElement
{
}

public class ComboBox : UIElement
{
}

public class InputBox : UIElement
{
}