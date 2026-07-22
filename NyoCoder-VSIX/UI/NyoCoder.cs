using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Windows;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// Tool window that hosts the NyoCoder user control.
    /// </summary>
    [Guid(GuidList.guidToolWindowPersistanceString)]
    public class NyoCoderToolWindow : ToolWindowPane
    {
        private NyoCoderControl _control;

        /// <summary>
        /// Gets the NyoCoderControl instance.
        /// </summary>
        public NyoCoderControl Control
        {
            get { return _control; }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public NyoCoderToolWindow() :
            base(null)
        {
            this.Caption = Resources.ToolWindowTitle;
            this.BitmapResourceID = 301;
            this.BitmapIndex = 2;

            _control = new NyoCoderControl();
            base.Content = _control;
        }
    }
}
