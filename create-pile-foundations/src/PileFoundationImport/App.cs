using Autodesk.Revit.UI;

namespace PileFoundationImport;

public sealed class App : IExternalApplication
{
    private const string TabName = "Structural Tools";
    private const string PanelName = "Foundations";
    private const string ButtonName = "Add Pile\nFoundations";

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

        // Try to get existing panel, or create new one
        RibbonPanel? panel = null;
        foreach (var existingPanel in application.GetRibbonPanels(TabName))
        {
            if (existingPanel.Name == PanelName)
            {
                panel = existingPanel;
                break;
            }
        }

        panel ??= application.CreateRibbonPanel(TabName, PanelName);
        string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        PushButtonData buttonData = new(
            "CreatePileFoundationsButton",
            ButtonName,
            assemblyPath,
            typeof(CreatePileFoundationsCommand).FullName!);

        buttonData.ToolTip = "Create pile foundation families under structural columns from JSON.";

        panel.AddItem(buttonData);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
