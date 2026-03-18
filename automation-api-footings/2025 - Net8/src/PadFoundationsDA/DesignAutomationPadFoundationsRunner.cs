using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;

namespace PadFoundationsDA;

internal static class DesignAutomationPadFoundationsRunner
{
    private const string InputPayloadFileName = "pad_foundations.json";
    private const string OutputModelFileName = "result.rvt";

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
            throw new InvalidOperationException("A Revit project document is required for pad foundation creation.");
        }

        string workingDirectory = Directory.GetCurrentDirectory();
        string inputPath = Path.Combine(workingDirectory, InputPayloadFileName);
        string outputPath = Path.Combine(workingDirectory, OutputModelFileName);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input payload not found: {inputPath}");
        }

        Console.WriteLine($"DA: Working directory: {workingDirectory}");
        Console.WriteLine($"DA: Revit version: {application.VersionNumber}");
        Console.WriteLine($"DA: Payload path: {inputPath}");
        Console.WriteLine($"DA: Output model: {outputPath}");

        IReadOnlyList<Models.PadFoundationRequest> requests = FoundationPlacementService.ReadRequests(inputPath);
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("The input payload does not contain any footing requests.");
        }

        FoundationPlacementService.BuildSummary summary;
        using (Transaction transaction = new(document, "Create Pad Foundations"))
        {
            transaction.Start();
            summary = FoundationPlacementService.Create(document, requests);
            transaction.Commit();
        }

        SaveAsOptions saveOptions = new()
        {
            OverwriteExistingFile = true,
        };

        document.SaveAs(outputPath, saveOptions);

        Console.WriteLine($"DA: Footings created: {summary.FootingsCreated}");
        Console.WriteLine($"DA: Types created: {summary.TypesCreated}");
        Console.WriteLine($"DA: Matched columns: {summary.MatchedByCoordinates}");
        Console.WriteLine($"DA: Missing columns: {summary.MissingColumns}");
        Console.WriteLine($"DA: Duplicates skipped: {summary.DuplicatesSkipped}");
        Console.WriteLine($"DA: Errors: {summary.Errors}");

        foreach (string warning in summary.ErrorMessages)
        {
            Console.WriteLine($"DA WARNING: {warning}");
        }
    }
}
