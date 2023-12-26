using System;
using System.Drawing;
using System.Windows.Forms;
using Linearstar.Windows.RawInput;
using SharpDX.Windows;
namespace SharpD12
{
  public class CustomedForm : RenderForm
  {
    public CustomedForm() : base() { }

    public delegate void InputAction(RawInputData msg);

    public event InputAction InputEvent;

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
      var form = new CustomedForm();
      form.Width = width;
      form.Height = height;
      form.AllowUserResizing = false;
      form.Text = "SharpD12";
      
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
}