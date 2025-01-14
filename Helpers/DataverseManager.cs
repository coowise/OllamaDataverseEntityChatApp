using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Text;

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
            query.ColumnSet.AddColumns("flowrunid",
                "name",
                "createdon",
                "status",
                "starttime",
                "endtime",
                "duration",
                "triggertype",
                "error",
                "inputs",
                "outputs",
                "correlationid",
                "organization",
                "solution"
            );

            var workflowLink = query.AddLink("workflow", "workflow", "workflowid", JoinOperator.LeftOuter);
            workflowLink.EntityAlias = "wf";
            workflowLink.Columns.AddColumns("name", "primaryentity", "description", "statecode", "category", "clientdata");

            query.AddOrder("createdon", OrderType.Descending);
            return query;
        }

        public static string FormatFlowRunRecord(Entity record)
        {
            var sb = new StringBuilder();

            var flowName = record.Contains("wf.name")
                ? record.GetAttributeValue<AliasedValue>("wf.name").Value.ToString()
                : "Unnamed Flow";
            var flowId = record.Id.ToString();
            var status = record.Contains("status") ? record["status"].ToString() : "Unknown";
            var startTime = record.Contains("starttime")
                ? DateTime.Parse(record["starttime"].ToString()).ToString("yyyy-MM-dd HH:mm:ss")
                : "Unknown";
            var endTime = record.Contains("endtime")
                ? DateTime.Parse(record["endtime"].ToString()).ToString("yyyy-MM-dd HH:mm:ss")
                : "Unknown";
            var duration = record.Contains("duration") ? record["duration"].ToString() : "Unknown";
            var triggerType = record.Contains("triggertype") ? record["triggertype"].ToString() : "Unknown";
            var error = record.Contains("error") ? record["error"].ToString() : "";

            // Additional Context
            var primaryEntity = record.Contains("wf.primaryentity")
                ? record.GetAttributeValue<AliasedValue>("wf.primaryentity").Value.ToString()
                : "Unknown";
            var flowDescription = record.Contains("wf.description")
                ? record.GetAttributeValue<AliasedValue>("wf.description").Value.ToString()
                : "";
            var flowState = record.Contains("wf.statecode")
                ? record.GetAttributeValue<AliasedValue>("wf.statecode").Value.ToString()
                : "Unknown";
            var clientData = record.Contains("wf.clientdata")
                ? record.GetAttributeValue<AliasedValue>("wf.clientdata").Value.ToString()
                : "";

            // ... existing error information ...

            // Build the formatted summary
            sb.AppendLine($"Flow: {flowName} (ID: {flowId})");
            sb.AppendLine($"Status: {status} | Flow State: {flowState}");
            sb.AppendLine($"Execution: {startTime} to {endTime} (Duration: {duration})");
            sb.AppendLine($"Trigger Type: {triggerType} | Primary Entity: {primaryEntity}");

            if (!string.IsNullOrEmpty(flowDescription))
            {
                sb.AppendLine($"Description: {flowDescription}");
            }

            if (!string.IsNullOrEmpty(clientData))
            {
                sb.AppendLine($"Client Data: {clientData}");
            }

            if (!string.IsNullOrEmpty(error))
            {
                sb.AppendLine($"Error: {error}");
            }

            return sb.ToString().TrimEnd();
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
