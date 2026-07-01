using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using SSMS_SqlAdjust_Interfaces;
using System;
using System.ComponentModel;
using System.Reflection;

namespace SSMS_SqlAdjust
{
    public class SmoObjectExplorer : IObjectExplorerHelper
    {
        private readonly IServiceProvider _package;
        private Delegate _handlerDelegate;

        public string SelectedSmoNode { get; private set; }

        public event EventHandler<string> SelectionChanged;

        public SmoObjectExplorer(IServiceProvider package)
        {
            _package = package;
        }

        public void StartMonitoring()
        {
            HookSelectionChanged(nameof(Provider_SelectionChanged));
        }

        private IObjectExplorerService GetObjectExplorer()
        {
            return _package.GetService(
                typeof(IObjectExplorerService))
                as IObjectExplorerService;
        }

        private void HookSelectionChanged(string methodName)
        {
            var objectExplorer = GetObjectExplorer();

            if (objectExplorer == null)
                return;

            var objectExplorerType =
                objectExplorer.GetType();

            var piContainer =
                objectExplorerType.GetProperty(
                    "Container");

            var container =
                piContainer?.GetValue(objectExplorer);

            if (container == null)
                return;

            var piComponents =
                container.GetType().GetProperty(
                    "Components");

            var components =
                piComponents?.GetValue(container)
                as ComponentCollection;

            if (components == null)
                return;

            object contextService = null;

            foreach (Component component in components)
            {
                if (component.GetType().FullName
                    .Contains("ContextService"))
                {
                    contextService = component;
                    break;
                }
            }

            if (contextService == null)
                return;

            var contextProperty =
                contextService.GetType()
                .GetProperty(
                    "ObjectExplorerContext");

            var context =
                contextProperty?.GetValue(
                    contextService);

            if (context == null)
                return;

            var eventInfo =
                context.GetType()
                .GetEvent(
                    "CurrentContextChanged");

            if (eventInfo == null)
                return;

            var method =
                GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);

            _handlerDelegate =
                Delegate.CreateDelegate(
                    eventInfo.EventHandlerType,
                    this,
                    method);

            eventInfo.AddEventHandler(
                context,
                _handlerDelegate);
        }

        private void Provider_SelectionChanged(object sender, NodesChangedEventArgs args)
        {
            if (args == null ||
                args.ChangedNodes == null ||
                args.ChangedNodes.Count == 0)
                return;

            var node = args.ChangedNodes[0];

            string name =
                GetProperty(node, "InvariantName");

            string urn =
                GetProperty(node, "UrnPath");

            bool isTable =
                urn?.EndsWith(
                    "Table",
                    StringComparison.OrdinalIgnoreCase)
                == true;

            bool isView =
                urn?.EndsWith(
                    "View",
                    StringComparison.OrdinalIgnoreCase)
                == true;

            SelectedSmoNode =
                isTable || isView
                ? name
                : null;

            SelectionChanged?.Invoke(
                this,
                SelectedSmoNode);
        }

        private string GetProperty(INodeInformation node, string propertyName)
        {
            if (node == null)
                return null;

            var pi =
                node.GetType()
                .GetProperty(
                    propertyName);

            return pi?
                .GetValue(node)?
                .ToString();
        }

        public void Dispose()
        {
            var objectExplorer =
                GetObjectExplorer();

            if (objectExplorer == null ||
                _handlerDelegate == null)
                return;

            try
            {
                var container =
                    objectExplorer.GetType()
                    .GetProperty("Container")
                    ?.GetValue(objectExplorer);

                var components =
                    container?.GetType()
                    .GetProperty("Components")
                    ?.GetValue(container)
                    as ComponentCollection;

                object contextService = null;

                foreach (Component component in components)
                {
                    if (component.GetType().FullName
                        .Contains("ContextService"))
                    {
                        contextService = component;
                        break;
                    }
                }

                var context =
                    contextService?.GetType()
                    .GetProperty(
                        "ObjectExplorerContext")
                    ?.GetValue(contextService);

                var evt =
                    context?.GetType()
                    .GetEvent(
                        "CurrentContextChanged");

                evt?.RemoveEventHandler(
                    context,
                    _handlerDelegate);
            }
            catch
            {
            }
        }

    }
}
