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
                        .Select((record, index) =>
                        {
                            if (entity == "flowrun")
                            {
                                var flowName = record.Contains("wf.name")
                                    ? record.GetAttributeValue<AliasedValue>("wf.name").Value.ToString()
                                    : "Unnamed Flow";
                                var status = record.Contains("status") ? record["status"].ToString() : "Unknown";
                                var startTime = record.Contains("starttime") ? record["starttime"].ToString() : "Unknown";
                                var duration = record.Contains("duration") ? record["duration"].ToString() : "Unknown";

                                return $"Flow '{flowName}' execution:" +
                                    string.Join(", ", record.Attributes
                                        .Where(attr => !attr.Key.StartsWith("wf."))
                                        .Select(attr => $"{attr.Key}: {attr.Value}"));
                            }
                            else if (entity == "plugintracelog")
                            {
                                var messageBlock = record.Contains("messageblock") ? record["messageblock"].ToString() : "No message";
                                var exceptionDetails = record.Contains("exceptiondetails") ? record["exceptiondetails"].ToString() : "No exception";

                                return $"Plugin Trace Log:" +
                                    $" Message: {messageBlock}, Exception: {exceptionDetails}, " +
                                    string.Join(", ", record.Attributes
                                        .Select(attr => $"{attr.Key}: {attr.Value}"));
                            }

                            return $"Record {index + 1}:" +
                                string.Join(", ", record.Attributes
                                    .Select(attr => $"{attr.Key}: {attr.Value}"));
                        })
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
                        Console.Write("[");
                        chunkEmbeddings = new Dictionary<int, (List<string> text, float[] embedding)>();
                        foreach (var chunk in chunks)
                        {
                            // Combine chunk into single text
                            var chunkText = string.Join("\n", chunk);

                            // Get embedding
                            var embeddingResult = await embedOllama.GenerateEmbeddingAsync(chunkText);
                            var embedding = embeddingResult.Vector.ToArray();

                            chunkEmbeddings.Add(processedChunks, (chunk, embedding));

                            // Update progress bar
                            UXManager.UpdateEmbeddingProgress(position, width, processedChunks, chunks.Count);

                            processedChunks++;
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

                        // Get embedding for the question
                        var questionEmbedding = await embedOllama.GenerateEmbeddingAsync(message);

                        // Find most relevant chunks (using cosine similarity)
                        var relevantChunks = OllamaManager.FindMostRelevantChunks(questionEmbedding.Vector.ToArray(), chunkEmbeddings, topK: 3);

                        // Send question with relevant context
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