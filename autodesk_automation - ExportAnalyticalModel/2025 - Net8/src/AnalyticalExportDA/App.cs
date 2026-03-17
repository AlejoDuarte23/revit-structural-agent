using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;

namespace AnalyticalExportDA;

public sealed class App : IExternalDBApplication
{
    public ExternalDBApplicationResult OnStartup(ControlledApplication application)
    {
        DesignAutomationBridge.DesignAutomationReadyEvent += OnDesignAutomationReady;
        return ExternalDBApplicationResult.Succeeded;
    }

    public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
    {
        DesignAutomationBridge.DesignAutomationReadyEvent -= OnDesignAutomationReady;
        return ExternalDBApplicationResult.Succeeded;
    }

    private static void OnDesignAutomationReady(object sender, DesignAutomationReadyEventArgs eventArgs)
    {
        try
        {
            DesignAutomationExportRunner.Run(eventArgs.DesignAutomationData);
            eventArgs.Succeeded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DA ERROR: {ex}");
            eventArgs.Succeeded = false;
        }
    }
}
