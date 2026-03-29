using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace SystemeCaisse.UI.Services
{
    public class ScreenHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        public class ScreenInfo
        {
            public string DeviceName { get; set; } = string.Empty;
            public Rect Bounds { get; set; }
            public Rect WorkingArea { get; set; }
            public Rect LogicalBounds { get; set; }
            public Rect LogicalWorkingArea { get; set; }
            public bool IsPrimary { get; set; }
            public double ScaleX { get; set; }
            public double ScaleY { get; set; }
        }

        public static List<ScreenInfo> GetScreens()
        {
            var screens = new List<ScreenInfo>();
            var seenDevices = new HashSet<string>();
            string logPath = "startup_log_v2.txt";

            try
            {
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] ScreenHelper.GetScreens() start\n");
                
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
                {
                    var mi = new MONITORINFOEX();
                    mi.Size = Marshal.SizeOf(mi);
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        string deviceName = string.IsNullOrEmpty(mi.DeviceName) ? $"Unknown_{hMonitor}" : mi.DeviceName;
                        bool isPrimary = (mi.Flags & 1) != 0;
                        
                        System.IO.File.AppendAllText(logPath, $"  - Raw Monitor: {deviceName}, Primary={isPrimary}, Bounds={mi.Monitor.Left},{mi.Monitor.Top} {mi.Monitor.Right - mi.Monitor.Left}x{mi.Monitor.Bottom - mi.Monitor.Top}\n");

                        if (seenDevices.Contains(deviceName))
                        {
                            System.IO.File.AppendAllText(logPath, $"    -> Skip duplicate {deviceName}\n");
                            return true;
                        }
                        seenDevices.Add(deviceName);

                        // Get DPI Scaling
                        uint dpiX = 96, dpiY = 96;
                        try
                        {
                            GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                        }
                        catch { }

                        double scaleX = dpiX / 96.0;
                        double scaleY = dpiY / 96.0;

                        screens.Add(new ScreenInfo
                        {
                            DeviceName = deviceName,
                            Bounds = new Rect(mi.Monitor.Left, mi.Monitor.Top, mi.Monitor.Right - mi.Monitor.Left, mi.Monitor.Bottom - mi.Monitor.Top),
                            WorkingArea = new Rect(mi.WorkArea.Left, mi.WorkArea.Top, mi.WorkArea.Right - mi.WorkArea.Left, mi.WorkArea.Bottom - mi.WorkArea.Top),
                            LogicalBounds = new Rect(mi.Monitor.Left / scaleX, mi.Monitor.Top / scaleY, (mi.Monitor.Right - mi.Monitor.Left) / scaleX, (mi.Monitor.Bottom - mi.Monitor.Top) / scaleY),
                            LogicalWorkingArea = new Rect(mi.WorkArea.Left / scaleX, mi.WorkArea.Top / scaleY, (mi.WorkArea.Right - mi.WorkArea.Left) / scaleX, (mi.WorkArea.Bottom - mi.WorkArea.Top) / scaleY),
                            IsPrimary = isPrimary,
                            ScaleX = scaleX,
                            ScaleY = scaleY
                        });
                        System.IO.File.AppendAllText(logPath, $"    -> Added unique screen {deviceName}. Count now={screens.Count}\n");
                    }
                    return true;
                }, IntPtr.Zero);
                
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] ScreenHelper.GetScreens() end. Total unique={screens.Count}\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] ScreenHelper ERROR: {ex.Message}\n");
            }

            return screens;
        }
    }
}
