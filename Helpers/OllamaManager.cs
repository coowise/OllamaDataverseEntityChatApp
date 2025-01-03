﻿using System.Configuration;

namespace OllamaDataverseEntityChatApp.Helpers
{
    public static class OllamaManager
    {
        private static string BaseUrl => $"https://{ConfigurationManager.AppSettings["OllamaHost"]}";

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

        public static float CosineSimilarity(float[] a, float[] b)
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

        public static List<(List<string> text, double score)> FindMostRelevantChunksBM25(
            string query,
            Dictionary<int, List<string>> chunks,
            int topK)
        {
            var tokenizedQuery = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.ToLowerInvariant())
                                    .ToList();

            var docFreq = CalculateDocumentFrequencies(chunks.Values.ToList());
            var avgDocLength = chunks.Values.Average(doc => doc.Count);

            const float k1 = 1.5f; // BM25 parameter
            const float b = 0.75f; // BM25 parameter

            var scores = chunks.Select(chunk => {
                double score = CalculateBM25Score(
                    tokenizedQuery,
                    chunk.Value,
                    docFreq,
                    avgDocLength,
                    k1,
                    b);
                return (chunk.Value, score);
            });

            return scores
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();
        }

        private static Dictionary<string, int> CalculateDocumentFrequencies(List<List<string>> documents)
        {
            var docFreq = new Dictionary<string, int>();
            foreach (var doc in documents)
            {
                var uniqueTerms = doc.Select(t => t.ToLowerInvariant()).Distinct();
                foreach (var term in uniqueTerms)
                {
                    if (!docFreq.ContainsKey(term))
                        docFreq[term] = 0;
                    docFreq[term]++;
                }
            }
            return docFreq;
        }

        private static double CalculateBM25Score(
            List<string> queryTerms,
            List<string> document,
            Dictionary<string, int> docFreq,
            double avgDocLength,
            float k1,
            float b)
        {
            double score = 0;
            var docLength = document.Count;
            var termFreq = document
                .GroupBy(t => t.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count());

            var totalDocuments = docFreq.Values.Max(); // Get total number of documents from document frequencies

            foreach (var term in queryTerms)
            {
                if (!docFreq.ContainsKey(term))
                    continue;

                var tf = termFreq.GetValueOrDefault(term, 0);
                var df = docFreq[term];
                var idf = Math.Log((totalDocuments - df + 0.5) / (df + 0.5) + 1);

                score += idf * ((tf * (k1 + 1)) /
                    (tf + k1 * (1 - b + b * docLength / avgDocLength)));
            }

            return score;
        }
    }
}
