using Autodesk.Revit.UI;

namespace PadFoundationImport;

public sealed class App : IExternalApplication
{
    private const string TabName = "Structural Tools";
    private const string PanelName = "Foundations";
    private const string ButtonName = "Add Pad\nFoundations";

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
            "CreatePadFoundationsButton",
            ButtonName,
            assemblyPath,
            typeof(CreatePadFoundationsCommand).FullName!);

        buttonData.ToolTip = "Create isolated pad foundations under structural columns from JSON.";

        panel.AddItem(buttonData);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
