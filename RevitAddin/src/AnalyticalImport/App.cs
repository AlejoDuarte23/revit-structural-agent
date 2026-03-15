using Autodesk.Revit.UI;

namespace AnalyticalImport;

public sealed class App : IExternalApplication
{
    private const string TabName = "Analytical Tools";
    private const string PanelName = "Import";
    private const string ButtonName = "Import\nAnalytical JSON";

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
            "AnalyticalImportButton",
            ButtonName,
            assemblyPath,
            typeof(ImportAnalyticalModelCommand).FullName!);

        buttonData.ToolTip = "Import analytical members and panels from a JSON file exported by the Python geometry model.";

        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
