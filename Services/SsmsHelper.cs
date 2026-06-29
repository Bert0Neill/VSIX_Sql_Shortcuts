using System;
using System.Diagnostics;
using System.IO;
using Process = System.Diagnostics.Process;

namespace VSIX_SqlShortcuts.Classes
{
    internal class SsmsHelper
    {
        public SsmsHelper() { }

        public string RetrieveSsmsVersion()
        {
            string ssmsVersion = "Unknown";

            try
            {
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
               
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                var fileVersion = versionInfo.FileVersion;  // "15.0.18338.0" etc.
                var majorVersion = versionInfo.ProductMajorPart;

                switch (majorVersion)
                {
                    case 15:
                        ssmsVersion = "SSMS 18";
                        break;
                    case 19:
                        ssmsVersion = "SSMS 19";
                        break;
                    case 20:
                        ssmsVersion = "SSMS 20";
                        break;
                    case 21:
                        ssmsVersion = "SSMS 21";
                        break;
                    case 22:
                        ssmsVersion = "SSMS 22";
                        break;
                    default:
                        ssmsVersion = "Unknown";
                        break;
                }

                return ssmsVersion;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"{ex.Message}");
                return ssmsVersion;
            }
            finally
            {
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
            }
        }

        public string RetrieveSsmsVersion_Extended()
        {
            string ssmsVersion = "Unknown";

            try
            {
                var ssmsInterfacesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SqlWorkbench.Interfaces.dll");

                if (File.Exists(ssmsInterfacesPath))
                {
                    var ssmsInterfacesVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(ssmsInterfacesPath);

                    switch (ssmsInterfacesVersion.FileMajorPart)
                    {
                        case 21:
                            ssmsVersion = "SSMS 21";
                            break;
                        case 20:
                            ssmsVersion = "SSMS 20";
                            break;
                        case 19:
                            ssmsVersion = "SSMS 19";
                            break;
                        case 15:
                            ssmsVersion = "SSMS 18";
                            break;
                    }
                }

                return ssmsVersion;
            }
            catch (Exception)
            {
                return ssmsVersion;
            }
        }
    }
}
