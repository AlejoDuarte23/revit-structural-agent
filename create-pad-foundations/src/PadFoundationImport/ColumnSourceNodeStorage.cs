using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace PadFoundationImport;

internal static class ColumnSourceNodeStorage
{
    private static readonly Guid SchemaGuid = new("A6F1F430-BA5F-48F7-9E3A-96958A0D6E45");
    private const string SchemaName = "RevitStructuralAgentColumnSourceNodes";
    private const string VendorId = "RSAG";
    private const string BaseNodeIdFieldName = "BaseNodeId";

    public static bool TryGetBaseNodeId(Element column, out int baseNodeId)
    {
        baseNodeId = default;

        Schema? schema = Schema.Lookup(SchemaGuid);
        if (schema is null)
        {
            return false;
        }

        Field? field = schema.GetField(BaseNodeIdFieldName);
        if (field is null)
        {
            return false;
        }

        Entity entity = column.GetEntity(schema);

        try
        {
            baseNodeId = entity.Get<int>(field);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Schema GetOrCreateSchema()
    {
        Schema? existing = Schema.Lookup(SchemaGuid);
        if (existing is not null)
        {
            return existing;
        }

        SchemaBuilder builder = new(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.SetVendorId(VendorId);
        builder.AddSimpleField(BaseNodeIdFieldName, typeof(int));
        builder.AddSimpleField("TopNodeId", typeof(int));
        builder.AddSimpleField("BaseX", typeof(double));
        builder.AddSimpleField("BaseY", typeof(double));
        builder.AddSimpleField("BaseZ", typeof(double));
        builder.AddSimpleField("TopX", typeof(double));
        builder.AddSimpleField("TopY", typeof(double));
        builder.AddSimpleField("TopZ", typeof(double));
        return builder.Finish();
    }
}
