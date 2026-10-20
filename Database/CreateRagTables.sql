-- Creates the RAG tables. Requires SQL Server 17.0 (SQL Server 2025) or later for the VECTOR type.
-- Run against the database used by ConnectionStrings:RagDb. Existing tables are dropped and recreated.

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

-- Drop the child table first so the foreign key doesn't block dropping the parent.
DROP TABLE IF EXISTS [dbo].[RagDocumentChunks]
GO

DROP TABLE IF EXISTS [dbo].[RagSourceDocuments]
GO

CREATE TABLE [dbo].[RagSourceDocuments](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[FileName] [nvarchar](400) NOT NULL,
	[Title] [nvarchar](400) NULL,
	[IngestedAtUtc] [datetime2](7) NOT NULL CONSTRAINT [DF_RagSourceDocuments_IngestedAtUtc] DEFAULT (sysutcdatetime()),
	[PageCount] [int] NOT NULL,
 CONSTRAINT [PK_SourceDocuments] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO

CREATE TABLE [dbo].[RagDocumentChunks](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[SourceDocumentId] [int] NOT NULL,
	[ChunkIndex] [int] NOT NULL,
	[PageNumber] [int] NOT NULL,
	[Type] [int] NOT NULL,
	[Content] [nvarchar](max) NOT NULL,
	[Embedding] [vector](1536, float32) NOT NULL,
	[TokenCount] [int] NOT NULL,
 CONSTRAINT [PK_DocumentChunks] PRIMARY KEY CLUSTERED ([Id] ASC),
 CONSTRAINT [FK_DocumentChunks_SourceDocuments] FOREIGN KEY ([SourceDocumentId])
	REFERENCES [dbo].[RagSourceDocuments] ([Id]) ON DELETE CASCADE
)
GO
