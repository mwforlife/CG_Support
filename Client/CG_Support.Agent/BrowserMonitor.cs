using System;
using System.Diagnostics;
using System.Windows.Automation;

namespace CG_Support.Agent
{
    public static class BrowserMonitor
    {
        public static string GetActiveBrowserUrl()
        {
            try
            {
                // Buscar navegadores comunes activos
                Process[] chromeProcesses = Process.GetProcessesByName("chrome");
                Process[] edgeProcesses = Process.GetProcessesByName("msedge");
                Process[] firefoxProcesses = Process.GetProcessesByName("firefox");

                if (chromeProcesses.Length > 0)
                {
                    return GetUrlFromChromium(chromeProcesses[0]);
                }
                else if (edgeProcesses.Length > 0)
                {
                    return GetUrlFromChromium(edgeProcesses[0]);
                }
                else if (firefoxProcesses.Length > 0)
                {
                    return GetUrlFromFirefox(firefoxProcesses[0]);
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetUrlFromChromium(Process process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero) return string.Empty;

                AutomationElement element = AutomationElement.FromHandle(process.MainWindowHandle);
                if (element == null) return string.Empty;

                // Buscar la barra de direcciones editables en navegadores Chromium
                // Tipicamente es un Edit Control con un tipo de control Edit y nombre "Dirección y búsqueda" o AutomationID "address_box"
                Condition editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                AutomationElementCollection editElements = element.FindAll(TreeScope.Descendants, editCondition);

                foreach (AutomationElement edit in editElements)
                {
                    // Obtener patrón de valor
                    if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
                    {
                        ValuePattern valPattern = (ValuePattern)valuePatternObj;
                        string url = valPattern.Current.Value;

                        // Limpiar y formatear URL básica
                        if (!string.IsNullOrEmpty(url))
                        {
                            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                            {
                                url = "https://" + url;
                            }
                            return url;
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetUrlFromFirefox(Process process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero) return string.Empty;

                AutomationElement element = AutomationElement.FromHandle(process.MainWindowHandle);
                if (element == null) return string.Empty;

                // Firefox usa un esquema similar
                Condition editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                AutomationElement edit = element.FindFirst(TreeScope.Descendants, editCondition);
                
                if (edit != null && edit.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
                {
                    ValuePattern valPattern = (ValuePattern)valuePatternObj;
                    return valPattern.Current.Value;
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
