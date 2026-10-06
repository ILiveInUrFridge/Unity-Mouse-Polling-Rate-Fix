// Fixes stutter caused by high polling rate mice (2kHz+) in Unity.
// Installs a thread-level WH_MOUSE hook that throttles WM_MOUSEMOVE messages
// to a configurable rate. All other mouse messages (clicks, scroll, hover)
// pass through untouched, so Unity's EventSystem and UI remain fully functional.

using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AOT;
#endif

public class HighPollingRateFix : MonoBehaviour
{
    [Tooltip("Max WM_MOUSEMOVE messages per second forwarded to Unity. " +
             "1000 is a safe default that eliminates stutter from 2kHz+ mice.")]
    [SerializeField, Range(125, 2000)]
    private int targetPollingRate = 1000;

#if UNITY_EDITOR_WIN
    [Tooltip("Also apply the fix inside the Unity Editor (useful for testing).")]
    [SerializeField]
    private bool enableInEditor = true;
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    const int WH_MOUSE = 7;
    const int WM_MOUSEMOVE  = 0x0200;
    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_LBUTTONUP   = 0x0202;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP   = 0x0205;
    const int WM_MBUTTONDOWN = 0x0207;
    const int WM_MBUTTONUP   = 0x0208;

    static IntPtr _hookHandle;
    static HookProc _hookDelegate; // prevent GC collection of the delegate
    static long _lastMoveTick;
    static long _minTickInterval;
    static int _buttonsDown;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
#if UNITY_EDITOR_WIN
        if (!enableInEditor) return;
#endif
        InstallHook();
    }

    void OnDisable()
    {
        RemoveHook();
    }

    void OnDestroy()
    {
        RemoveHook();
    }

    void InstallHook()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _minTickInterval = Stopwatch.Frequency / targetPollingRate;
        _lastMoveTick = 0;
        _hookDelegate = ThrottleMouseMove;
        _hookHandle = SetWindowsHookExW(WH_MOUSE, _hookDelegate, IntPtr.Zero, GetCurrentThreadId());
    }

    static void RemoveHook()
    {
        if (_hookHandle == IntPtr.Zero) return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookDelegate = null;
    }

    [MonoPInvokeCallback(typeof(HookProc))]
    static IntPtr ThrottleMouseMove(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;

            switch (msg)
            {
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    _buttonsDown++;
                    break;

                case WM_LBUTTONUP:
                case WM_RBUTTONUP:
                case WM_MBUTTONUP:
                    if (_buttonsDown > 0) _buttonsDown--;
                    break;

                case WM_MOUSEMOVE when _buttonsDown == 0:
                    long now = Stopwatch.GetTimestamp();
                    if (now - _lastMoveTick < _minTickInterval)
                        return (IntPtr)1;
                    _lastMoveTick = now;
                    break;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

#endif
}
