using Linearstar.Windows.RawInput;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SharpD12
{
  public static class Input
  {
    private enum State
    {
      NOT_PRESSED,
      PRESSED,
      DOWN,
      UP
    }

    private static Dictionary<Keys, State> prevKeyStates = new Dictionary<Keys, State>();
    private static Dictionary<Keys, State> keyStates = new Dictionary<Keys, State>();

    public static void Register(nint formHandle)
    {
      RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink | RawInputDeviceFlags.NoLegacy, formHandle);
      //RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink | RawInputDeviceFlags.NoLegacy, formHandle);
    }

    public static void UnRegister()
    {
      RawInputDevice.UnregisterDevice(HidUsageAndPage.Keyboard);
      //RawInputDevice.UnregisterDevice(HidUsageAndPage.Mouse);
    }

    public static bool GetKeyNotPressed(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.NOT_PRESSED : true;

    public static bool GetKeyPressed(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.PRESSED : false;

    public static bool GetKeyDown(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.DOWN : false;

    public static bool GetKeyUp(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.UP : false;

    /// <summary>
    /// <b>бя Recommanded</b><br/>Check if that key is preseed down or pressed.
    /// </summary>
    public static bool GetKey(Keys key) => GetKeyPressed(key) || GetKeyDown(key);

    /// <summary>
    /// Called by system after RenderLoop.NextFrame().
    /// </summary>
    public static void PerMessageProcess(RawInputData msg)
    {
      if (msg.Header.Type == RawInputDeviceType.Keyboard)
      {
        RawInputKeyboardData message = (RawInputKeyboardData)msg;
        var key = (Keys)message.Keyboard.VirutalKey;

        // Add new key into dictionary.
        if (keyStates.ContainsKey(key) == false)
        {
          keyStates.Add(key, State.NOT_PRESSED);
          prevKeyStates.Add(key, State.NOT_PRESSED);
        }

        switch (message.Keyboard.Flags)
        {
          case Linearstar.Windows.RawInput.Native.RawKeyboardFlags.None:
            // Remove continuous key down from system.
            if (prevKeyStates[key] == State.NOT_PRESSED || prevKeyStates[key] == State.UP)
            {
              keyStates[key] = State.DOWN;
            }
            break;
          case Linearstar.Windows.RawInput.Native.RawKeyboardFlags.Up:
            keyStates[key] = State.UP;
            break;
          default:
            throw new InvalidOperationException("Invalid keyboard flag.");
        }
      }
      else if (msg.Header.Type == RawInputDeviceType.Mouse)
      {
        RawInputMouseData message = (RawInputMouseData)msg;
        // TODO: message.Mouse.ButtonData
      }
    }

    /// <summary>
    /// Invoke once Manually in every tick, after that all input events are handled.
    /// </summary>
    public static void PostProcess()
    {
      foreach (var key in keyStates.Keys)
      {
        // Acquire states.
        var prevState = prevKeyStates[key];
        var state = keyStates[key];

        // Adjust key states.
        if (prevState == State.DOWN && state == State.DOWN)
        {
          state = State.PRESSED;
        }
        else if (prevState == State.UP && state == State.UP)
        {
          state = State.NOT_PRESSED;
        }

        // Update key states.
        prevKeyStates[key] = state;
        keyStates[key] = state;
      }
    }
  }
}