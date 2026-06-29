using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Syncfusion.Licensing;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VSIX_SqlShortcuts.Forms;

namespace VSIX_SqlShortcuts.Commands
{
    [Guid("bf54b6c0-6ed7-4107-a84b-c1a031eab60a")]
    internal sealed class cmdShortcuts : IOleCommandTarget
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("bf54b6c0-6ed7-4107-a84b-c1a031eab60a");

        private readonly AsyncPackage _package;
        private uint _commandCookie; // For unregistering if needed

        public static cmdShortcuts Instance { get; private set; }

        private cmdShortcuts(AsyncPackage package)
        {
            _package = package;
            Instance = this;
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandTarget = new cmdShortcuts(package);

            // Register as a priority command target using the correct service
            var registerService = await package.GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget))
                                  as IVsRegisterPriorityCommandTarget;

            if (registerService != null)
            {
                // Register the command target
                int hr = registerService.RegisterPriorityCommandTarget(0, commandTarget, out commandTarget._commandCookie);
                if (ErrorHandler.Succeeded(hr))
                {
                    //Logger.Log("Priority command target registered successfully");
                }
                else
                {
                    //Logger.Log($"Failed to register priority command target: {hr}");
                }
            }
            else
            {
                //Logger.Log("IVsRegisterPriorityCommandTarget service is null");
            }

            // Also register via the standard command service for menu visibility
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var cmdId = new CommandID(CommandSet, CommandId);
                var menuCommand = new OleMenuCommand(commandTarget.Execute, cmdId);
                menuCommand.Enabled = true;
                menuCommand.Visible = true;
                commandService.AddCommand(menuCommand);
                //Logger.Log("Command added via service (for UI)");
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            //Logger.Log("Execute called!");
            try
            {
                SyncfusionLicenseProvider.RegisterLicense(
                    SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.SyncfusionKey);
                var frm = new frmShortcuts();
                frm.ShowDialog();
            }
            catch (Exception ex)
            {
                //Logger.Log($"Execute ERROR: {ex.Message}\n{ex.StackTrace}");
                System.Windows.Forms.MessageBox.Show($"Error: {ex.Message}", "Command Error");
            }
        }

        // IOleCommandTarget implementation
        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == CommandSet && nCmdID == CommandId)
            {
                Execute(null, null);
                return VSConstants.S_OK;
            }
            return VSConstants.E_FAIL;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup == CommandSet)
            {
                for (int i = 0; i < cCmds; i++)
                {
                    if (prgCmds[i].cmdID == CommandId)
                    {
                        prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                        return VSConstants.S_OK;
                    }
                }
            }
            return VSConstants.E_FAIL;
        }
    }




//public static class Logger
//    {
//        public static void Log(string msg)
//        {
//            var path = "C:\\Temp\\SSMS_Command.log";
            
//            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} – {msg}\n");
//        }
//    }
}

//using Microsoft.VisualStudio.Shell;
//using Syncfusion.Licensing;
//using System;
//using System.ComponentModel.Design;
//using System.Windows;
//using VSIX_SqlShortcuts.Forms;
//using Task = System.Threading.Tasks.Task;

//namespace VSIX_SqlShortcuts.Commands
//{
//    /// <summary>
//    /// Command handler
//    /// </summary>
//    internal sealed class cmdShortcuts
//    {
//        /// <summary>
//        /// Command ID.
//        /// </summary>
//        public const int CommandId = 0x0100;

//        /// <summary>
//        /// Command menu group (command set GUID).
//        /// </summary>
//        public static readonly Guid CommandSet = new Guid("bf54b6c0-6ed7-4107-a84b-c1a031eab60a");

//        /// <summary>
//        /// VS Package that provides this command, not null.
//        /// </summary>
//        private readonly AsyncPackage package;

//        /// <summary>
//        /// Initializes a new instance of the <see cref="cmdShortcuts"/> class.
//        /// Adds our command handlers for menu (commands must exist in the command table file)
//        /// </summary>
//        /// <param name="package">Owner package, not null.</param>
//        /// <param name="commandService">Command service to add command to, not null.</param>
//        private cmdShortcuts(AsyncPackage package, OleMenuCommandService commandService)
//        {
//            this.package = package ?? throw new ArgumentNullException(nameof(package));
//            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

//            var menuCommandID = new CommandID(CommandSet, CommandId);
//            var menuItem = new MenuCommand(this.Execute, menuCommandID);
//            commandService.AddCommand(menuItem);
//        }

//        /// <summary>
//        /// Gets the instance of the command.
//        /// </summary>
//        public static cmdShortcuts Instance
//        {
//            get;
//            private set;
//        }

//        /// <summary>
//        /// Gets the service provider from the owner package.
//        /// </summary>
//        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
//        {
//            get
//            {
//                return this.package;
//            }
//        }

//        /// <summary>
//        /// Initializes the singleton instance of the command.
//        /// </summary>
//        /// <param name="package">Owner package, not null.</param>
//        public static async Task InitializeAsync(AsyncPackage package)
//        {
//            // Switch to the main thread - the call to AddCommand in cmdShortcuts's constructor requires
//            // the UI thread.
//            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

//            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
//            Instance = new cmdShortcuts(package, commandService);
//        }

//        /// <summary>
//        /// This function is the callback used to execute the command when the menu item is clicked.
//        /// See the constructor to see how the menu item is associated with this function using
//        /// OleMenuCommandService service and MenuCommand class.
//        /// </summary>
//        /// <param name="sender">Event sender.</param>
//        /// <param name="e">Event args.</param>
//        private void Execute(object sender, EventArgs e)
//        {
//            ThreadHelper.ThrowIfNotOnUIThread();

//            SyncfusionLicenseProvider.RegisterLicense(SQL_Shortcuts_SSMS21Plus.Properties.Settings.Default.SyncfusionKey);
//            frmShortcuts frmShortcut = new frmShortcuts();
//            frmShortcut.ShowDialog();


//        }
//    }
//}
