namespace OllamaDataverseEntityChatApp.Caching
{
    public class EmbeddingsCache
    {
        public string EntityName { get; set; }
        public Dictionary<int, CacheEntry> Embeddings { get; set; }
    }
}
