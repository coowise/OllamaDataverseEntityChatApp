using Microsoft.PowerPlatform.Dataverse.Client;
using OllamaDataverseEntityChatApp.Helpers;
using OllamaSharp;
using System.Configuration;

namespace OllamaDataverseEntityChatApp
{
    class Program
    {
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
                }
                catch (Exception ex)
                {
                    UXManager.ShowError(ex.Message);
                }
            }
        }
    }
}