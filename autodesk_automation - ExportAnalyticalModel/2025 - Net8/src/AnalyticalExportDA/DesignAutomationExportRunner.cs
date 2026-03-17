using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;

namespace AnalyticalExportDA;

internal static class DesignAutomationExportRunner
{
    private const string OutputFileName = "analytical_export.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static void Run(DesignAutomationData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        Application application = data.RevitApp ?? throw new InvalidOperationException("RevitApp is null.");
        Document document = data.RevitDoc ?? throw new InvalidOperationException("RevitDoc is null.");

        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("A Revit project document is required for analytical export.");
        }

        string workingDirectory = Directory.GetCurrentDirectory();
        string outputPath = Path.Combine(workingDirectory, OutputFileName);

        Console.WriteLine($"DA: Working directory: {workingDirectory}");
        Console.WriteLine($"DA: Revit version: {application.VersionNumber}");
        Console.WriteLine($"DA: Export output: {outputPath}");

        AnalyticalExportResult export = AnalyticalModelExporter.Export(document);

        File.WriteAllText(outputPath, JsonSerializer.Serialize(export.Model, JsonOptions));

        Console.WriteLine($"DA: Export completed. Nodes={export.Model.Nodes.Count}, Lines={export.Model.Lines.Count}, Areas={export.Model.Areas.Count}");

        if (export.Warnings.Count == 0)
        {
            Console.WriteLine("DA: Export warnings: 0");
            return;
        }

        Console.WriteLine($"DA: Export warnings: {export.Warnings.Count}");
        foreach (string warning in export.Warnings)
        {
            Console.WriteLine($"DA WARNING: {warning}");
        }
    }
}
