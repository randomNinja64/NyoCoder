using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace NyoCoder
{
    /// <summary>
    /// A custom text marker type (red background) for deleted text in diff previews. The
    /// editor finds it through the "Text Editor\External Markers" registry key written by the
    /// install scripts, then asks the service proffered here for the type's appearance.
    /// </summary>
    [ComVisible(true)]
    public sealed class DeletionMarkerType :
        IVsPackageDefinedTextMarkerType, IVsMergeableUIItem, IVsTextMarkerTypeProvider, IOleServiceProvider
    {
        // Both GUIDs are repeated in Install-VS2005.bat and Install-VS2008.bat.
        private static Guid MarkerGuid = new Guid("C188BC75-0A82-412B-901C-31B303F881F5");
        private static Guid ServiceGuid = new Guid("A436925E-DB48-4854-96B6-0C0C9E9A3C33");

        private const string Name = "NyoCoder Deleted Text";
        private const int S_OK = 0;
        private const int E_NOINTERFACE = unchecked((int)0x80004002);

        private static DeletionMarkerType _instance; // kept alive for the proffered service

        /// <summary>
        /// Proffers the marker service and returns the marker type ID to pass to
        /// CreateLineMarker, or 0 when the marker is not registered (install script not run).
        /// </summary>
        internal static int Register()
        {
            try
            {
                if (_instance == null)
                {
                    IProfferService proffer = Package.GetGlobalService(typeof(SProfferService)) as IProfferService;
                    if (proffer == null)
                        return 0;

                    DeletionMarkerType instance = new DeletionMarkerType();
                    uint cookie;
                    if (proffer.ProfferService(ref ServiceGuid, instance, out cookie) < 0)
                        return 0;
                    _instance = instance;
                }

                IVsTextManager textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
                int id;
                if (textManager == null || textManager.GetRegisteredMarkerTypeID(ref MarkerGuid, out id) != S_OK)
                    return 0;
                return id;
            }
            catch
            {
                return 0;
            }
        }

        // IOleServiceProvider: what the shell calls to reach the proffered service.
        public int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (guidService != ServiceGuid)
                return E_NOINTERFACE;

            IntPtr unknown = Marshal.GetIUnknownForObject(this);
            try { return Marshal.QueryInterface(unknown, ref riid, out ppvObject); }
            finally { Marshal.Release(unknown); }
        }

        // IVsTextMarkerTypeProvider
        public int GetTextMarkerType(ref Guid pguidMarker, out IVsPackageDefinedTextMarkerType ppMarkerType)
        {
            ppMarkerType = pguidMarker == MarkerGuid ? this : null;
            return ppMarkerType != null ? S_OK : E_NOINTERFACE;
        }

        // IVsPackageDefinedTextMarkerType
        public int GetVisualStyle(out uint pdwVisualFlags)
        {
            pdwVisualFlags = (uint)MARKERVISUAL.MV_COLOR_ALWAYS;
            return S_OK;
        }

        public int GetDefaultColors(COLORINDEX[] piForeground, COLORINDEX[] piBackground)
        {
            piForeground[0] = COLORINDEX.CI_WHITE;
            piBackground[0] = COLORINDEX.CI_MAROON;
            return S_OK;
        }

        public int GetDefaultLineStyle(COLORINDEX[] piLineColor, LINESTYLE[] piLineIndex)
        {
            piLineColor[0] = COLORINDEX.CI_RED;
            piLineIndex[0] = LINESTYLE.LI_NONE;
            return S_OK;
        }

        public int GetDefaultFontFlags(out uint pdwFontFlags)
        {
            pdwFontFlags = (uint)FONTFLAGS.FF_DEFAULT;
            return S_OK;
        }

        public int GetBehaviorFlags(out uint pdwFlags)
        {
            pdwFlags = (uint)MARKERBEHAVIORFLAGS.MB_DEFAULT;
            return S_OK;
        }

        public int GetPriorityIndex(out int piPriorityIndex)
        {
            piPriorityIndex = 500;
            return S_OK;
        }

        public int DrawGlyphWithColors(IntPtr hdc, Microsoft.VisualStudio.OLE.Interop.RECT[] pRect, int iMarkerType,
            IVsTextMarkerColorSet pMarkerColors, uint dwGlyphDrawFlags, int iLineHeight)
        {
            return S_OK; // no margin glyph
        }

        // IVsMergeableUIItem: lists the marker under Tools > Options > Fonts and Colors.
        public int GetCanonicalName(out string pbstrNonLocalizeName) { pbstrNonLocalizeName = Name; return S_OK; }
        public int GetDisplayName(out string pbstrDisplayName) { pbstrDisplayName = Name; return S_OK; }
        public int GetDescription(out string pbstrDesc) { pbstrDesc = Name; return S_OK; }
        public int GetMergingPriority(out int piMergingPriority) { piMergingPriority = 0x100; return S_OK; }
    }
}
