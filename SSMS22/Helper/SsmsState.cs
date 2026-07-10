using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SQL_Shortcuts_SSMS22.Helper
{
    public sealed class SsmsState
    {
        private static readonly Lazy<SsmsState> _instance =
            new Lazy<SsmsState>(() => new SsmsState());

        public static SsmsState Instance => _instance.Value;

        private SsmsState() { }

        public string SelectedTableOrView { get; set; }
    }
}
