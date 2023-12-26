using Linearstar.Windows.RawInput.Native;
using Linearstar.Windows.RawInput;
using System.Collections.Generic;
using System.Windows.Forms;
using SharpDX;
using System;

namespace SharpD12
{
  public enum MiceButton
  {
    MIDDLE,
    RIGHT,
    LEFT,
    X1,
    X2,
  }

  public static class Input
  {
    private enum State
    {
      NOT_PRESSED,
      PRESSED,
      DOWN,
      UP
    }

    public static Vector2 MouseOffset { get => lastMouseOffset; }
    public static float WheelOffset { get => lastWheelOffset; }

    private static Vector2 lastMouseOffset = Vector2.Zero;
    private static Vector2 mouseOffset = Vector2.Zero;
    private static float lastWheelOffset = 0;
    private static float wheelOffset = 0;
    private static Dictionary<MiceButton, State> prevMouseStates = new Dictionary<MiceButton, State>();
    private static Dictionary<MiceButton, State> mouseStates = new Dictionary<MiceButton, State>();
    private static Dictionary<Keys, State> prevKeyStates = new Dictionary<Keys, State>();
    private static Dictionary<Keys, State> keyStates = new Dictionary<Keys, State>();

    static Input()
    {
      var miceButtons = Enum.GetValues<MiceButton>();
      foreach (var button in miceButtons)
      {
        prevMouseStates.Add(button, State.NOT_PRESSED);
        mouseStates.Add(button, State.NOT_PRESSED);
      }
    }

    public static void Register(nint formHandle)
    {
      RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.ExInputSink | RawInputDeviceFlags.NoLegacy, formHandle);
      // If we added "NoLegacy" flag to mouse, the window would be unresponsive.
      RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.ExInputSink, formHandle);
    }

    public static void UnRegister()
    {
      RawInputDevice.UnregisterDevice(HidUsageAndPage.Keyboard);
      RawInputDevice.UnregisterDevice(HidUsageAndPage.Mouse);
    }

    public static bool GetKeyNotPressed(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.NOT_PRESSED : true;

    public static bool GetKeyPressed(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.PRESSED : false;

    public static bool GetKeyDown(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.DOWN : false;

    public static bool GetKeyUp(Keys key) => keyStates.ContainsKey(key) ? keyStates[key] == State.UP : false;

    /// <summary> <b>бя Recommanded</b><br/>Is that key preseed down or pressed? </summary>
    public static bool GetKey(Keys key) => GetKeyPressed(key) || GetKeyDown(key);

    public static bool GetButtonNotPressed(MiceButton button) => mouseStates[button] == State.NOT_PRESSED;

    public static bool GetButtonPressed(MiceButton button) => mouseStates[button] == State.PRESSED;

    public static bool GetButtonDown(MiceButton button) => mouseStates[button] == State.DOWN;

    public static bool GetButtonUp(MiceButton button) => mouseStates[button] == State.UP;

    /// <summary> <b>бя Recommanded</b><br/>Is that mouse button preseed down or pressed? </summary>
    public static bool GetButton(MiceButton button) => GetButtonPressed(button) || GetButtonDown(button);

    /// <summary>
    /// Called by system after RenderLoop.NextFrame().
    /// </summary>
    public static void PerMessageProcess(RawInputData msg)
    {
      if (msg.Header.Type == RawInputDeviceType.Mouse)
      {
        RawInputMouseData message = (RawInputMouseData)msg;
        switch (message.Mouse.Buttons)
        {
          case RawMouseButtonFlags.None:
            // 0x00 MOUSE_MOVE_RELATIVE
            bool isRelative = message.Mouse.Flags == RawMouseFlags.None;
            // Accumulate mouse offset per frame.
            mouseOffset += isRelative ? new Vector2(message.Mouse.LastX, message.Mouse.LastY) : Vector2.Zero;
            break;
          case RawMouseButtonFlags.LeftButtonDown:
            mouseStates[MiceButton.LEFT] = State.DOWN;
            break;
          case RawMouseButtonFlags.LeftButtonUp:
            mouseStates[MiceButton.LEFT] = State.UP;
            break;
          case RawMouseButtonFlags.RightButtonDown:
            mouseStates[MiceButton.RIGHT] = State.DOWN;
            break;
          case RawMouseButtonFlags.RightButtonUp:
            mouseStates[MiceButton.RIGHT] = State.UP;
            break;
          case RawMouseButtonFlags.MiddleButtonDown:
            mouseStates[MiceButton.MIDDLE] = State.DOWN;
            break;
          case RawMouseButtonFlags.MiddleButtonUp:
            mouseStates[MiceButton.MIDDLE] = State.UP;
            break;
          case RawMouseButtonFlags.Button4Down:
            mouseStates[MiceButton.X1] = State.DOWN;
            break;
          case RawMouseButtonFlags.Button4Up:
            mouseStates[MiceButton.X1] = State.UP;
            break;
          case RawMouseButtonFlags.Button5Down:
            mouseStates[MiceButton.X2] = State.DOWN;
            break;
          case RawMouseButtonFlags.Button5Up:
            mouseStates[MiceButton.X2] = State.UP;
            break;
          case RawMouseButtonFlags.MouseWheel:
            const int WHEEL_DELTA = 120;
            wheelOffset += message.Mouse.ButtonData / WHEEL_DELTA;
            break;
          default:
            //throw new InvalidOperationException("Invalid mouse button flag.");
            break;
        }
      }
      else if (msg.Header.Type == RawInputDeviceType.Keyboard)
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
          case RawKeyboardFlags.None:
            // Remove continuous key down from system.
            if (prevKeyStates[key] == State.NOT_PRESSED || prevKeyStates[key] == State.UP)
            {
              keyStates[key] = State.DOWN;
            }
            break;
          case RawKeyboardFlags.Up:
            keyStates[key] = State.UP;
            break;
          default:
            //throw new InvalidOperationException("Invalid keyboard flag.");
            break;
        }
      }
    }

    /// <summary>
    /// Invoke after all input events of current frame are handled.
    /// </summary>
    public static void Update()
    {
      lastMouseOffset = mouseOffset;
      mouseOffset = Vector2.Zero;
      lastWheelOffset = wheelOffset;
      wheelOffset = 0;

      foreach (var button in mouseStates.Keys)
      {
        // Acquire states.
        var prevState = prevMouseStates[button];
        var state = mouseStates[button];
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
        prevMouseStates[button] = state;
        mouseStates[button] = state;
      }

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