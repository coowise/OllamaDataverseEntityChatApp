using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace OllamaDataverseEntityChatApp.Helpers
{
    public static class DataverseManager
    {
        public static async Task<EntityMetadata> GetEntityMetadata(ServiceClient serviceClient, string entityLogicalName)
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Attributes,
                LogicalName = entityLogicalName
            };

            var response = (RetrieveEntityResponse)await serviceClient.ExecuteAsync(request);
            return response.EntityMetadata;
        }

        public static QueryExpression BuildQueryFromMetadata(EntityMetadata metadata, string entity)
        {
            if (entity == "flowrun")
            {
                var query = new QueryExpression(entity);
                query.ColumnSet.AddColumns("flowrunid", "name", "createdon", "status", "starttime", "endtime", "duration", "triggertype");

                var workflowLink = query.AddLink("workflow", "workflow", "workflowid", JoinOperator.LeftOuter);
                workflowLink.EntityAlias = "wf";
                workflowLink.Columns.AddColumns("name", "primaryentity", "description");

                query.AddOrder("createdon", OrderType.Descending);
                return query;
            }
            else if (entity == "plugintracelog")
            {
                var query = new QueryExpression(entity);
                query.ColumnSet.AddColumns("createdon", "messageblock", "exceptiondetails");
                query.Criteria.AddCondition("exceptiondetails", ConditionOperator.NotNull);
                query.AddOrder("createdon", OrderType.Descending);
                return query;
            }

            var defaultQuery = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(metadata.Attributes
                    .Where(a => a.IsValidForRead.GetValueOrDefault()
                        && !a.LogicalName.Contains("_") // Filter out related attributes
                        && !a.LogicalName.EndsWith("name")) // Filter out name fields of lookups
                    .Select(a => a.LogicalName)
                    .ToArray())
            };
            defaultQuery.AddOrder("createdon", OrderType.Descending);
            return defaultQuery;
        }
    }
}
