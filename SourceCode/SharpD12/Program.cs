using System;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Windows;
using Linearstar.Windows.RawInput;

namespace SharpD12
{
  using System.Drawing;
  static class Program
  {
    /// <summary>
    /// Entry point of application.
    /// </summary>
    [STAThread]
    static void Main()
    {
      var form = new CustomedForm();
      form.Show();
      form.SetWindowSize(1920, 1080);
      try
      {
        var engine = new SD12Engine(form);
        engine.Run();
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.ToString(), "Fatal Error", MessageBoxButtons.OK);
      }
    }
  }

  public class CustomedForm : RenderForm
  {
    Rectangle preWinRectangle;
    Action<RawInputData> inputEvent;
    /// <summary>Border size of windows is known, wen acquire it at the beginning.</summary>
    Size winBorderSize;
    /// <summary>Form size does NOT equal to content size, add this panel for actual drawing.</summary>
    public Panel DrawingPanel { get; private set; }

    public CustomedForm() : base()
    {
      Text = "SharpD12";
      Width = 400;
      Height = 400;
      MinimumSize = new Size(Width, Height);
      AllowUserResizing = true;
      DrawingPanel = new Panel();
      DrawingPanel.Dock = DockStyle.Fill;
      DrawingPanel.Margin = Padding.Empty;
      DrawingPanel.BackColor = Color.Black;
      DrawingPanel.BorderStyle = BorderStyle.None;
      Controls.Add(DrawingPanel);
    }

    public void SetWindowSize(int width, int height)
    {
      Size newSize = new Size(width, height);
      if (!Visible)
        throw new Exception("Show window firstly before setting it.");
      winBorderSize = Size - DrawingPanel.Size;
      Size = newSize + winBorderSize;
    }

    public void SetWindowMode(bool fullScreen, bool allowResizing = true)
    {
      AllowUserResizing = true;
      var targetRect = new Rectangle();
      if (fullScreen)
      {
        if (FormBorderStyle == FormBorderStyle.None) return;
        FormBorderStyle = FormBorderStyle.None;
        preWinRectangle = new Rectangle(Location, Size);
        targetRect = Screen.FromControl(this).Bounds;
      }
      else
      {
        if (FormBorderStyle != FormBorderStyle.None) return;
        FormBorderStyle = allowResizing ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
        targetRect = preWinRectangle;
      }
      Size = targetRect.Size;
      Location = targetRect.Location;
    }

    public void SetInputEvent(Action<RawInputData> act) => inputEvent = act;

    protected override void WndProc(ref Message m)
    {
      base.WndProc(ref m);
      const int WM_INPUT = 0x00FF;
      if (m.Msg == WM_INPUT)
      {
        var data = RawInputData.FromHandle(m.LParam);
        inputEvent?.Invoke(data);
      }
    }
  }
}