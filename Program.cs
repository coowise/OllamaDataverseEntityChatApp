using Microsoft.PowerPlatform.Dataverse.Client;
using OllamaDataverseEntityChatApp.Helpers;
using System.Configuration;

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
                }
                catch (Exception ex)
                {
                    UXManager.ShowError(ex.Message);
                }
            }
        }
    }
}