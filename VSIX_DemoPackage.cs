// VSIX_DemoPackage Class
//
// Description:
// This class is the main package for the VSIX extension. It interacts with Visual Studio's
// environment (DTE) to manage text editor windows, load SQL shortcuts, and interact with 
// status bars and text views. The package also initializes logging and handles events 
// related to editor window activation and document type checks for SQL files.
//
// Features:
// - Initializes the VSIX package with logging and configuration.
// - Loads and manages SQL shortcuts from a configuration file.
// - Monitors active editor windows and applies filters for SQL file documents.
// - Updates Visual Studio's status bar based on actions in the editor.
//
// License Agreement:
// © Bert O'Neill Software - 2026. All rights reserved.
//
// This Software and any associated materials are confidential and proprietary to Bert O'Neill ("Licensor").
// By using this Software, you are agreeing to comply with the terms of this License Agreement.
//
// 1. Grant of License:
//    Licensor grants you a limited, non-exclusive, non-transferable license to use the Software for internal business operations on a single machine or as otherwise specified in the applicable agreement with Licensor.
//
// 2. Restrictions:
//    You may not:
//    - Copy, modify, distribute, or create derivative works of the Software, in whole or in part, except as expressly permitted in writing by Licensor.
//    - Reverse-engineer, decompile, or disassemble the Software, or attempt to discover its source code or underlying algorithms.
//    - Use the Software for any illegal purpose or in a manner inconsistent with the terms of this License.
//
// 3. Ownership:
//    All rights, title, and interest in the Software, including any updates or modifications, remain exclusively with Licensor. The Software is licensed, not sold.
//
// 4. Warranties and Liability:
//    The Software is provided "as-is," and Licensor makes no warranties, either express or implied, regarding its functionality, fitness for a particular purpose, or any other aspect of its use. In no event shall Licensor be liable for any direct, indirect, incidental, or consequential damages arising from the use or inability to use the Software.
//
// 5. License Termination:
//    Licensor may terminate this license at any time if you breach any of the terms of this License Agreement. Upon termination, you must immediately cease all use of the Software and return or destroy all copies of the Software.
//
// 6. Support:
//    Licensor may offer support for the Software under a separate agreement. You must have an active support contract to receive any updates or assistance.
//
// For licensing inquiries, support, or additional information, please contact:
// Bert O'Neill
// Email: BertONeill@Yahoo.com
//
// ==============================================

using EnvDTE;
using EnvDTE80;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SQL_Shortcuts_SSMS21Plus.Helper;
using SQL_Shortcuts_SSMS21Plus.Resources;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VSIX_SqlShortcuts.Classes;
using VSIX_SqlShortcuts.Models;

