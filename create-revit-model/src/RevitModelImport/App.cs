using Autodesk.Revit.UI;

namespace RevitModelImport;

public sealed class App : IExternalApplication
{
    private const string TabName = "Structural Tools";
    private const string PanelName = "Import";
    private const string ButtonName = "Create\nRevit Model";

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
            "CreateRevitModelButton",
            ButtonName,
            assemblyPath,
            typeof(CreateRevitModelCommand).FullName!);

        buttonData.ToolTip = "Create structural framing, columns, and slabs from the repository JSON geometry.";

        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
