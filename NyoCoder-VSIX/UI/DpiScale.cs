using System;
using System.Drawing;

namespace NyoCoder
{
    /// <summary>
    /// Scales 96-DPI pixel sizes for hand-built WinForms dialogs. Their font follows
    /// display scaling, and VS2017+ is DPI-aware (Windows does not stretch its dialogs),
    /// so fixed pixel sizes would clip the text at 150% and up.
    /// </summary>
    internal static class DpiScale
    {
        private static readonly float Factor = ReadFactor();

        private static float ReadFactor()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>A 96-DPI pixel length at the current display scaling.</summary>
        public static int Px(int pixels)
        {
            return (int)Math.Round(pixels * Factor);
        }
    }
}
