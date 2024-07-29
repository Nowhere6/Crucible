using System;
using System.Windows.Forms;
using SharpDX.Windows;
using Linearstar.Windows.RawInput;

namespace Crucible;
using System.Diagnostics;
using System.Drawing;

static class Program
{
  /// <summary>
  /// Entry point of application.
  /// </summary>
  [STAThread]
  static void Main()
  {
    try
    {
      var engine = new SD12Engine();
      engine.Run();
    }
    catch (Exception ex)
    {
      MessageBox.Show(ex.ToString(), "Fatal Error", MessageBoxButtons.OK);
    }
  }
}

public sealed class SD12Form : RenderForm
{
  // Hide useless properties.
  new bool IsFullscreen { get; }
  new bool AllowUserResizing { get; }

  Timer timer;
  Rectangle prevWinRect;
  Action LoopBody;
  Action<RawInputData> inputEvent;
  static Size minSize = new Size(400, 400);

  public SD12Form() : base()
  {
    Size = minSize;
    Text = "DefaultTitle";
    MinimumSize = minSize;
    SetWindowMode(false);
  }

  public void SetLoopBody(Action loopBody) => LoopBody = loopBody;

  /// <summary>
  ///  ClientSize excludes border size. 
  /// </summary>
  public void SetClientSize(int width, int height) => ClientSize = new Size(width, height);

  public void SetWindowMode(bool isFullScreenForm, bool allowResizing = true)
  {
    var targetRect = new Rectangle();
    if (isFullScreenForm)
    {
      FormBorderStyle = FormBorderStyle.None;
      prevWinRect = new Rectangle(Location, ClientSize);
      targetRect = Screen.FromControl(this).Bounds;
    }
    else
    {
      base.MaximizeBox = allowResizing;
      FormBorderStyle = allowResizing ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
      targetRect = prevWinRect;
    }
    ClientSize = targetRect.Size;
    Location = targetRect.Location;
  }

  public void SetInputEvent(Action<RawInputData> act) => inputEvent = act;

  protected override void WndProc(ref Message m)
  {
    const int WM_INPUT = 0x00FF;
    const int WM_ENTERSIZEMOVE = 0x0231;
    const int WM_EXITSIZEMOVE = 0x0232;
    switch (m.Msg)
    {
      case WM_INPUT:
        var data = RawInputData.FromHandle(m.LParam);
        inputEvent?.Invoke(data);
        break;
      /* 
       * When users resize or move the form, the program will be trapped in a modal loop,
       * which will stop our message loop, causing the engine not to run.
       * Therefore, create a timer to run the engine.
      */
      case WM_ENTERSIZEMOVE:
        timer = new Timer();
        timer.Interval = 1000/50; //20ms
        timer.Tick += (x, y) => LoopBody();
        timer.Enabled = true;
        break;
      case WM_EXITSIZEMOVE:
        timer.Enabled = false;
        timer.Dispose();
        timer = null;
        break;
    }
    base.WndProc(ref m);
  }
}
