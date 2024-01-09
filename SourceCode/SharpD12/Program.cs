using System;
using System.Drawing;
using System.Windows.Forms;
using Linearstar.Windows.RawInput;
using SharpDX.Windows;
namespace SharpD12
{
  static class Program
  {
    /// <summary>
    /// Entry point of application.
    /// </summary>
    [STAThread]
    static void Main()
    {
      int width = 1920;
      int height = 1080;
      var form = new CustomedForm(width, height);

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
    public delegate void InputAction(RawInputData msg);

    public event InputAction InputEvent;
    /// <summary>Because size of form does not equal to size ofrender target, we add this panel for drawing.</summary>
    public Panel DrawingPanel { get; private set; }
    
    public CustomedForm(int formWidth, int formHeight) : base()
    {
      Text = "SharpD12";
      Width = formWidth;
      Height = formHeight;
      AllowUserResizing = true;
      DrawingPanel = new Panel();
      DrawingPanel.Margin = new Padding(0);
      DrawingPanel.Dock = DockStyle.Fill;
      Controls.Add(DrawingPanel);
    }

    protected override void WndProc(ref Message m)
    {
      base.WndProc(ref m);
      const int WM_INPUT = 0x00FF;
      if (m.Msg == WM_INPUT)
      {
        var data = RawInputData.FromHandle(m.LParam);
        InputEvent?.Invoke(data);
      }
    }
  }
}