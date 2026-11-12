// .NET 4 APIs the shared sources use that .NET 3.5 lacks and that can be supplied from
// outside (new types and instance methods; missing static members cannot be).
using System;

namespace System.Net
{
    internal static class WebUtility
    {
        public static string HtmlDecode(string value)
        {
            return System.Web.HttpUtility.HtmlDecode(value);
        }
    }
}

namespace System.Windows.Media
{
    internal enum TextFormattingMode { Ideal, Display }

    /// <summary>WPF 3.5 has no display-mode text formatting; the request is ignored.</summary>
    internal static class TextOptions
    {
        public static void SetTextFormattingMode(DependencyObject element, TextFormattingMode value) { }
    }
}

namespace NyoCoder
{
    internal static class Net35Extensions
    {
        public static bool HasFlag(this Enum value, Enum flag)
        {
            ulong bits = Convert.ToUInt64(flag);
            return (Convert.ToUInt64(value) & bits) == bits;
        }
    }
}
