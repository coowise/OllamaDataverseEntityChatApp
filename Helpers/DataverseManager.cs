using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
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
            switch (entity)
            {
                case "flowrun":
                    return BuildFlowRunQuery();
                case "plugintracelog":
                    return BuildPluginTraceLogQuery();
                default:
                    return BuildDefaultQuery(metadata, entity);
            }
        }

        private static QueryExpression BuildFlowRunQuery()
        {
            var query = new QueryExpression("flowrun");
            query.ColumnSet.AddColumns("flowrunid", "name", "createdon", "status",
                "starttime", "endtime", "duration", "triggertype");

            var workflowLink = query.AddLink("workflow", "workflow", "workflowid", JoinOperator.LeftOuter);
            workflowLink.EntityAlias = "wf";
            workflowLink.Columns.AddColumns("name", "primaryentity", "description", "statecode");

            query.AddOrder("createdon", OrderType.Descending);
            return query;
        }

        public static string FormatFlowRunRecord(Entity record)
        {
            var flowName = record.Contains("wf.name")
                ? record.GetAttributeValue<AliasedValue>("wf.name").Value.ToString()
                : "Unnamed Flow";
            var status = record.Contains("status") ? record["status"].ToString() : "Unknown";
            var startTime = record.Contains("starttime") ? record["starttime"].ToString() : "Unknown";
            var endTime = record.Contains("endtime") ? record["endtime"].ToString() : "Unknown";
            var duration = record.Contains("duration") ? record["duration"].ToString() : "Unknown";
            var triggerType = record.Contains("triggertype") ? record["triggertype"].ToString() : "Unknown";

            return $"Flow '{flowName}' execution:" +
                $" Status: {status}, Start: {startTime}, End: {endTime}" +
                $" Duration: {duration}, Trigger Type: {triggerType}";
        }

        private static QueryExpression BuildPluginTraceLogQuery()
        {
            var query = new QueryExpression("plugintracelog");
            query.ColumnSet.AddColumns("createdon", "messagename", "operationtype",
                "messageblock", "exceptiondetails", "performanceexecutionduration");
            query.Criteria.AddCondition("exceptiondetails", ConditionOperator.NotNull);
            query.AddOrder("createdon", OrderType.Descending);
            return query;
        }

        public static string FormatPluginTraceLogRecord(Entity record)
        {
            var messageBlock = record.Contains("messageblock") ? record["messageblock"].ToString() : "No message";
            var exceptionDetails = record.Contains("exceptiondetails") ? record["exceptiondetails"].ToString() : "No exception";
            var messageName = record.Contains("messagename") ? record["messagename"].ToString() : "Unknown";
            var operationType = record.Contains("operationtype") ? record["operationtype"].ToString() : "Unknown";
            var executionDuration = record.Contains("performanceexecutionduration") ? record["performanceexecutionduration"].ToString() : "Unknown";
            var createdOn = record.Contains("createdon") ? record["createdon"].ToString() : "Unknown";

            return $"Plugin Trace Log:" +
                $" Created: {createdOn}, Message: {messageName}" +
                $" Operation: {operationType}, Duration: {executionDuration}" +
                $" Details: {messageBlock}" +
                $" Exception: {exceptionDetails}";
        }

        public static string FormatDefaultRecord(Entity record, int index)
        {
            return $"Record {index + 1}:" +
                string.Join(", ", record.Attributes
                    .Select(attr => $"{attr.Key}: {attr.Value}"));
        }

        public static string FormatRecordSummary(Entity record, string entity, int index)
        {
            return entity switch
            {
                "flowrun" => FormatFlowRunRecord(record),
                "plugintracelog" => FormatPluginTraceLogRecord(record),
                _ => FormatDefaultRecord(record, index)
            };
        }

        private static QueryExpression BuildDefaultQuery(EntityMetadata metadata, string entity)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(metadata.Attributes
                    .Where(a => a.IsValidForRead.GetValueOrDefault()
                        && !a.LogicalName.Contains("_")
                        && !a.LogicalName.EndsWith("name"))
                    .Select(a => a.LogicalName)
                    .ToArray())
            };
            query.AddOrder("createdon", OrderType.Descending);
            return query;
        }
    }
}