namespace VSIX_SqlShortcuts
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class VSIX_DemoPackage : AsyncPackage
    {
        // =====================================================
        // OBJECT EXPLORER
        // =====================================================
        private IObjectExplorerService _objectExplorer;
        private CancellationTokenSource _oeCts;

        public static string SelectedSmoNode { get; private set; }
        private string _lastUrn;
        private string _lastNodeSignature;

        // =====================================================
        // EDITOR
        // =====================================================
        private DTE2 _dte;
        private IComponentModel _componentModel;
        private IWpfTextView _currentView;

        public static BindingList<SqlShortcuts> _sqlShortcuts = new BindingList<SqlShortcuts>();

        private IVsTextViewFilter _textViewFilter;
        private IVsStatusbar _statusBar;
        private string _filePath = string.Empty;
        private FileService _fileService;

        // =====================================================
        // INIT
        // =====================================================
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // -----------------------------
            // Object Explorer
            // -----------------------------
            _objectExplorer = GetService(typeof(IObjectExplorerService)) as IObjectExplorerService;

            if (_objectExplorer == null)
                return;

            StartObjectExplorerMonitor();

            // -----------------------------
            // Editor hook (DTE-based)
            // -----------------------------
            //await HookEditorAsync();


            string logFilename = SimpleLogger.Initialize(SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.LogFilename);
            SimpleLogger.Info(Resource.Package_Initializing);

            LoadSqlShortcuts();
            await LoadVsStatusBarReference();
            await MonitorEditorWindow();
            await VSIX_SqlShortcuts.Commands.cmdShortcuts.InitializeAsync(this);

            string message = string.Format(Resource.StatusBar_Status, $"SQL Shortcut Ext. - Log file: {logFilename}");
            StatusBarService.UpdateStatusBarText(message);
        }

        // =====================================================
        // OBJECT EXPLORER MONITOR (SSMS SAFE)
        // =====================================================
        private void StartObjectExplorerMonitor()
        {
            _oeCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();

                while (!_oeCts.IsCancellationRequested)
                {
                    try
                    {
                        _objectExplorer.GetSelectedNodes(
                            out int count,
                            out INodeInformation[] nodes);

                        if (count > 0 && nodes != null && nodes.Length > 0)
                        {
                            var node = nodes[0];

                            string name = Get(node, "InvariantName");

                            // 🔥 THIS is the key fix
                            string urn = TryGetUrn(node);

                            if (string.IsNullOrEmpty(urn))
                                continue;

                            bool isTable =
                                urn.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0;

                            bool isView =
                                urn.IndexOf("View", StringComparison.OrdinalIgnoreCase) >= 0;

                            string signature = name + "|" + urn;

                            if (signature != _lastUrn)
                            {
                                _lastUrn = signature;

                                if (isTable || isView)
                                {
                                    SsmsState.Instance.SelectedTableOrView = name;
                                }
                                else
                                {
                                    SsmsState.Instance.SelectedTableOrView = null;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("OE error: " + ex.Message);
                    }

                    await Task.Delay(150);
                }
            });
        }

        private string TryGetUrn(INodeInformation node)
        {
            try
            {
                // SSMS sometimes exposes Urn as method, not property
                var type = node.GetType();

                var prop = type.GetProperty("Urn");
                if (prop != null)
                    return prop.GetValue(node)?.ToString();

                var prop2 = type.GetProperty("UrnPath");
                if (prop2 != null)
                    return prop2.GetValue(node)?.ToString();

                var method = type.GetMethod("GetUrn");
                if (method != null)
                    return method.Invoke(node, null)?.ToString();
            }
            catch { }

            return null;
        }

        // =====================================================
        // EDITOR HOOK (SSMS SAFE)
        // =====================================================
        //private async Task HookEditorAsync()
        //{
        //    await JoinableTaskFactory.SwitchToMainThreadAsync();

        //    _dte = await GetServiceAsync(typeof(DTE)) as DTE2;

        //    if (_dte == null)
        //        return;

        //    _dte.Events.WindowEvents.WindowActivated += OnWindowActivated;
        //}

        //private void OnWindowActivated(Window gotFocus, Window lostFocus)
        //{
        //    ThreadHelper.ThrowIfNotOnUIThread();

        //    try
        //    {
        //        if (gotFocus?.Document == null)
        //            return;

        //        if (!gotFocus.Document.FullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        //            return;

        //        _componentModel =
        //            GetService(typeof(SComponentModel)) as IComponentModel;

        //        var factory =
        //            _componentModel?.GetService<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>();

        //        var textManager =
        //            GetService(typeof(SVsTextManager)) as IVsTextManager;

        //        if (factory == null || textManager == null)
        //            return;

        //        textManager.GetActiveView(1, null, out IVsTextView vsView);

        //        if (vsView == null)
        //            return;

        //        var wpfView = factory.GetWpfTextView(vsView);

        //        if (wpfView == null)
        //            return;

        //        // avoid duplicate hookup
        //        if (_currentView == wpfView)
        //            return;

        //        _currentView = wpfView;

        //        _currentView.TextBuffer.Changed -= OnTextChanged;
        //        _currentView.TextBuffer.Changed += OnTextChanged;
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine(ex.Message);
        //    }
        //}

        // =====================================================
        // KEYSTONE: REAL TEXT CAPTURE
        // =====================================================
        private void OnTextChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                var buffer = sender as ITextBuffer;
                if (buffer == null)
                    return;

                string text = buffer.CurrentSnapshot.GetText();

                // SIMPLE TRIGGERS (you can refine later)
                if (text.EndsWith(" s"))
                    Expand(buffer, "s");

                else if (text.EndsWith(" w"))
                    Expand(buffer, "w");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        // =====================================================
        // SHORTCUT EXPANSION
        // =====================================================
        private void Expand(ITextBuffer buffer, string shortcut)
        {
            //string table = SelectedSmoNode ?? "<Table>";
            string table = SsmsState.Instance.SelectedTableOrView ?? "<Table>";

            string replacement = null;

            if (shortcut == "s")
            {
                replacement = "SELECT * FROM " + table;
            }
            else if (shortcut == "w")
            {
                replacement = "SELECT * FROM " + table + " WHERE 1=1";
            }
            else if (shortcut == "d")
            {
                replacement = "DELETE FROM " + table;
            }
            else if (shortcut == "i")
            {
                replacement = "INSERT INTO " + table;
            }

            if (string.IsNullOrEmpty(replacement))
                return;

            var snapshot = buffer.CurrentSnapshot;

            if (snapshot.Length < 2)
                return;

            // replace last 2 characters (space + shortcut)
            var span = new Microsoft.VisualStudio.Text.Span(snapshot.Length - 2, 2);

            buffer.Replace(span, replacement);
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private string Get(INodeInformation node, string prop)
        {
            try
            {
                var p = node.GetType().GetProperty(prop);
                return p?.GetValue(node)?.ToString();
            }
            catch
            {
                return null;
            }
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
                
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error loading SQL shortcuts: {ex.Message}");
            }
        }

        // =====================================================
        // DISPOSE
        // =====================================================
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _oeCts?.Cancel();

            base.Dispose(disposing);
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

        
    }

}



