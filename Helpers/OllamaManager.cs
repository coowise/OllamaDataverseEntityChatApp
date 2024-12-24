using System.Configuration;

namespace OllamaDataverseEntityChatApp.Helpers
{
    public static class OllamaManager
    {
        private static string BaseUrl => $"http://{ConfigurationManager.AppSettings["OllamaHost"]}:{ConfigurationManager.AppSettings["OllamaPort"]}";
        public static Uri GetOllamaUri() => new Uri(BaseUrl);

        public static List<(List<string> text, float[] embedding)> FindMostRelevantChunks(
            float[] queryEmbedding,
            Dictionary<int, (List<string> text, float[] embedding)> chunkEmbeddings,
            int topK)
        {
            return chunkEmbeddings
                .Select(kvp => kvp.Value)
                .OrderByDescending(chunk => CosineSimilarity(queryEmbedding, chunk.embedding))
                .Take(topK)
                .ToList();
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dotProduct = 0;
            float normA = 0;
            float normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        }
    }
}
