using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Syncfusion.Licensing;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VSIX_SqlShortcuts.Forms;

namespace VSIX_SqlShortcuts.Commands
{
    [Guid("bf54b6c0-6ed7-4107-a84b-c1a031eab60a")]
    internal sealed class cmdShortcuts //: IOleCommandTarget
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

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var cmdId = new CommandID(CommandSet, CommandId);
                var menuCommand = new OleMenuCommand((s, e) => Instance.Execute(s, e), cmdId);
                menuCommand.Enabled = true;
                menuCommand.Visible = true;

                commandService.AddCommand(menuCommand);
            }

            Instance = new cmdShortcuts(package);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            SyncfusionLicenseProvider.RegisterLicense(SQL_Shortcuts.Properties.Settings.Default.SyncfusionKey);
            var frm = new frmShortcuts();
            frm.ShowDialog();   // or frm.Show();
        }
        
        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == CommandSet && nCmdID == CommandId)
            {
                System.Windows.Forms.MessageBox.Show("Exec intercepted!");
                Execute(null, null);
                return VSConstants.S_OK;
            }
            return VSConstants.E_NOTIMPL;
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
            return VSConstants.E_NOTIMPL;   // ← critical
        }
    }
}