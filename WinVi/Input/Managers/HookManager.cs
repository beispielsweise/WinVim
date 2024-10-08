﻿using System;
using System.Runtime.InteropServices;
using System.Windows;
using WinVi.Input.Handlers.Commands;
using WinVi.Input.Handlers.Modes;
using WinVi.Input.Utilities;
using WinVi.UI.Tray;
using WinVi.UiAutomation.Taskbar;

namespace WinVi.Input
{
    /// <summary>
    /// A class responsible for getting system-wide keyboard presses. 
    /// Contains logic for Processing keypresses
    /// Uses singleton pattern
    /// </summary>
    internal class HookManager : IDisposable 
    {
        private static readonly Lazy<HookManager> _instance = new Lazy<HookManager>(() => new HookManager(), true);

        private IntPtr _hookID;
        private readonly KeyboardHookUtilities.LowLevelKeyboardProc _proc;

        // 
        private bool _isInsertModeEnabled= false;
        private bool _isOverlayWindowOpened = false;

        // Modifier buttons status fields
        private bool _ctrlPressed = false;
        private bool _altPressed = false;
        private bool _shiftPressed = false;
        private static string vkString = "";

        /// <summary>
        /// Initialize keyboard hook and handlers 
        /// </summary>
        /// <param name="window">Instance of a window, needed to work with the overlay window itself </param>
        private HookManager()
        {
            _proc = HookCallback;
            try
            {
                _hookID = SetHook();
            }
            catch
            {
                throw new ArgumentNullException();
            }
        }

        internal static HookManager Instance => _instance.Value;

        /// <summary>
        /// Sets a keyboard hook
        /// </summary>
        private IntPtr SetHook()
        {
            IntPtr hookID = KeyboardHookUtilities.SetWindowsHookEx((int)KeyboardHookUtilities.KeyboardHooks.WH_KEYBOARD_LL, _proc, KeyboardHookUtilities.GetModuleHandle(null), 0);
            if (hookID == IntPtr.Zero)
                throw new ArgumentNullException();

            return hookID;
        }

        /// <summary>
        /// Runs when a keyboard event occures
        /// </summary>
        /// <param name="nCode"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        /// <returns>IntPtr(1): if the key is handeled by the program
        /// IntPtr: KeyboardUtilities.CallNextHookEx(_hookID, nCode, wParam, lParam) - pass the handling to other programs
        /// </returns>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            vkString = KeyboardHookUtilities._vkKeyCodes.TryGetValue(Marshal.ReadInt32(lParam), out string keyString) ? keyString : "null";

            if (wParam == (IntPtr)KeyboardHookUtilities.KeyboardEventTypes.KeyDown || wParam == (IntPtr)KeyboardHookUtilities.KeyboardEventTypes.SyskeyDown)
            {
                CheckHotkeyButtonPressed(vkString, true);

                // If the hotkey combintation is not pressed AND insert mode is not active
                // Process single-press buttons
                if (!CheckHotkeyCombinationPressed() && !_isInsertModeEnabled)
                {
                    // If overlay window is opened, process only these keys
                    if (_isOverlayWindowOpened)
                    {
                        // shift modifier?
                        switch (vkString)
                        {
                            case "ESCAPE":
                                CloseOverlayWindow();
                                return (IntPtr)1;
                            default:
                                // !!!
                                switch (TaskbarMode.ProcessHintKey(vkString, CheckShiftModifierPressed()))
                                {
                                    case TaskbarMode.HintKeyStatus.Pressed:
                                        _isOverlayWindowOpened = false;
                                        TaskbarElements.Instance.Dispose();
                                        TrayManager.SetIconStatus(TrayIconStatus.Default);
                                        return (IntPtr)1;
                                    case TaskbarMode.HintKeyStatus.Error:
                                        break;
                                    case TaskbarMode.HintKeyStatus.Skip:
                                        break;
                                    default:
                                        return KeyboardHookUtilities.CallNextHookEx(_hookID, nCode, wParam, lParam);
                                }

                                return (IntPtr)1;
                        }
                    }
                }
                // If the hotkey combination is pressed
                else if (CheckHotkeyCombinationPressed() && !_isOverlayWindowOpened)
                {
                    // Logic for exiting the insert mode, ESC key
                    if (_isInsertModeEnabled)
                    {
                        if (vkString == "ESCAPE")
                        {
                            ExitInsertMode();
                            return (IntPtr)1;
                        }

                        return KeyboardHookUtilities.CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    // Process hotkeys
                    switch (vkString)
                    {
                        case "T":
                            HandleTaskbarMode();
                            return (IntPtr)1;
                        case "I":
                            EnterInsertMode();
                            return (IntPtr)1;
                        default:
                            return KeyboardHookUtilities.CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }
                }
            }
            else if (wParam == (IntPtr)KeyboardHookUtilities.KeyboardEventTypes.KeyUp)
                CheckHotkeyButtonPressed(vkString, false);

            return KeyboardHookUtilities.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void EnterInsertMode()
        {
            _isInsertModeEnabled = true;
            TrayManager.SetIconStatus(TrayIconStatus.InsertMode);
        }

        private void HandleTaskbarMode()
        {
            if (TaskbarMode.OpenOverlay() == true)
            {
                _isOverlayWindowOpened = true;
                TrayManager.SetIconStatus(TrayIconStatus.OverlayOn);
            }
        }
        private void ExitInsertMode()
        {
            _isInsertModeEnabled = false;
            TrayManager.SetIconStatus(TrayIconStatus.Default);
        }

        private void CloseOverlayWindow()
        {
            _isOverlayWindowOpened = false;
            ForceCloseWindow.Execute();
            TrayManager.SetIconStatus(TrayIconStatus.Default);
        }

        /// <summary>
        /// Checks if buttons CTRL SHIFT and ALT are pressed at the same time. 
        /// This is a standart shortcut modifyer which is NOT intended to be changed.
        /// </summary>
        /// <param name="vkString"> string: with a lowercase character</param>
        /// <param name="isKeyDown">bool: is key pressed</param>
        private void CheckHotkeyButtonPressed(string vkString, bool isKeyDown)
        {
            // Hotkey mechanism logic
            switch (vkString)
            {
                case "SHIFT":
                    _shiftPressed = isKeyDown;
                    break;
                case "CTRL":
                    _ctrlPressed = isKeyDown;
                    break;
                case "ALT":
                    _altPressed = isKeyDown;
                    break;
                default:
                    break;
            }
        }

        private bool CheckHotkeyCombinationPressed()
        {
            return _ctrlPressed && _altPressed && _shiftPressed;
        }

        private bool CheckShiftModifierPressed()
        {
            return _shiftPressed && !_altPressed && !_ctrlPressed;
        }

        /// <summary>
        /// Disposes of resources and nullifies the singleton instance
        /// </summary>
        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                KeyboardHookUtilities.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }
    }
}
