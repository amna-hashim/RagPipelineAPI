# RagPipeline

## Overview

RagPipeline is a Retrieval-Augmented Generation (RAG) API built with ASP.NET Core (.NET 10). You upload PDF documents, and it answers questions using their content.

- **Ingestion**: an uploaded PDF is extracted, split into chunks, embedded with `text-embedding-3-small` (1536 dimensions), and stored in SQL Server using the native `VECTOR` column type.
- **Querying**: a question is embedded, the closest chunks are found with `VECTOR_DISTANCE` (cosine), and `gpt-4o` writes an answer from them. The question and the answer are saved as a conversation through the SimpleChatbot project.
- **Models**: served through [GitHub Models](https://github.com/marketplace/models), or directly through OpenAI as a fallback.
- **UI**: simple static pages in `wwwroot` (`index.html`, `upload.html`, `ask.html`).

### Projects

| Project | Purpose |
|---|---|
| `RagPipeline` | Core library: PDF extraction, chunking, embeddings, vector search, answer generation |
| `RagPipelineAPI` | Web API host, controllers, and static UI |
| `RagPipeline.Tests` | xUnit integration tests for ingestion |

### Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/document/ingest` | Upload a PDF (multipart form field `file`, up to 50 MB) |
| `POST` | `/api/document/query` | Ask a question: `{ "query": "..." }` |

## Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server 17.0 (SQL Server 2025) or later, for the `VECTOR` type
- A GitHub personal access token with access to GitHub Models
- The SimpleChatbot repository cloned next to this one, because `RagPipelineAPI` references `..\..\SimpleChatbot\SimpleChatbot\SimpleChatbot.csproj`:

  ```
  learning_AI/
  ├── RagPipelineAPI/
  └── SimpleChatbot/
  ```

### 1. Configure secrets

Run these from the `RagPipelineAPI` project folder:

```powershell
cd RagPipelineAPI
dotnet user-secrets set "GH_PAT" "<your-github-token>"
dotnet user-secrets set "Jwt:Key" "<a-long-random-signing-key>"
dotnet user-secrets set "ConnectionStrings:ChatDb" "<sql-server-connection-string>"
dotnet user-secrets set "ConnectionStrings:RagDb" "<sql-server-connection-string>"
```

To call OpenAI directly instead of GitHub Models, set `OpenAI:ApiKey` and leave `GH_PAT` unset. Model names and the endpoint are set in `appsettings.json` under `GitHubModels`.

### 2. Prepare the database

Run [Database/CreateRagTables.sql](Database/CreateRagTables.sql) against the `RagDb` database. It creates the `RagSourceDocuments` and `RagDocumentChunks` tables, including the `VECTOR(1536)` embedding column. EF Core can't map that column, so the tables are created with SQL instead of migrations. The script drops and recreates the tables, so running it again deletes any ingested documents.

The `ChatDb` database uses the SimpleChatbot schema.

### 3. Run

```powershell
dotnet restore
dotnet run --project RagPipelineAPI
```

The app starts at http://localhost:5080. Swagger is at http://localhost:5080/swagger in Development.

### 4. Test

```powershell
dotnet test
```

The integration tests read their settings from `RagPipeline.Tests/appsettings.test.json`.
