using Linearstar.Windows.RawInput;
using System.Collections.Generic;
namespace SharpD12
{
  public static class GameInput
  {
    private static List<byte> Keys;

    public static void InputProcess(RawInputData msg)
    {
      if (msg.Header.Type == RawInputDeviceType.Keyboard)
      {
        RawInputKeyboardData m = (RawInputKeyboardData)msg;
        //string key = System.Text.Encoding.ASCII.GetString((byte)m.Keyboard.VirutalKey, 1);
      }
    }

    public static void Empty()
    {
      Keys.Clear();
    }
  }
}