# OllamaDataverseEntityChatApp

A command-line tool that connects to Microsoft Dataverse, retrieves data, and uses AI (Embedding and ChatCompletion) to allow you to chat with your Dataverse data using semantic search capabilities.

## Features

- 🔌 Seamless connection to Microsoft Dataverse
- 📊 Dynamic entity metadata retrieval
- 🤖 AI-powered semantic search and chat using Ollama
- 🎯 Specialized analysis for different entity types (Flow Runs, Plugin Trace Logs, etc.)
- 📈 Real-time progress visualization for embeddings generation
- 🔍 Context-aware responses using vector similarity search

## Prerequisites

- .NET 8.0
- Ollama installed and running
- Milvus installed and running
- Access to a Microsoft Dataverse environment
- Sufficient GPU, disk and system memory for running AI models

## Configuration

Add the following settings to your `App.config` file:

```xml
<configuration>
<appSettings>
    <add key="DataverseConnectionString" value="AuthType=ClientSecret;url=;ClientId=;ClientSecret=" />
    <add key="AIGenerationModel" value="llama3" />
    <add key="AIEmbeddingModel" value="nomic-embed-text" />
    <add key="OllamaHost" value="Enter Ollama host IP address here" />
    <add key="OllamaPort" value="Enter Ollama port number here" />
    <add key="MaxRecords" value="10" />
    <add key="MultiThreadEmbedding" value="true" />
    <add key="SimilarityMethod" value="L2" />
    <add key="VectorDB" value="Milvus" />
    <add key="VectorDimensions" value="768" />
    <add key="MilvusHost" value="Enter Milvus host IP address here" />
    <add key="MilvusPort" value="Enter Milvus port number here" />
</appSettings>
</configuration>
```

## Installation

1. Clone the repository
2. Restore NuGet packages
3. Configure your `App.config` file
4. Build and run the application

## Usage

Before running the application, ensure Ollama and Milvus are running:

1. If you are using Ollama locally, start the Ollama server:

 ```bash
ollama serve
```

2. Start the Milvus server for a standalone instance:

```bash
docker run -d --name milvus-standalone -p 19530:19530 -p 9091:9091 milvusdb/milvus-standalone:latest
```

For a cluster setup, refer to the [Milvus documentation](https://milvus.io/docs).

3. Run the application:

 ```bash
dotnet run
```

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
- Milvus.Client

## Contributing

Contributions are welcome! Feel free to fork the repository and submit a pull request.