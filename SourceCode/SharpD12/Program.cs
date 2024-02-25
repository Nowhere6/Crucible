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
      form.SetContentSize(1920, 1080);
      form.Show();
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
    Rectangle preWinRect;
    Action<RawInputData> inputEvent;
    static string defaultName = "SharpD12";
    static Size minSize = new Size(400, 400);
    
    public CustomedForm() : base()
    {
      Text = defaultName;
      Size = minSize;
      MinimumSize = minSize;
      AllowUserResizing = true;
    }

    public void SetContentSize(int width, int height) => ClientSize = new Size(width, height); // ClientSize excludes border size. 

    public void SetWindowMode(bool fullScreen, bool allowResizing = true)
    {
      AllowUserResizing = allowResizing;
      var targetRect = new Rectangle();
      if (fullScreen)
      {
        FormBorderStyle = FormBorderStyle.None;
        preWinRect = new Rectangle(Location, ClientSize);
        targetRect = Screen.FromControl(this).Bounds;
      }
      else
      {
        FormBorderStyle = allowResizing ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
        targetRect = preWinRect;
      }
      ClientSize = targetRect.Size;
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