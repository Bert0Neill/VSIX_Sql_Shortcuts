using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Diagnostics;

namespace VSIX_Demo.Core
{
    public class SqlEditorHookService
    {
        private SqlTextChangeTracker _tracker;
        private IWpfTextView _currentView;

        public void Hook(IVsTextView nativeView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (nativeView == null)
                return;

            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var factory = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            IWpfTextView view = factory.GetWpfTextView(nativeView);

            if (view == null)
                return;

            // prevent double hook
            if (_currentView == view)
                return;

            _currentView = view;

            // detach old
            _tracker?.Dispose();

            _tracker = new SqlTextChangeTracker(view);

            Debug.WriteLine("✔ SQL TextBuffer tracking enabled");
        }
    }
}