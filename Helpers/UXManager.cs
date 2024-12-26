namespace OllamaDataverseEntityChatApp.Helpers
{
    public static class UXManager
    {
        public static void DisplayProgressBar()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.Write("Progress: [");
            for (int i = 0; i < 50; i++)
            {
                Console.Write("█");
                Thread.Sleep(100);
            }
            Console.WriteLine("] Done!");
            Console.ResetColor();
        }

        public static void ShowSuccess(string message = "✓")
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public static void ShowError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ {message}");
            Console.ResetColor();
        }

        public static void UpdateEmbeddingProgress(int position, int width, int processedChunks, int totalChunks)
        {
            var percentage = ((processedChunks + 1) * 100) / totalChunks;
            var completed = (int)((width * percentage) / 100);

            Console.SetCursorPosition(position, Console.CursorTop);
            Console.Write(new string('█', completed));
            Console.Write(new string('░', width - completed));
            Console.Write($"] {percentage}%");
        }

        public static void ShowHeader(string title)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔═════════════════════════════════════════╗");
            Console.WriteLine($"║{title.PadLeft((40 + title.Length) / 2).PadRight(40)} ║");
            Console.WriteLine("╚═════════════════════════════════════════╝");
            Console.ResetColor();
        }

        public static string ShowMenu(string prompt, string[] options)
        {
            int selectedIndex = 0;
            ConsoleKey keyPressed;

            do
            {
                Console.Clear();
                ShowHeader(" CW Ollama Chat with Dataverse v0.1");
                Console.WriteLine($"\n{prompt}");

                for (int i = 0; i < options.Length; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($" > {options[i]}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine($"   {options[i]}");
                    }
                    Console.ResetColor();
                }

                keyPressed = Console.ReadKey(true).Key;

                if (keyPressed == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + options.Length) % options.Length;
                }
                else if (keyPressed == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % options.Length;
                }

            } while (keyPressed != ConsoleKey.Enter);

            Console.WriteLine();
            return (selectedIndex + 1).ToString();
        }

        public static string GetEntitySelection()
        {
            string[] menuOptions = new[]
            {
                "Plugin Trace Logs",
                "Flow Run",
                "Other"
            };

            string choice = ShowMenu("Select an entity to analyze:", menuOptions);

            string entity = choice switch
            {
                "1" => "plugintracelog",
                "2" => "flowrun",
                "3" => GetCustomEntityName(),
                _ => null
            };

            if (entity == null)
            {
                Console.WriteLine("Invalid choice. Please try again.");
                return null;
            }

            return entity;
        }

        private static string GetCustomEntityName()
        {
            Console.WriteLine("\nEnter the Entity Schema Name:");
            string customEntity = Console.ReadLine()?.Trim().ToLower();

            if (string.IsNullOrWhiteSpace(customEntity))
            {
                Console.WriteLine("Entity name cannot be empty.");
                return null;
            }

            return customEntity;
        }
    }
}
