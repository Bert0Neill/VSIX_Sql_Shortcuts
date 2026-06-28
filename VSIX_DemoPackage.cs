// ==============================================
// VSIX_DemoPackage Class
// ... (license header unchanged) ...
// ==============================================

extern alias ssms22;

using EnvDTE;
using EnvDTE80;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using SQL_Shortcuts_SSMS21Plus.Resources;
using SSMS_SqlAdjust_Interfaces;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using VSIX_SqlShortcuts.Classes;
using VSIX_SqlShortcuts.Models;
using VSIX_SqlShortcuts.Models.MenuObjects;
using Task = System.Threading.Tasks.Task;

namespace VSIX_SqlShortcuts
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class VSIX_DemoPackage : AsyncPackage
    {
        public const string PackageGuidString = "4607758b-acdd-4838-adb8-1dc2cfa69b00";

        private DTE2 _dte;
        private IVsTextViewFilter _textViewFilter;
        private IVsStatusbar _statusBar;
        private string _filePath = string.Empty;
        private FileService _fileService;

        private TreeView treeView;
        private IObjectExplorerService objectExplorerService;
        private SsmsHelper _helperService;
        public static IObjectExplorerHelper _smoObjectExplorer;  // This is where we store the selected table name

        // Optional: store the full NodeInfo for more details
        public static NodeInfo SelectedNodeInfo { get; private set; }

        public static BindingList<SqlShortcuts> _sqlShortcuts = new BindingList<SqlShortcuts>();
        public static bool IsDemo = true;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            try
            {
                try
                {
                    _helperService = new SsmsHelper();
                    var ssmsVersion = _helperService.RetrieveSsmsVersion();
                    Debug.WriteLine($"Extension loaded - SSMS version: {ssmsVersion}");

                    InitializeSmoObjectExplorer(ssmsVersion);
                    objectExplorerService = _smoObjectExplorer.GetObjectExplorer();
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error(ex.ToString());
                    return;
                }

                if (objectExplorerService == null)
                    return;

                // Get the TreeView from Object Explorer
                var treeProperty = objectExplorerService.GetType()
                    .GetProperty("Tree", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (treeProperty == null)
                    return;

                treeView = treeProperty.GetValue(objectExplorerService, null) as TreeView;
                if (treeView == null)
                    return;

                treeView.AfterSelect += TreeView_AfterSelect; // Hook into selection changes

                Debug.WriteLine("SSMS integration complete");

                string logFilename = SimpleLogger.Initialize(SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.LogFilename);
                SimpleLogger.Info(Resource.Package_Initializing);

                LoadSqlShortcuts();
                await LoadVsStatusBarReference();
                await MonitorEditorWindow();
                await VSIX_SqlShortcuts.Commands.cmdShortcuts.InitializeAsync(this);

                string message = string.Format(Resource.StatusBar_Status, $"SQL Shortcut Ext. - Log file: {logFilename}");
                StatusBarService.UpdateStatusBarText(message);
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"{ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        // Initialize SMO helper (no event hook – selection handled by TreeView)
        // -----------------------------------------------------------------
        private void InitializeSmoObjectExplorer(string ssmsVersion)
        {
            switch (ssmsVersion)
            {
                //case "SSMS 21":
                //    _smoObjectExplorer = new ssms21::SSMS_21_SqlAdjust.SmoObjectExplorer(this);
                //    break;
                case "SSMS 22":
                    _smoObjectExplorer = new ssms22::SSMS_22_SqlAdjust.SmoObjectExplorer(this);
                    break;
                default:
                    _smoObjectExplorer = new ssms22::SSMS_22_SqlAdjust.SmoObjectExplorer(this);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // TreeView selection event – populates _smoObjectExplorer.SelectedSmoNode
        // -----------------------------------------------------------------
        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                objectExplorerService.GetSelectedNodes(out int arraySize, out INodeInformation[] nodes);
                if (arraySize > 0 && nodes != null && nodes.Length > 0)
                {
                    var node = nodes[0];
                    SelectedNodeInfo = node.GetInfo(); // optional, for extra data

                    // Extract details via reflection
                    string name = GetProperty(node, "InvariantName");
                    string path = GetProperty(node, "UrnPath");
                    string nodeType = node.GetType().Name;

                    Debug.WriteLine($"📌 Selected: {name} (Type: {nodeType})");
                    Debug.WriteLine($"   Path: {path}");

                    // Check if it's a Table or View
                    if (!string.IsNullOrEmpty(path) && (path.EndsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                                                         path.EndsWith("View", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Store the table/view name in the SMO helper for use by shortcuts
                        _smoObjectExplorer.SelectedSmoNode = name;
                        Debug.WriteLine($"✅ Table/View selected: {name}");
                    }
                    else
                    {
                        // Not a table/view – clear the stored name
                        _smoObjectExplorer.SelectedSmoNode = null;
                    }
                }
                else
                {
                    // No node selected – clear
                    _smoObjectExplorer.SelectedSmoNode = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TreeView_AfterSelect: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        // On‑demand selection retrieval (alternative if you don't want to rely on the event)
        // -----------------------------------------------------------------
        public INodeInformation GetCurrentSelectedNode()
        {
            if (objectExplorerService == null) return null;
            objectExplorerService.GetSelectedNodes(out int arraySize, out INodeInformation[] nodes);
            return (arraySize > 0 && nodes != null && nodes.Length > 0) ? nodes[0] : null;
        }

        // -----------------------------------------------------------------
        // Helper to get a property from INodeInformation using reflection
        // -----------------------------------------------------------------
        private string GetProperty(INodeInformation node, string propertyName)
        {
            if (node == null) return null;
            try
            {
                var prop = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                return prop?.GetValue(node)?.ToString();
            }
            catch { return null; }
        }

        // -----------------------------------------------------------------
        // The rest of the class (unchanged)
        // -----------------------------------------------------------------
        private async Task MonitorEditorWindow()
        {
            try
            {
                _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
                if (_dte != null)
                    _dte.Events.WindowEvents.WindowActivated += OnWindowActivated;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error in MonitorEditorWindow: {ex.Message}");
            }
        }

        private async Task LoadVsStatusBarReference()
        {
            _statusBar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            if (_statusBar != null)
                StatusBarService.Initialize(_statusBar);
        }

        private void LoadSqlShortcuts()
        {
            try
            {
                string FOLDER_FILENAME = SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.ConfigFile;
                _fileService = new FileService();
                _filePath = Path.Combine(Directory.GetCurrentDirectory(), FOLDER_FILENAME);

                if (File.Exists(_filePath))
                {
                    _sqlShortcuts = _fileService.LoadFromJson<BindingList<SqlShortcuts>>(_filePath);
                }
                else
                {
                    _sqlShortcuts = new BindingList<SqlShortcuts>()
                    {
                        new SqlShortcuts { SqlShort = "w", SqlComplete = "Select * From <Table> Where CreatedDate > {DateTime.Now.ToString(\"d\")} " },
                        new SqlShortcuts { SqlShort = "s", SqlComplete = "Select * From <Table>" },
                        new SqlShortcuts { SqlShort = "d", SqlComplete = "Delete * From " },
                        new SqlShortcuts { SqlShort = "i", SqlComplete = "Insert Into " }
                    };
                }
                if (VSIX_DemoPackage.IsDemo)
                    SimpleLogger.Info("Demo mode: Limited shortcuts available");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error loading SQL shortcuts: {ex.Message}");
            }
        }

        private void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                const string ACCEPT_FILE_TYPE = "SQL";
                const string ACCEPT_FILE_EXT = ".sql";

                if (gotFocus?.Document == null)
                {
                    Debug.WriteLine("VSIX Package: No document in focus");
                    return;
                }

                bool isSqlFile = false;
                try
                {
                    string docLanguage = gotFocus.Document.Language;
                    string docFileName = gotFocus.Document.FullName;
                    SimpleLogger.Info($"VSIX Package: Document language: {docLanguage}, Filename: {docFileName}");
                    isSqlFile = docLanguage == ACCEPT_FILE_TYPE || docFileName.EndsWith(ACCEPT_FILE_EXT, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error($"Error checking document type: {ex.Message}");
                    return;
                }

                if (isSqlFile)
                {
                    Debug.WriteLine("VSIX Package: SQL file detected, setting up event handlers");
                    var componentModel = this.GetService(typeof(SComponentModel)) as IComponentModel;
                    if (componentModel == null)
                    {
                        Debug.WriteLine("VSIX Package: ComponentModel is null");
                        return;
                    }

                    var editorFactory = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                    var textManager = this.GetService(typeof(SVsTextManager)) as IVsTextManager;

                    if (textManager == null || editorFactory == null)
                    {
                        Debug.WriteLine("VSIX Package: TextManager or EditorFactory is null");
                        return;
                    }

                    IVsTextView textView;
                    int result = textManager.GetActiveView(1, null, out textView);
                    if (ErrorHandler.Failed(result) || textView == null)
                    {
                        Debug.WriteLine($"VSIX Package: Failed to get active text view, result: {result}");
                        return;
                    }

                    var wpfTextView = editorFactory.GetWpfTextView(textView);
                    if (wpfTextView != null)
                    {
                        Debug.WriteLine("VSIX Package: Setting up text view filter");
                        IVsTextView nativeTextView = editorFactory.GetViewAdapter(wpfTextView);
                        if (nativeTextView != null)
                        {
                            _textViewFilter = new ShortcutEvaluation(this, nativeTextView);
                            result = nativeTextView.AddCommandFilter(
                                (IOleCommandTarget)_textViewFilter,
                                out IOleCommandTarget nextCommandFilter
                            );

                            if (result == VSConstants.S_OK)
                            {
                                ((ShortcutEvaluation)_textViewFilter)._nextFilter = nextCommandFilter;
                                Debug.WriteLine("VSIX Package: Tab key filter installed successfully");
                            }
                            else
                            {
                                Debug.WriteLine($"VSIX Package: Failed to install tab key filter, result: {result}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error in OnWindowActivated: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (treeView != null)
                        treeView.AfterSelect -= TreeView_AfterSelect;

                    _sqlShortcuts?.Clear();
                    _sqlShortcuts = null;
                    _fileService = null;

                    if (_textViewFilter is IDisposable disposableFilter)
                        disposableFilter.Dispose();
                }
                base.Dispose(disposing);
            }
            catch (InvalidCastException ex)
            {
                SimpleLogger.Info($"Unable to access DTE during disposal: {ex.Message}");
            }
            catch (Exception ex)
            {
                SimpleLogger.Info($"Unexpected error in Dispose: {ex.Message}");
            }
        }
    }
}