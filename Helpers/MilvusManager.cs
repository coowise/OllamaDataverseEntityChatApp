using Milvus.Client;
using System.Configuration;

namespace OllamaDataverseEntityChatApp.Helpers
{
    public static class MilvusManager
    {
        private const string MILVUS_DATABASE = "cw_ollama_dataverse";
        private const string MILVUS_COLLECTION_PREFIX = "cw_dataverse_";
        private const int MAX_RETRIES = 5;
        private const int RETRY_DELAY_MS = 10000;
        private const int BATCH_SIZE = 5;

        public static async Task<(MilvusClient client, MilvusCollection collection)> ConnectAsync(string host, int port, string entitySchema)
        {
            for (int retry = 0; retry < MAX_RETRIES; retry++)
            {
                try
                {
                    Console.WriteLine($"\nAttempting to connect to {host}:{port} (Attempt {retry + 1}/{MAX_RETRIES})");

                    var client = new MilvusClient(host, port, false, MILVUS_DATABASE);
                    var version = await client.GetVersionAsync();
                    Console.Write($"Connected successfully! Milvus version: {version}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(" ✓");
                    Console.ResetColor();

                    var collection = await EnsureCollectionExists(client, entitySchema);

                    return (client, collection);
                }
                catch (Exception ex)
                {
                    if (retry == MAX_RETRIES - 1)
                        throw new Exception($"Failed to connect to Milvus after {MAX_RETRIES} attempts: {ex.Message}");

                    await Task.Delay(RETRY_DELAY_MS);
                }
            }

            throw new Exception("Failed to connect to Milvus");
        }

        private static async Task<MilvusCollection> EnsureCollectionExists(MilvusClient client, string entitySchema)
        {
            var collectionName = $"{MILVUS_COLLECTION_PREFIX}{entitySchema.ToLower()}";

            var fields = new[] {
                FieldSchema.CreateVarchar("id", 100, isPrimaryKey: true),
                FieldSchema.CreateVarchar("entity_id", 100),
                FieldSchema.CreateVarchar("content", 65535),
                FieldSchema.CreateFloatVector("embedding", int.Parse(ConfigurationManager.AppSettings["VectorDimensions"]))
            };

            var databases = await client.ListDatabasesAsync();
            if (!databases.Contains(MILVUS_DATABASE))
                await client.CreateDatabaseAsync(MILVUS_DATABASE);

            var hasCollection = await client.HasCollectionAsync(collectionName);
            if (!hasCollection)
            {
                var collection = await client.CreateCollectionAsync(collectionName, fields);
                Console.Write($"\nCollection {collectionName} created successfully...");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" ✓");
                Console.ResetColor();

                await collection.CreateIndexAsync(
                    fieldName: "embedding",
                    indexType: IndexType.IvfFlat,
                    metricType: SimilarityMetricType.L2,
                    extraParams: new Dictionary<string, string> { { "nlist", "128" } }
                );
                Console.Write("\nIndex created successfully...");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" ✓");
                Console.ResetColor();

                return collection;
            }

            return client.GetCollection(collectionName);
        }

        public static async Task InsertEmbeddingsAsync(
            MilvusCollection collection,
            string recordId,
            Dictionary<int, (List<string> text, float[] embedding)> embeddings)
        {
            string entityId = $"{recordId}";
            await collection.LoadAsync();
            await Task.Delay(2000); // Ensure collection is loaded

            for (int i = 0; i < embeddings.Count; i += BATCH_SIZE)
            {
                var batchIds = embeddings
                    .Skip(i)
                    .Take(BATCH_SIZE)
                    .Select(kvp => $"{entityId}_{kvp.Key}")
                    .ToArray();

                var batchContents = embeddings
                    .Skip(i)
                    .Take(BATCH_SIZE)
                    .Select(kvp => string.Join("\n", kvp.Value.text))
                    .ToArray();

                var batchEmbeddings = embeddings
                    .Skip(i)
                    .Take(BATCH_SIZE)
                    .Select(kvp => new ReadOnlyMemory<float>(kvp.Value.embedding))
                    .ToList();

                var fields = new List<FieldData>
                {
                    FieldData.CreateVarChar("id", batchIds),
                    FieldData.CreateVarChar("entity_id", Enumerable.Repeat(entityId, batchIds.Length).ToArray()),
                    FieldData.CreateVarChar("content", batchContents),
                    FieldData.CreateFloatVector("embedding", batchEmbeddings)
                };

                await collection.InsertAsync(fields);
                await collection.FlushAsync();
                await Task.Delay(2000);
            }
        }

        public static async Task<List<(List<string> content, double score)>> SearchSimilarAsync(
            MilvusCollection collection,
            float[] queryEmbedding, string rankingMetricType, int limit = 3)
        {
            await collection.LoadAsync();

            SimilarityMetricType similarityMetric;

            switch (rankingMetricType.ToUpper())
            {
                case "L2":
                    similarityMetric = SimilarityMetricType.L2;
                    break;
                case "COSINE":
                    similarityMetric = SimilarityMetricType.Ip;
                    break;
                default:
                    similarityMetric = SimilarityMetricType.L2;
                    break;
            }

            var searchParams = new SearchParameters();
            searchParams.OutputFields.Add("id");
            searchParams.OutputFields.Add("content");
            searchParams.ExtraParameters.Add("nprobe", "16");

            var searchResults = await collection.SearchAsync(
                "embedding",
                new ReadOnlyMemory<float>[] { queryEmbedding },
                similarityMetric,
                limit,
                searchParams
            );

            var results = new List<(List<string> content, double score)>();

            for (int i = 0; i < searchResults.Scores.Count; i++)
            {
                var score = 1.0 - searchResults.Scores[i];
                var contentField = searchResults.FieldsData
                    .FirstOrDefault(f => f.FieldName == "content") as FieldData<string>;

                if (contentField?.Data != null)
                {
                    results.Add((contentField.Data.ToList(), score));
                }
            }

            return results;
        }

        public static async Task<bool> DoesEntityExist(MilvusCollection collection, string recordId)
        {
            try
            {
                string entityId = $"{recordId}";
                await collection.LoadAsync();

                var expr = $"entity_id == '{entityId}'";
                var queryParams = new QueryParameters();
                queryParams.OutputFields.Add("id");

                var result = await collection.QueryAsync(expr, queryParams);
                return result.Any() && result[0].RowCount > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking entity existence: {ex.Message}");
                return false;
            }
        }

        public static async Task InsertEmbeddingsIfNotExistsAsync(
            MilvusCollection collection,
            string recordId,
            Dictionary<int, (List<string> text, float[] embedding)> embeddings)
        {
            if (await DoesEntityExist(collection, recordId))
            {
                //Console.WriteLine($"Embeddings for entity {recordId} already exist. Skipping insertion.");
                return;
            }

            await InsertEmbeddingsAsync(collection, recordId, embeddings);
        }
    }
}