//extern alias ssms22;
//// extern alias ssms21;

//using EnvDTE;
//using EnvDTE80;
//using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
//using Microsoft.VisualStudio;
//using Microsoft.VisualStudio.ComponentModelHost;
//using Microsoft.VisualStudio.Editor;
//using Microsoft.VisualStudio.OLE.Interop;
//using Microsoft.VisualStudio.Shell;
//using Microsoft.VisualStudio.Shell.Interop;
//using Microsoft.VisualStudio.TextManager.Interop;
//using SQL_Shortcuts_SSMS21Plus.Resources;
//using SSMS_SqlAdjust_Interfaces;
//using System;
//using System.ComponentModel;
//using System.Diagnostics;
//using System.IO;
//using System.Reflection;
//using System.Threading;
//using System.Windows.Forms;
//using VSIX_SqlShortcuts.Classes;
//using VSIX_SqlShortcuts.Models;
//using VSIX_SqlShortcuts.Models.MenuObjects;
//using Task = System.Threading.Tasks.Task;

//namespace VSIX_SqlShortcuts
//{
//    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
//    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
//    [ProvideMenuResource("Menus.ctmenu", 1)]
//    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
//    public sealed class VSIX_DemoPackage : AsyncPackage
//    {
//        public const string PackageGuidString = "4607758b-acdd-4838-adb8-1dc2cfa69b00";

//        private DTE2 _dte;
//        private IVsTextViewFilter _textViewFilter;
//        private IVsStatusbar _statusBar;



//private string _filePath = string.Empty;
//        private FileService _fileService;

//        private TreeView treeView;
//        private IObjectExplorerService objectExplorerService;
//        private SsmsHelper _helperService;
//        public static IObjectExplorerHelper _smoObjectExplorer;  // This is where we store the selected table name

//        // Optional: store the full NodeInfo for more details
//        public static NodeInfo SelectedNodeInfo { get; private set; }

//        public static BindingList<SqlShortcuts> _sqlShortcuts = new BindingList<SqlShortcuts>();
//        public static bool IsDemo = true;

//        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
//        {
//            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
//            try
//            {
//                try
//                {
//                    _helperService = new SsmsHelper();
//                    var ssmsVersion = _helperService.RetrieveSsmsVersion();
//                    Debug.WriteLine($"Extension loaded - SSMS version: {ssmsVersion}");

//                    InitializeSmoObjectExplorer(ssmsVersion);
//                    objectExplorerService = _smoObjectExplorer.GetObjectExplorer();
//                }
//                catch (Exception ex)
//                {
//                    SimpleLogger.Error(ex.ToString());
//                    return;
//                }

//                if (objectExplorerService == null)
//                    return;

//                // Get the TreeView from Object Explorer
//                var treeProperty = objectExplorerService.GetType()
//                    .GetProperty("Tree", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
//                if (treeProperty == null)
//                    return;

//                treeView = treeProperty.GetValue(objectExplorerService, null) as TreeView;
//                if (treeView == null)
//                    return;

//                treeView.AfterSelect += TreeView_AfterSelect; // Hook into selection changes

//                Debug.WriteLine("SSMS integration complete");

//                string logFilename = SimpleLogger.Initialize(SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.LogFilename);
//                SimpleLogger.Info(Resource.Package_Initializing);

//                LoadSqlShortcuts();
//                await LoadVsStatusBarReference();
//                await MonitorEditorWindow();
//                await VSIX_SqlShortcuts.Commands.cmdShortcuts.InitializeAsync(this);

