using System;
using System.Windows.Forms;
using System.Collections.Generic;
using SharpDX;
using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;

namespace SharpD12;

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
  private static Dictionary<MiceButton, State> prevMice = new Dictionary<MiceButton, State>();
  private static Dictionary<MiceButton, State> currMice = new Dictionary<MiceButton, State>();
  private static Dictionary<Keys, State> prevKeys = new Dictionary<Keys, State>();
  private static Dictionary<Keys, State> currKeys = new Dictionary<Keys, State>();

  static Input()
  {
    var miceButtons = Enum.GetValues<MiceButton>();
    foreach (var button in miceButtons)
    {
      prevMice.Add(button, State.NOT_PRESSED);
      currMice.Add(button, State.NOT_PRESSED);
    }
  }

  public static void Register(nint formHandle)
  {
    // Remove RIDEV_EXINPUTSINK so we don't receive raw-input when app is in background.
    RawInputDevice.RegisterDevice(HidUsageAndPage.Keyboard, RawInputDeviceFlags.NoLegacy, formHandle);
    // If we added "NoLegacy" flag to mouse, the window would be unresponsive.
    RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.None, formHandle);
  }

  public static void UnRegister()
  {
    RawInputDevice.UnregisterDevice(HidUsageAndPage.Keyboard);
    RawInputDevice.UnregisterDevice(HidUsageAndPage.Mouse);
  }

  public static bool GetKeyNotPressed(Keys key) => currKeys.ContainsKey(key) ? currKeys[key] == State.NOT_PRESSED : true;

  public static bool GetKeyPressed(Keys key) => currKeys.ContainsKey(key) ? currKeys[key] == State.PRESSED : false;

  public static bool GetKeyDown(Keys key) => currKeys.ContainsKey(key) ? currKeys[key] == State.DOWN : false;

  public static bool GetKeyUp(Keys key) => currKeys.ContainsKey(key) ? currKeys[key] == State.UP : false;

  /// <summary> <b>бя Recommanded</b><br/>Is that key preseed down or pressed? </summary>
  public static bool GetKey(Keys key) => GetKeyPressed(key) || GetKeyDown(key);

  public static bool GetButtonNotPressed(MiceButton button) => currMice[button] == State.NOT_PRESSED;

  public static bool GetButtonPressed(MiceButton button) => currMice[button] == State.PRESSED;

  public static bool GetButtonDown(MiceButton button) => currMice[button] == State.DOWN;

  public static bool GetButtonUp(MiceButton button) => currMice[button] == State.UP;

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
          currMice[MiceButton.LEFT] = State.DOWN;
          break;
        case RawMouseButtonFlags.LeftButtonUp:
          currMice[MiceButton.LEFT] = State.UP;
          break;
        case RawMouseButtonFlags.RightButtonDown:
          currMice[MiceButton.RIGHT] = State.DOWN;
          break;
        case RawMouseButtonFlags.RightButtonUp:
          currMice[MiceButton.RIGHT] = State.UP;
          break;
        case RawMouseButtonFlags.MiddleButtonDown:
          currMice[MiceButton.MIDDLE] = State.DOWN;
          break;
        case RawMouseButtonFlags.MiddleButtonUp:
          currMice[MiceButton.MIDDLE] = State.UP;
          break;
        case RawMouseButtonFlags.Button4Down:
          currMice[MiceButton.X1] = State.DOWN;
          break;
        case RawMouseButtonFlags.Button4Up:
          currMice[MiceButton.X1] = State.UP;
          break;
        case RawMouseButtonFlags.Button5Down:
          currMice[MiceButton.X2] = State.DOWN;
          break;
        case RawMouseButtonFlags.Button5Up:
          currMice[MiceButton.X2] = State.UP;
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
      if (currKeys.ContainsKey(key) == false)
      {
        currKeys.Add(key, State.NOT_PRESSED);
        prevKeys.Add(key, State.NOT_PRESSED);
      }
      switch (message.Keyboard.Flags)
      {
        case RawKeyboardFlags.None:
          // Remove continuous key down from system.
          if (prevKeys[key] != State.PRESSED)
          {
            currKeys[key] = State.DOWN;
          }
          break;
        case RawKeyboardFlags.Up:
          currKeys[key] = State.UP;
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

    foreach (var button in currMice.Keys)
    {
      CheckPressed(out State state, prevMice[button], currMice[button]);
      // Update key states.
      prevMice[button] = state;
      currMice[button] = state;
    }

    foreach (var key in currKeys.Keys)
    {
      CheckPressed(out State state, prevKeys[key], currKeys[key]);
      // Update key states.
      prevKeys[key] = state;
      currKeys[key] = state;
    }
  }

  private static void CheckPressed(out State state, State prevState, State currentState)
  {
    state = currentState;
    if (prevState == State.DOWN && currentState == State.DOWN)
    {
      state = State.PRESSED;
    }
    else if (prevState == State.UP && currentState == State.UP)
    {
      state = State.NOT_PRESSED;
    }
  }
}