using System;
using System.Drawing;
using System.Windows.Forms;
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
      var form = new RenderForm();
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