using Accounting.Application;
using Accounting.Domain;

namespace Accounting.Api.GraphQL;

// Explicit schema types: new read-model properties never appear in the schema by accident.

public sealed class EstimateExportType : ObjectType<EstimateExportReadModel>
{
    protected override void Configure(IObjectTypeDescriptor<EstimateExportReadModel> descriptor)
    {
        descriptor.Name("EstimateExport");
        descriptor.Description("The record of exporting one estimate to one accounting provider.");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(export => export.Id);
        descriptor.Field(export => export.EstimateId).Description("The estimate's ID in the Estimates service.");
        descriptor.Field(export => export.Provider);
        descriptor.Field(export => export.Status);
        descriptor.Field(export => export.ExternalReference)
            .Description("The invoice reference in the accounting system. Set when status is SUCCEEDED.");
        descriptor.Field(export => export.CurrencyCode);
        descriptor.Field(export => export.ExportedTotal).Description("The estimate total that was sent.");
        descriptor.Field(export => export.AttemptCount).Description("How many times the export has been attempted.");
        descriptor.Field(export => export.LastErrorCode)
            .Description("Why the latest attempt failed, for example ACCOUNTING_PROVIDER_REJECTED. Null unless status is FAILED.");
        descriptor.Field(export => export.LastErrorMessage);
        descriptor.Field(export => export.CreatedAt);
        descriptor.Field(export => export.UpdatedAt);
        descriptor.Field(export => export.CompletedAt).Description("When the export succeeded.");
    }
}

public sealed class AccountingProviderType : EnumType<AccountingProvider>
{
    protected override void Configure(IEnumTypeDescriptor<AccountingProvider> descriptor)
    {
        descriptor.Name("AccountingProvider");
        descriptor.Description("Accounting systems. More values (for example XERO, QUICKBOOKS) will be added: handle unknown values.");
        descriptor.Value(AccountingProvider.Mock).Name("MOCK").Description("Demonstration provider. Does not contact a real accounting system.");
    }
}

public sealed class ExportStatusType : EnumType<ExportStatus>
{
    protected override void Configure(IEnumTypeDescriptor<ExportStatus> descriptor)
    {
        descriptor.Name("ExportStatus");
        descriptor.Value(ExportStatus.Pending).Description("Recorded; the provider call is in progress.");
        descriptor.Value(ExportStatus.Succeeded).Description("Accepted by the provider. Final.");
        descriptor.Value(ExportStatus.Failed).Description("The provider call failed. Call exportEstimate again to retry.");
    }
}
