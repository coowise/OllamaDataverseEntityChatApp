using Microsoft.Extensions.AI;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using OllamaDataverseEntityChatApp.Caching;
using OllamaDataverseEntityChatApp.Helpers;
using OllamaSharp;
using System.Configuration;
using System.Text.Json;

namespace OllamaDataverseEntityChatApp
{
    class Program
    {
        private const string EMBEDDINGS_CACHE_PATH = "embeddings_cache.json";

        // Ensure Ollama is available before executing this program.
        // Specify the URL where Ollama is running in the App.config,
        // regardless of whether it is local (requires 'ollama serve') or hosted on a server 
        // (e.g., within a container). The URL is used by the OllamaSharp package to connect.

        static async Task Main(string[] args)
        {
            UXManager.ShowHeader(" CW Ollama Chat with Dataverse v0.1");

            // Read configuration
            var connectionString = ConfigurationManager.AppSettings["DataverseConnectionString"];
            var aiModel = ConfigurationManager.AppSettings["AIModel"];
            var systemPrompt = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", "SystemPrompt.txt"));

            // Get entity selection from user
            string entity = UXManager.GetEntitySelection();
            if (entity == null)
            {
                return;
            }

            // Connect to Dataverse
            Console.Write("Connecting to Dataverse... ");

            using (var serviceClient = new ServiceClient(connectionString))
            {
                if (!serviceClient.IsReady)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Failed to connect to Dataverse");
                    Console.ResetColor();
                    return;
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();

                try
                {
                    Console.Write("Retrieving entity metadata... ");
                    var metadata = await DataverseManager.GetEntityMetadata(serviceClient, entity);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓");
                    Console.ResetColor();

                    Console.Write("Building query... ");
                    var query = DataverseManager.BuildQueryFromMetadata(metadata, entity);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓");
                    Console.ResetColor();

                    Console.Write("Executing query... ");
                    var results = serviceClient.RetrieveMultiple(query);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓");
                    Console.ResetColor();

                    // Add check for empty results
                    if (results.Entities.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\nNo records found for the selected entity.");
                        Console.ResetColor();
                        return;
                    }

                    //OllamaSharp
                    Console.WriteLine("");
                    var uri = OllamaManager.GetOllamaUri();
                    var ollama = new OllamaApiClient(uri);

                    var models = await ollama.ListLocalModelsAsync();
                    string chatModel = null;
                    string embedModel = null;

                    foreach (var m in models)
                    {
                        Console.WriteLine($"Model available: {m.Name}");
                        if (m.Name.Contains(aiModel.Trim()))
                        {
                            chatModel = m.Name;
                            embedModel = m.Name;
                        }
                        else if (m.Name.Contains("nomic-embed-text"))
                        {
                            //Smaller embedding model
                            //embedModel = m.Name;
                        }
                    }

                    if (chatModel == null || embedModel == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Missing required models! Please ensure both llama2 and nomic-embed-text are available.");
                        Console.ResetColor();
                        return;
                    }

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Using chat model: {chatModel}");
                    Console.WriteLine($"Using embedding model: {embedModel}");
                    Console.ResetColor();
                    Console.WriteLine("");

                    var httpClient = new HttpClient()
                    {
                        Timeout = TimeSpan.FromMinutes(5),
                        BaseAddress = uri
                    };

                    // Create two separate clients for different purposes
                    var chatOllama = new OllamaApiClient(httpClient) { SelectedModel = chatModel };
                    var embedOllama = new OllamaApiClient(httpClient) { SelectedModel = embedModel };

                    var chat = new OllamaSharp.Chat(chatOllama);

                    // Create a consolidated context of all records
                    var allRecordSummaries = results.Entities
                        .Take(Convert.ToInt32(ConfigurationManager.AppSettings["MaxRecords"]))  // Limit to MaxRecords records
                        .Select((record, index) => DataverseManager.FormatRecordSummary(record, entity, index))
                        .ToList();

                    // Add warning if records were truncated
                    if (results.Entities.Count > Convert.ToInt32(ConfigurationManager.AppSettings["MaxRecords"]))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\nWarning: Results truncated to last {Convert.ToInt32(ConfigurationManager.AppSettings["MaxRecords"])} records");
                        Console.ResetColor();
                    }

                    // Split records into manageable chunks
                    const int CHUNK_SIZE = 1;
                    var totalRecords = results.Entities.Count;
                    var chunks = new List<List<string>>();

                    for (int i = 0; i < allRecordSummaries.Count; i += CHUNK_SIZE)
                    {
                        chunks.Add(allRecordSummaries.Skip(i).Take(CHUNK_SIZE).ToList());
                    }
                    var position = Console.CursorLeft;
                    var width = 50;
                    var processedChunks = 0;

                    // Store embeddings for each chunk
                    var shouldCreateEmbeddings = true;
                    Dictionary<int, (List<string> text, float[] embedding)> chunkEmbeddings = new();

                    if (File.Exists(EMBEDDINGS_CACHE_PATH))
                    {
                        var cache = JsonSerializer.Deserialize<EmbeddingsCache>(File.ReadAllText(EMBEDDINGS_CACHE_PATH));
                        if (cache.EntityName == entity)
                        {
                            Console.WriteLine("\rLoading embeddings from cache...");
                            chunkEmbeddings = cache.Embeddings.ToDictionary(
                                kvp => kvp.Key,
                                kvp => (kvp.Value.Text, kvp.Value.Embedding)
                            );
                            shouldCreateEmbeddings = false;
                        }
                    }

                    if (shouldCreateEmbeddings)
                    {
                        Console.WriteLine("\nCreating embeddings...");
                        chunkEmbeddings = new Dictionary<int, (List<string> text, float[] embedding)>();
                        var isMultiThread = Convert.ToBoolean(ConfigurationManager.AppSettings["MultiThreadEmbedding"]);

                        if (isMultiThread)
                        {
                            var tasks = new List<Task<(int index, float[] embedding)>>();
                            Console.WriteLine("Processing chunks in parallel...");

                            // Create tasks for parallel processing
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                var index = i;
                                var chunk = chunks[i];
                                var chunkText = string.Join("\n", chunk);

                                tasks.Add(Task.Run(async () =>
                                {
                                    var embeddingResult = await embedOllama.GenerateEmbeddingAsync(chunkText);
                                    return (index, embeddingResult.Vector.ToArray());
                                }));
                            }

                            // Show spinning cursor while processing
                            var spinChars = new[] { '|', '/', '-', '\\' };
                            var spinIndex = 0;
                            while (tasks.Any(t => !t.IsCompleted))
                            {
                                Console.Write($"\rProcessing... {spinChars[spinIndex]} ({tasks.Count(t => t.IsCompleted)}/{tasks.Count} chunks complete)");
                                spinIndex = (spinIndex + 1) % spinChars.Length;
                                await Task.Delay(100);
                            }

                            // Store results (renamed from 'results' to 'embeddingResults')
                            var embeddingResults = await Task.WhenAll(tasks);
                            foreach (var (index, embedding) in embeddingResults)
                            {
                                chunkEmbeddings.Add(index, (chunks[index], embedding));
                            }
                            Console.WriteLine("\rAll chunks processed successfully!            ");
                        }
                        else
                        {
                            Console.Write("[");
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                var chunkText = string.Join("\n", chunks[i]);
                                var embeddingResult = await embedOllama.GenerateEmbeddingAsync(chunkText);
                                var embedding = embeddingResult.Vector.ToArray();

                                chunkEmbeddings.Add(i, (chunks[i], embedding));

                                // Update progress bar
                                UXManager.UpdateEmbeddingProgress(position, width, i, chunks.Count);
                            }
                        }

                        // Save embeddings to cache
                        var cache = new EmbeddingsCache
                        {
                            EntityName = entity,
                            Embeddings = chunkEmbeddings.ToDictionary(
                                kvp => kvp.Key,
                                kvp => new CacheEntry { Text = kvp.Value.text, Embedding = kvp.Value.embedding }
                            )
                        };
                        File.WriteAllText(EMBEDDINGS_CACHE_PATH, JsonSerializer.Serialize(cache));
                    }

                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Embeddings created! ✓");
                    Console.ResetColor();