//                string message = string.Format(Resource.StatusBar_Status, $"SQL Shortcut Ext. - Log file: {logFilename}");
//                StatusBarService.UpdateStatusBarText(message);
//            }
//            catch (Exception ex)
//            {
//                SimpleLogger.Error($"{ex.Message}");
//            }
//        }

//        // -----------------------------------------------------------------
//        // Initialize SMO helper (no event hook – selection handled by TreeView)
//        // -----------------------------------------------------------------
//        private void InitializeSmoObjectExplorer(string ssmsVersion)
//        {
//            switch (ssmsVersion)
//            {
//                //case "SSMS 21":
//                //    _smoObjectExplorer = new ssms21::SSMS_21_SqlAdjust.SmoObjectExplorer(this);
//                //    break;
//                case "SSMS 22":
//                    _smoObjectExplorer = new ssms22::SSMS_22_SqlAdjust.SmoObjectExplorer(this);
//                    break;
//                default:
//                    _smoObjectExplorer = new ssms22::SSMS_22_SqlAdjust.SmoObjectExplorer(this);
//                    break;
//            }
//        }

//        // -----------------------------------------------------------------
//        // TreeView selection event – populates _smoObjectExplorer.SelectedSmoNode
//        // -----------------------------------------------------------------
//        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
//        {
//            try
//            {
//                objectExplorerService.GetSelectedNodes(out int arraySize, out INodeInformation[] nodes);
//                if (arraySize > 0 && nodes != null && nodes.Length > 0)
//                {
//                    var node = nodes[0];
//                    SelectedNodeInfo = node.GetInfo(); // optional, for extra data

//                    // Extract details via reflection
//                    string name = GetProperty(node, "InvariantName");
//                    string path = GetProperty(node, "UrnPath");
//                    string nodeType = node.GetType().Name;

//                    Debug.WriteLine($"📌 Selected: {name} (Type: {nodeType})");
//                    Debug.WriteLine($"   Path: {path}");

//                    // Check if it's a Table or View
//                    if (!string.IsNullOrEmpty(path) && (path.EndsWith("Table", StringComparison.OrdinalIgnoreCase) ||
//                                                         path.EndsWith("View", StringComparison.OrdinalIgnoreCase)))
//                    {
//                        // Store the table/view name in the SMO helper for use by shortcuts
//                        _smoObjectExplorer.SelectedSmoNode = name;
//                        Debug.WriteLine($"✅ Table/View selected: {name}");
//                    }
//                    else
//                    {
//                        // Not a table/view – clear the stored name
//                        _smoObjectExplorer.SelectedSmoNode = null;
//                    }
//                }
//                else
//                {
//                    // No node selected – clear
//                    _smoObjectExplorer.SelectedSmoNode = null;
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"Error in TreeView_AfterSelect: {ex.Message}");
//            }
//        }

//        // -----------------------------------------------------------------
//        // On‑demand selection retrieval (alternative if you don't want to rely on the event)
//        // -----------------------------------------------------------------
//        public INodeInformation GetCurrentSelectedNode()
//        {
//            if (objectExplorerService == null) return null;
//            objectExplorerService.GetSelectedNodes(out int arraySize, out INodeInformation[] nodes);
//            return (arraySize > 0 && nodes != null && nodes.Length > 0) ? nodes[0] : null;
//        }

//        // -----------------------------------------------------------------
//        // Helper to get a property from INodeInformation using reflection
//        // -----------------------------------------------------------------
//        private string GetProperty(INodeInformation node, string propertyName)
//        {
//            if (node == null) return null;
//            try
//            {
//                var prop = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
//                return prop?.GetValue(node)?.ToString();
//            }
//            catch { return null; }
//        }




//        private void LoadSqlShortcuts()
//        {
//            try
//            {
//                string FOLDER_FILENAME = SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.ConfigFile;
//                _fileService = new FileService();
//                _filePath = Path.Combine(Directory.GetCurrentDirectory(), FOLDER_FILENAME);

//                if (File.Exists(_filePath))
//                {
//                    _sqlShortcuts = _fileService.LoadFromJson<BindingList<SqlShortcuts>>(_filePath);
//                }
//                else
//                {
//                    _sqlShortcuts = new BindingList<SqlShortcuts>()
//                    {
//                        new SqlShortcuts { SqlShort = "w", SqlComplete = "Select * From <Table> Where CreatedDate > {DateTime.Now.ToString(\"d\")} " },
//                        new SqlShortcuts { SqlShort = "s", SqlComplete = "Select * From <Table>" },
//                        new SqlShortcuts { SqlShort = "d", SqlComplete = "Delete * From " },
//                        new SqlShortcuts { SqlShort = "i", SqlComplete = "Insert Into " }
//                    };
//                }
//                if (VSIX_DemoPackage.IsDemo)
//                    SimpleLogger.Info("Demo mode: Limited shortcuts available");
//            }
//            catch (Exception ex)
//            {
//                SimpleLogger.Error($"Error loading SQL shortcuts: {ex.Message}");
//            }
//        }

