namespace OllamaDataverseEntityChatApp.Caching
{
    public class CacheEntry
    {
        public List<string> Text { get; set; }
        public float[] Embedding { get; set; }
    }
}
