namespace Estimates.IntegrationTests;

/// <summary>GraphQL documents used by the tests. They match the examples in the client guide.</summary>
internal static class Operations
{
    public const string EstimateFields = """
        fragment EstimateFields on Estimate {
          id
          status
          currencyCode
          subtotal
          taxTotal
          total
          version
          approvedAt
          customer { name email }
          lineItems { id position description quantity unitPrice taxRatePercent subtotal taxAmount total }
        }
        """;

    public const string CreateEstimate = """
        mutation CreateEstimate($input: CreateEstimateInput!) {
          createEstimate(input: $input) {
            estimate { ...EstimateFields }
            errors { __typename ... on UserError { code message } ... on ValidationError { field } }
          }
        }
        """ + EstimateFields;

    public const string AddLineItem = """
        mutation AddLineItem($input: AddLineItemInput!) {
          addLineItem(input: $input) {
            estimate { ...EstimateFields }
            errors { __typename ... on UserError { code message } ... on ValidationError { field } }
          }
        }
        """ + EstimateFields;

    public const string UpdateLineItem = """
        mutation UpdateLineItem($input: UpdateLineItemInput!) {
          updateLineItem(input: $input) {
            estimate { ...EstimateFields }
            errors { __typename ... on UserError { code message } }
          }
        }
        """ + EstimateFields;

    public const string RemoveLineItem = """
        mutation RemoveLineItem($input: RemoveLineItemInput!) {
          removeLineItem(input: $input) {
            estimate { ...EstimateFields }
            errors { __typename ... on UserError { code message } }
          }
        }
        """ + EstimateFields;

    public const string ApproveEstimate = """
        mutation ApproveEstimate($input: ApproveEstimateInput!) {
          approveEstimate(input: $input) {
            estimate { ...EstimateFields }
            errors { __typename ... on UserError { code message } }
          }
        }
        """ + EstimateFields;

    public const string GetEstimate = """
        query GetEstimate($id: UUID!) {
          estimate(id: $id) { ...EstimateFields }
        }
        """ + EstimateFields;

    public const string ListEstimates = """
        query ListEstimates($first: Int, $after: String) {
          estimates(first: $first, after: $after) {
            nodes { id customer { name } total lineItems { description } }
            pageInfo { hasNextPage endCursor }
          }
        }
        """;

    public const string GetEstimateForIntegration = """
        query GetEstimateForIntegration($tenantId: String!, $id: UUID!) {
          estimateForIntegration(tenantId: $tenantId, id: $id) { id status total currencyCode }
        }
        """;
}
