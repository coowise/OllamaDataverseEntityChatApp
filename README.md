# CW.AI.Dataverse.Entity.Chat

A command-line tool that connects to Microsoft Dataverse, retrieves data, and uses AI (Embedding and ChatCompletion) to allow you to chat with your Dataverse data using semantic search capabilities.

## Features

- 🔌 Seamless connection to Microsoft Dataverse
- 📊 Dynamic entity metadata retrieval
- 🤖 AI-powered semantic search and chat using Ollama
- 🎯 Specialized analysis for different entity types (Flow Runs, Plugin Trace Logs, etc.)
- 📈 Real-time progress visualization for embeddings generation
- 🔍 Context-aware responses using vector similarity search

## Prerequisites

- .NET 6.0 or higher
- Ollama installed and running
- Access to a Microsoft Dataverse environment
- Sufficient GPU, disk and system memory for running AI models

## Configuration

Add the following settings to your `App.config` file:

xml
<configuration>
<appSettings>
    <add key="DataverseConnectionString" value="AuthType=ClientSecret;url=;ClientId=;ClientSecret=" />
    <add key="AIModel" value="llama3" />
    <add key="OllamaHost" value="192.168.0.144" />
    <add key="OllamaPort" value="11434" />
</appSettings>
</configuration>


## Installation

1. Clone the repository
2. Restore NuGet packages
3. Configure your `App.config` file
4. Build and run the application

## Usage

bash
dotnet run


The application will:
1. Connect to your Dataverse environment
2. Retrieve entity metadata
3. Query the specified entity
4. Process the results
5. Ask your Dataverse data

## Error Handling

The application includes robust error handling for:
- Connection issues
- Insufficient resources for AI models
- Invalid XML responses
- General runtime errors

## Dependencies

- Microsoft.PowerPlatform.Dataverse.Client
- Microsoft.Xrm.Sdk
- System.Configuration.ConfigurationManager
- Ollama (external dependency)
- OllamaSharp
