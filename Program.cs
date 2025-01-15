using Microsoft.Extensions.AI;
using Microsoft.PowerPlatform.Dataverse.Client;
using Milvus.Client;
using OllamaDataverseEntityChatApp.Helpers;
using OllamaSharp;
using System.Configuration;

namespace OllamaDataverseEntityChatApp
{
    class Program
    {
        private static readonly string MILVUS_HOST = ConfigurationManager.AppSettings["MilvusHost"];
        private static readonly int MILVUS_PORT = Convert.ToInt32(ConfigurationManager.AppSettings["MilvusPort"]);
        private static readonly string MILVUS_COLLECTION = ConfigurationManager.AppSettings["MilvusCollection"];
        private const int MAX_RETRIES = 5;
        private const int RETRY_DELAY_MS = 10000; // Increased to 10 seconds

        // Ensure Ollama is available before executing this program.
        // Specify the URL where Ollama is running in the App.config,
        // regardless of whether it is local (requires 'ollama serve') or hosted on a server 
        // (e.g., within a container). The URL is used by the OllamaSharp package to connect.

        static async Task Main(string[] args)
        {
            UXManager.ShowHeader(" CW Ollama Chat with Dataverse v0.1");

            // Read configuration
            var connectionString = ConfigurationManager.AppSettings["DataverseConnectionString"];
            var aiGenerationModel = ConfigurationManager.AppSettings["AIGenerationModel"];
            var aiEmbeddingModel = ConfigurationManager.AppSettings["AIEmbeddingModel"];
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
                    var maxRecords = Convert.ToInt32(ConfigurationManager.AppSettings["MaxRecords"]);
                    var query = DataverseManager.BuildQueryFromMetadata(metadata, entity, maxRecords);

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

                    OllamaApiClient chatOllama = null;
                    OllamaApiClient embedOllama = null;

                    try
                    {
                        (chatOllama, embedOllama) = await OllamaManager.InitializeClientsAsync(aiGenerationModel, aiEmbeddingModel);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine(ex.Message);
                        return;
                    }

                    var chat = new Chat(chatOllama);

                    var allRecordSummaries = results.Entities
                        .Take(Convert.ToInt32(ConfigurationManager.AppSettings["MaxRecords"]))
                        .AsParallel()
                        .Select((record, index) =>
                        {
                            var summary = DataverseManager.FormatRecordSummary(record, entity, index);
                            Console.WriteLine($"Formatting record {index + 1}: {summary.Length} characters");
                            return summary;
                        })
                        .ToList();

                    Console.WriteLine($"Total summaries generated: {allRecordSummaries.Count}");

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
                    var vectorDbMode = ConfigurationManager.AppSettings["VectorDB"] ?? "Milvus";
                    Dictionary<int, (List<string> text, float[] embedding)> chunkEmbeddings = new();

                    MilvusClient milvusClient = null;
                    MilvusCollection milvusCollection = null;

                    if (vectorDbMode == "Milvus")
                    {
                        Console.Write("Connecting to Milvus... ");
                        try
                        {
                            (milvusClient, milvusCollection) = await MilvusManager.ConnectAsync(MILVUS_HOST, MILVUS_PORT, entity);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("❌");
                            Console.WriteLine($"Failed to connect to Milvus: {ex.Message}");
                            Console.WriteLine("Please ensure Milvus is running using docker-compose up -d");
                            Console.ResetColor();
                            return;
                        }
                    }

                    var isMultiThread = Convert.ToBoolean(ConfigurationManager.AppSettings["MultiThreadEmbedding"]);

                    if (isMultiThread)
                    {
                        var tasks = new List<Task<(int index, float[] embedding)>>();
                        var recordsToProcess = new List<(int index, Microsoft.Xrm.Sdk.Entity record)>();

                        // First, check which records need processing
                        Console.WriteLine("\nChecking existing embeddings in Milvus...");
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            var record = results.Entities[i];
                            var recordId = record.Id.ToString();

                            var exists = await MilvusManager.DoesEntityExist(milvusCollection, entity, recordId);
                            Console.WriteLine($"Record {recordId} exists: {exists}");

                            if (!exists)
                            {
                                recordsToProcess.Add((i, record));
                            }
                        }

                        if (recordsToProcess.Count == 0)
                        {
                            Console.WriteLine("All records already have embeddings in Milvus. Skipping embedding generation.");
                        }
                        else
                        {
                            Console.WriteLine($"Generating embeddings for {recordsToProcess.Count} new records...");

                            // Create tasks only for records that need processing
                            foreach (var (index, record) in recordsToProcess)
                            {
                                if (index >= chunks.Count) continue;

                                var chunk = chunks[index];
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

                            var embeddingResults = await Task.WhenAll(tasks);
                            foreach (var (index, embedding) in embeddingResults)
                            {
                                chunkEmbeddings.Add(index, (chunks[index], embedding));
                            }
                            Console.WriteLine("\rAll embeddings generated successfully!");
                        }

                        Console.WriteLine("\rInserting new embeddings into Milvus... ");
                        for (int retry = 0; retry < MAX_RETRIES; retry++)
                        {
                            try
                            {
                                if (milvusCollection == null)
                                    milvusCollection = milvusClient.GetCollection(MILVUS_COLLECTION);

                                // Process each record in chunkEmbeddings
                                foreach (var kvp in chunkEmbeddings)
                                {
                                    var index = kvp.Key;
                                    var record = results.Entities[index];
                                    var recordId = record.Id.ToString();

                                    await MilvusManager.InsertEmbeddingsIfNotExistsAsync(
                                        milvusCollection,
                                        entity, // schema name
                                        recordId,
                                        new Dictionary<int, (List<string>, float[])>
                                        {
                                            { index, kvp.Value }
                                        }
                                    );
                                }

                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("\rCompleted inserting all records ✓");
                                Console.ResetColor();
                                break;
                            }
                            catch (Exception ex)
                            {
                                if (retry == MAX_RETRIES - 1)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"\nFailed to insert embeddings after {MAX_RETRIES} attempts: {ex.Message}");
                                    Console.ResetColor();
                                    throw;
                                }

                                var delay = RETRY_DELAY_MS * (retry + 1); // Exponential backoff
                                Console.Write($"\rRate limit hit. Retrying in {delay / 1000.0:F1}s... ");
                                await Task.Delay(delay);
                            }
                        }
                    }

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
                        List<(List<string> text, double score)> similarChunks;

                        var questionEmbedding = await embedOllama.GenerateEmbeddingAsync(message);

                        similarChunks = await MilvusManager.SearchSimilarAsync(
                            milvusCollection,
                            questionEmbedding.Vector.ToArray(),
                            similarityMethod,
                            limit: 3
                        );

                        if (!similarChunks.Any())
                        {
                            Console.WriteLine("No relevant chunks found to answer the question.");
                            continue;
                        }

                        // Format context more clearly
                        var context = "Here are the most relevant records:\n\n" +
                            string.Join("\n---\n", similarChunks.Select(c => string.Join("\n", c.text)));

                        // Add more structure to the prompt
                        await foreach (var answerToken in chat.SendAsync(
                            $"Based on the following Dataverse records:\n\n{context}\n\n" +
                            $"Please answer this question: {message}\n\n" +
                            "Use only the information provided in the context above to answer the question."))
                        {
                            Console.Write(answerToken);
                        }
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