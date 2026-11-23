using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// VS 2017+ registered package. Uses IVs* indexing triggers; EnvDTE COM event sinks
    /// compiled against legacy PIAs throw MissingMethodException on VS2022.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(NyoCoderToolWindow))]
    [ProvideOptionPage(typeof(OptionsPage), "NyoCoder", "General", 0, 0, true)]
    [ProvideOptionPage(typeof(ContextOptionsPage), "NyoCoder", "Context", 0, 0, true)]
    [ProvideOptionPage(typeof(ToolsOptionsPage), "NyoCoder", "Tools", 0, 0, true)]
    [ProvideOptionPage(typeof(WebSearchOptionsPage), "NyoCoder", "Web Search", 0, 0, true)]
    [ProvideOptionPage(typeof(IndexingOptionsPage), "NyoCoder", "Indexing", 0, 0, true)]
    [ProvideOptionPage(typeof(AppearanceOptionsPage), "NyoCoder", "Appearance", 0, 0, true)]
    [ProvideOptionPage(typeof(BuildErrorHandlingOptionsPage), "NyoCoder", "Build Error Handling", 0, 0, true)]
    [ProvideOptionPage(typeof(ModesOptionsPage), "NyoCoder", "Modes", 0, 0, true)]
    [Guid(GuidList.guidNyoCoder_VSIXPkgString)]
    public sealed class NyoCoder_VSIXPackage :
        NyoCoderPackageBase,
        IVsSolutionEvents,
        IVsTrackProjectDocumentsEvents2,
        IVsRunningDocTableEvents
    {
        private IVsSolution _vsSolution;
        private IVsTrackProjectDocuments2 _vsTrackDocs;
        private IVsRunningDocumentTable _vsRdt;
        private uint _solutionEventsCookie;
        private uint _trackDocsCookie;
        private uint _rdtCookie;

        /// <summary>
        /// Gets the singleton instance of the registered package.
        /// </summary>
        public new static NyoCoder_VSIXPackage Instance
        {
            get { return (NyoCoder_VSIXPackage)_instance; }
        }

        public NyoCoder_VSIXPackage()
        {
        }

        protected override void SetupIndexingTriggers()
        {
            try
            {
                _vsSolution = GetService(typeof(SVsSolution)) as IVsSolution;
                if (_vsSolution != null)
                    ErrorHandler.ThrowOnFailure(_vsSolution.AdviseSolutionEvents(this, out _solutionEventsCookie));

                _vsTrackDocs = GetService(typeof(SVsTrackProjectDocuments)) as IVsTrackProjectDocuments2;
                if (_vsTrackDocs != null)
                    ErrorHandler.ThrowOnFailure(_vsTrackDocs.AdviseTrackProjectDocumentsEvents(this, out _trackDocsCookie));

                _vsRdt = GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
                if (_vsRdt != null)
                    ErrorHandler.ThrowOnFailure(_vsRdt.AdviseRunningDocTableEvents(this, out _rdtCookie));

                try
                {
                    DTE2 dte = ApplicationObject;
                    if (dte != null && dte.Solution != null && dte.Solution.IsOpen)
                        OnSolutionOpened();
                }
                catch { }
            }
            catch
            {
                // Indexing triggers are best-effort; never block package init.
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_vsRdt != null && _rdtCookie != 0)
                {
                    _vsRdt.UnadviseRunningDocTableEvents(_rdtCookie);
                    _rdtCookie = 0;
                }
                if (_vsTrackDocs != null && _trackDocsCookie != 0)
                {
                    _vsTrackDocs.UnadviseTrackProjectDocumentsEvents(_trackDocsCookie);
                    _trackDocsCookie = 0;
                }
                if (_vsSolution != null && _solutionEventsCookie != 0)
                {
                    _vsSolution.UnadviseSolutionEvents(_solutionEventsCookie);
                    _solutionEventsCookie = 0;
                }
            }
            catch { }

            base.Dispose(disposing);
        }

        #region IVsSolutionEvents

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            OnSolutionOpened();
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            OnSolutionClosed();
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) { return VSConstants.S_OK; }
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) { return VSConstants.S_OK; }
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) { return VSConstants.S_OK; }
        public int OnBeforeCloseSolution(object pUnkReserved) { return VSConstants.S_OK; }
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) { return VSConstants.S_OK; }
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) { return VSConstants.S_OK; }
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) { return VSConstants.S_OK; }
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) { return VSConstants.S_OK; }

        #endregion

        #region IVsRunningDocTableEvents (save → re-index)

        public int OnAfterSave(uint docCookie)
        {
            try
            {
                if (!ConfigHandler.GetIndexOnSave() || _vsRdt == null)
                    return VSConstants.S_OK;

                string path = GetRdtDocumentPath(docCookie);
                if (!string.IsNullOrEmpty(path))
                    CodebaseIndexer.RequestIndexFile(path);
            }
            catch { }
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) { return VSConstants.S_OK; }
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) { return VSConstants.S_OK; }
        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) { return VSConstants.S_OK; }
        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame) { return VSConstants.S_OK; }
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) { return VSConstants.S_OK; }

        private string GetRdtDocumentPath(uint docCookie)
        {
            try
            {
                uint flags, readLocks, editLocks, itemid;
                string path;
                IVsHierarchy hierarchy;
                IntPtr docData;
                ErrorHandler.ThrowOnFailure(_vsRdt.GetDocumentInfo(
                    docCookie, out flags, out readLocks, out editLocks,
                    out path, out hierarchy, out itemid, out docData));
                if (docData != IntPtr.Zero)
                    Marshal.Release(docData);
                return path;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region IVsTrackProjectDocumentsEvents2 (add/remove/rename)

        public int OnAfterRemoveFiles(
            int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices,
            string[] rgpszMkDocuments, VSREMOVEFILEFLAGS[] rgFlags)
        {
            try
            {
                if (rgpszMkDocuments == null)
                    return VSConstants.S_OK;
                for (int i = 0; i < cFiles && i < rgpszMkDocuments.Length; i++)
                {
                    string path = rgpszMkDocuments[i];
                    if (!string.IsNullOrEmpty(path))
                        CodebaseIndexer.RequestRemoveFile(path);
                }
            }
            catch { }
            return VSConstants.S_OK;
        }

        public int OnAfterRenameFiles(
            int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices,
            string[] rgszMkOldNames, string[] rgszMkNewNames, VSRENAMEFILEFLAGS[] rgFlags)
        {
            try
            {
                if (rgszMkOldNames == null || rgszMkNewNames == null)
                    return VSConstants.S_OK;
                int count = Math.Min(cFiles, Math.Min(rgszMkOldNames.Length, rgszMkNewNames.Length));
                for (int i = 0; i < count; i++)
                    CodebaseIndexer.RequestRenameFile(rgszMkOldNames[i], rgszMkNewNames[i]);
            }
            catch { }
            return VSConstants.S_OK;
        }

        public int OnAfterAddFilesEx(
            int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices,
            string[] rgpszMkDocuments, VSADDFILEFLAGS[] rgFlags)
        {
            try
            {
                if (rgpszMkDocuments == null || !ConfigHandler.GetIndexOnSave())
                    return VSConstants.S_OK;
                for (int i = 0; i < cFiles && i < rgpszMkDocuments.Length; i++)
                {
                    string path = rgpszMkDocuments[i];
                    if (!string.IsNullOrEmpty(path))
                        CodebaseIndexer.RequestIndexFile(path);
                }
            }
            catch { }
            return VSConstants.S_OK;
        }

        public int OnAfterAddDirectoriesEx(int cProjects, int cDirectories, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSADDDIRECTORYFLAGS[] rgFlags) { return VSConstants.S_OK; }
        public int OnAfterRemoveDirectories(int cProjects, int cDirectories, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSREMOVEDIRECTORYFLAGS[] rgFlags) { return VSConstants.S_OK; }
        public int OnAfterRenameDirectories(int cProjects, int cDirs, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgszMkOldNames, string[] rgszMkNewNames, VSRENAMEDIRECTORYFLAGS[] rgFlags) { return VSConstants.S_OK; }
        public int OnAfterSccStatusChanged(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, uint[] rgdwSccStatus) { return VSConstants.S_OK; }
        public int OnQueryAddDirectories(IVsProject pProject, int cDirectories, string[] rgpszMkDocuments, VSQUERYADDDIRECTORYFLAGS[] rgFlags, VSQUERYADDDIRECTORYRESULTS[] pSummaryResult, VSQUERYADDDIRECTORYRESULTS[] rgResults) { return VSConstants.S_OK; }
        public int OnQueryAddFiles(IVsProject pProject, int cFiles, string[] rgpszMkDocuments, VSQUERYADDFILEFLAGS[] rgFlags, VSQUERYADDFILERESULTS[] pSummaryResult, VSQUERYADDFILERESULTS[] rgResults) { return VSConstants.S_OK; }
        public int OnQueryRemoveDirectories(IVsProject pProject, int cDirectories, string[] rgpszMkDocuments, VSQUERYREMOVEDIRECTORYFLAGS[] rgFlags, VSQUERYREMOVEDIRECTORYRESULTS[] pSummaryResult, VSQUERYREMOVEDIRECTORYRESULTS[] rgResults) { return VSConstants.S_OK; }
        public int OnQueryRemoveFiles(IVsProject pProject, int cFiles, string[] rgpszMkDocuments, VSQUERYREMOVEFILEFLAGS[] rgFlags, VSQUERYREMOVEFILERESULTS[] pSummaryResult, VSQUERYREMOVEFILERESULTS[] rgResults) { return VSConstants.S_OK; }
        public int OnQueryRenameDirectories(IVsProject pProject, int cDirs, string[] rgszMkOldNames, string[] rgszMkNewNames, VSQUERYRENAMEDIRECTORYFLAGS[] rgFlags, VSQUERYRENAMEDIRECTORYRESULTS[] pSummaryResult, VSQUERYRENAMEDIRECTORYRESULTS[] rgResults) { return VSConstants.S_OK; }
        public int OnQueryRenameFiles(IVsProject pProject, int cFiles, string[] rgszMkOldNames, string[] rgszMkNewNames, VSQUERYRENAMEFILEFLAGS[] rgFlags, VSQUERYRENAMEFILERESULTS[] pSummaryResult, VSQUERYRENAMEFILERESULTS[] rgResults) { return VSConstants.S_OK; }

        #endregion
    }
}