                    // Instead of loading all chunks into context, we'll use embeddings for retrieval
                    await foreach (var token in chat.SendAsAsync(OllamaSharp.Models.Chat.ChatRole.System, systemPrompt))
                    {
                        // Optional: Show progress of system message
                    }

                    // Then in the question loop, use embeddings to find relevant chunks
                    while (true)
                    {
                        Console.Write("\nAsk a question (or 'exit' to quit): ");
                        var message = Console.ReadLine();
                        if (message?.ToLower() == "exit") break;

                        var similarityMethod = ConfigurationManager.AppSettings["SimilarityMethod"];
                        List<(List<string> text, double score)> relevantChunks;

                        if (similarityMethod?.ToLower() == "bm25")
                        {
                            // Convert chunks for BM25 format
                            var textChunks = chunkEmbeddings.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.text
                            );

                            relevantChunks = OllamaManager.FindMostRelevantChunksBM25(
                                message,
                                textChunks,
                                topK: 3
                            );
                        }
                        else // Default to Cosine similarity
                        {
                            var questionEmbedding = await embedOllama.GenerateEmbeddingAsync(message);
                            var cosineChunks = OllamaManager.FindMostRelevantChunks(
                                questionEmbedding.Vector.ToArray(),
                                chunkEmbeddings,
                                topK: 3
                            );

                            // Convert to common format
                            relevantChunks = cosineChunks.Select(c => (c.text, (double)OllamaManager.CosineSimilarity(questionEmbedding.Vector.ToArray(), c.embedding))).ToList();
                        }

                        // Use the relevant chunks (same as before)
                        var context = string.Join("\n", relevantChunks.SelectMany(c => c.text));
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        await foreach (var answerToken in chat.SendAsync(
                            $"Context:\n{context}\n\nQuestion: {message}"))
                        {
                            Console.Write(answerToken);
                        }
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    UXManager.ShowError(ex.Message);
                }
            }
        }
    }
}