using System;

namespace SSMS_SqlAdjust_Interfaces
{
    public interface IObjectExplorerHelper : IDisposable
    {
       
        string SelectedSmoNode { get; }

        event EventHandler<string> SelectionChanged;

        void StartMonitoring();

    }
}
