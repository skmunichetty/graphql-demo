using Estimates.Application;
using Estimates.Domain;

namespace Estimates.Api.GraphQL.Types;

// Object types are declared explicitly (BindFieldsExplicitly) so that adding a property to a
// read model never silently adds a field to the public schema. Every schema change is a
// deliberate edit in this file, which makes additive evolution and deprecation reviewable.

public sealed class EstimateType : ObjectType<EstimateReadModel>
{
    protected override void Configure(IObjectTypeDescriptor<EstimateReadModel> descriptor)
    {
        descriptor.Name("Estimate");
        descriptor.Description("A priced construction estimate for one customer, in one currency.");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(estimate => estimate.Id).Description("Unique estimate identifier.");
        descriptor.Field(estimate => estimate.Customer).Type<NonNullType<CustomerType>>();
        descriptor.Field(estimate => estimate.CurrencyCode)
            .Description("ISO 4217 currency code. All amounts on the estimate use this currency.");
        descriptor.Field(estimate => estimate.Status);
        descriptor.Field(estimate => estimate.Subtotal).Description("Sum of line subtotals, before tax.");
        descriptor.Field(estimate => estimate.TaxTotal).Description("Sum of rounded line tax amounts.");
        descriptor.Field(estimate => estimate.Total).Description("Subtotal plus tax total.");
        descriptor.Field(estimate => estimate.Version)
            .Description("Optimistic concurrency version. Pass it as `expectedVersion` when changing the estimate.");
        descriptor.Field(estimate => estimate.CreatedAt);
        descriptor.Field(estimate => estimate.UpdatedAt);
        descriptor.Field(estimate => estimate.ApprovedAt).Description("When the estimate was approved; null while it is a draft.");

        descriptor.Field("lineItems")
            .Description("Line items in display order.")
            .Type<NonNullType<ListType<NonNullType<LineItemType>>>>()
            .ResolveWith<EstimateResolvers>(resolvers => resolvers.GetLineItemsAsync(default!, default!, default));
    }
}

public sealed class CustomerType : ObjectType<CustomerReadModel>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerReadModel> descriptor)
    {
        descriptor.Name("Customer");
        descriptor.Description("Customer details captured on the estimate.");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(customer => customer.Name);
        descriptor.Field(customer => customer.Email);
        descriptor.Field(customer => customer.Phone);
    }
}

public sealed class LineItemType : ObjectType<LineItemReadModel>
{
    protected override void Configure(IObjectTypeDescriptor<LineItemReadModel> descriptor)
    {
        descriptor.Name("LineItem");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(lineItem => lineItem.Id);
        descriptor.Field(lineItem => lineItem.Position).Description("Display order, starting at 1.");
        descriptor.Field(lineItem => lineItem.Description);
        descriptor.Field(lineItem => lineItem.Quantity);
        descriptor.Field(lineItem => lineItem.UnitPrice);
        descriptor.Field(lineItem => lineItem.TaxRatePercent).Description("Demo tax rate, for example 10 means 10%.");
        descriptor.Field(lineItem => lineItem.Subtotal).Description("Quantity x unit price, rounded to 2 decimal places.");
        descriptor.Field(lineItem => lineItem.TaxAmount).Description("Subtotal x tax rate, rounded to 2 decimal places.");
        descriptor.Field(lineItem => lineItem.Total);
    }
}

public sealed class EstimateStatusType : EnumType<EstimateStatus>
{
    protected override void Configure(IEnumTypeDescriptor<EstimateStatus> descriptor)
    {
        descriptor.Name("EstimateStatus");
        descriptor.Description("Estimate lifecycle. More values may be added: clients should handle unknown values.");
        descriptor.Value(EstimateStatus.Draft).Description("Editable.");
        descriptor.Value(EstimateStatus.Approved).Description("Locked. Can be exported to accounting.");
    }
}
