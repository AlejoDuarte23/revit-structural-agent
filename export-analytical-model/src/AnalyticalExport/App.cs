using Autodesk.Revit.UI;

namespace AnalyticalExport;

public sealed class App : IExternalApplication
{
    private const string TabName = "Analytical Tools";
    private const string PanelName = "Export";
    private const string ButtonName = "Export\nAnalytical JSON";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // The tab already exists in this Revit session.
        }

        RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
        string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        PushButtonData buttonData = new(
            "AnalyticalExportButton",
            ButtonName,
            assemblyPath,
            typeof(ExportAnalyticalModelCommand).FullName!);

        buttonData.ToolTip = "Export analytical members, panels, and hosted slab loads to a JSON file.";

        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