//        private void OnWindowActivated(EnvDTE.Window gotFocus, EnvDTE.Window lostFocus)
//        {
//            ThreadHelper.ThrowIfNotOnUIThread();
//            try
//            {
//                const string ACCEPT_FILE_TYPE = "SQL";
//                const string ACCEPT_FILE_EXT = ".sql";

//                if (gotFocus?.Document == null)
//                {
//                    Debug.WriteLine("VSIX Package: No document in focus");
//                    return;
//                }

//                bool isSqlFile = false;
//                try
//                {
//                    string docLanguage = gotFocus.Document.Language;
//                    string docFileName = gotFocus.Document.FullName;
//                    SimpleLogger.Info($"VSIX Package: Document language: {docLanguage}, Filename: {docFileName}");
//                    isSqlFile = docLanguage == ACCEPT_FILE_TYPE || docFileName.EndsWith(ACCEPT_FILE_EXT, StringComparison.OrdinalIgnoreCase);
//                }
//                catch (Exception ex)
//                {
//                    SimpleLogger.Error($"Error checking document type: {ex.Message}");
//                    return;
//                }

//                if (isSqlFile)
//                {
//                    Debug.WriteLine("VSIX Package: SQL file detected, setting up event handlers");
//                    var componentModel = this.GetService(typeof(SComponentModel)) as IComponentModel;
//                    if (componentModel == null)
//                    {
//                        Debug.WriteLine("VSIX Package: ComponentModel is null");
//                        return;
//                    }

//                    var editorFactory = componentModel.GetService<IVsEditorAdaptersFactoryService>();
//                    var textManager = this.GetService(typeof(SVsTextManager)) as IVsTextManager;

//                    if (textManager == null || editorFactory == null)
//                    {
//                        Debug.WriteLine("VSIX Package: TextManager or EditorFactory is null");
//                        return;
//                    }

//                    IVsTextView textView;
//                    int result = textManager.GetActiveView(1, null, out textView);
//                    if (ErrorHandler.Failed(result) || textView == null)
//                    {
//                        Debug.WriteLine($"VSIX Package: Failed to get active text view, result: {result}");
//                        return;
//                    }

//                    var wpfTextView = editorFactory.GetWpfTextView(textView);
//                    if (wpfTextView != null)
//                    {
//                        Debug.WriteLine("VSIX Package: Setting up text view filter");
//                        IVsTextView nativeTextView = editorFactory.GetViewAdapter(wpfTextView);
//                        if (nativeTextView != null)
//                        {
//                            _textViewFilter = new ShortcutEvaluation(this, nativeTextView);
//                            result = nativeTextView.AddCommandFilter(
//                                (IOleCommandTarget)_textViewFilter,
//                                out IOleCommandTarget nextCommandFilter
//                            );

//                            if (result == VSConstants.S_OK)
//                            {
//                                ((ShortcutEvaluation)_textViewFilter)._nextFilter = nextCommandFilter;
//                                Debug.WriteLine("VSIX Package: Tab key filter installed successfully");
//                            }
//                            else
//                            {
//                                Debug.WriteLine($"VSIX Package: Failed to install tab key filter, result: {result}");
//                            }
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                SimpleLogger.Error($"Error in OnWindowActivated: {ex.Message}");
//            }
//        }

//        protected override void Dispose(bool disposing)
//        {
//            try
//            {
//                if (disposing)
//                {
//                    if (treeView != null)
//                        treeView.AfterSelect -= TreeView_AfterSelect;

//                    _sqlShortcuts?.Clear();
//                    _sqlShortcuts = null;
//                    _fileService = null;

//                    if (_textViewFilter is IDisposable disposableFilter)
//                        disposableFilter.Dispose();
//                }
//                base.Dispose(disposing);
//            }
//            catch (InvalidCastException ex)
//            {
//                SimpleLogger.Info($"Unable to access DTE during disposal: {ex.Message}");
//            }
//            catch (Exception ex)
//            {
//                SimpleLogger.Info($"Unexpected error in Dispose: {ex.Message}");
//            }
//        }
//    }
//}