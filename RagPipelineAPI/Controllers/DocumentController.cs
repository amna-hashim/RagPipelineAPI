using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RagPipeline;
using RagPipeline.Abstractions;
using SimpleChatbot.DTOs;
using SimpleChatbot.Models;
using SimpleChatbot.Services;
using RagPipelineAPI.DTOs;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;


namespace SimpleChatbot.Controllers
{
    [ApiController]
    [Route("api/document")]
    public class DocumentController : BaseController
    {
        private readonly IChatService _chatService;
        private readonly RagPipeline.Abstractions.IKnowledgeRetriever _knowledge;
        private readonly IKnowledgeIngestor _ingestor;

        public DocumentController(IChatService chatService, RagPipeline.Abstractions.IKnowledgeRetriever knowledge, IKnowledgeIngestor ingestor)
        {
            _chatService = chatService;
            _knowledge = knowledge;
            _ingestor = ingestor;
        }

        /// <summary>
        /// Uploads a PDF and runs it through the RAG ingestion pipeline
        /// (extract -> chunk -> embed -> store).
        /// </summary>
        [HttpPost("ingest")]
        [RequestSizeLimit(50_000_000)] // 50MB - adjust to your expected document sizes
        public async Task<IActionResult> Ingest(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return BadRequest("No file was uploaded.");

            if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only PDF files are supported.");

            // Save to a temp path - IngestionService reads from disk (needed for PdfPig
            // and the Azure/vision calls), not from an in-memory stream.
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{file.FileName}");

            try
            {
                await using (var stream = System.IO.File.Create(tempPath))
                {
                    await file.CopyToAsync(stream, ct);
                }

                var chunkCount = await _ingestor.IngestPdfAsync(tempPath, ct);

                return Ok(new
                {
                    fileName = file.FileName,
                    chunksCreated = chunkCount
                });
            }
            catch (Exception ex)
            {
                // TODO: use your existing logging setup instead of Console
                Console.Error.WriteLine($"Ingestion failed for {file.FileName}: {ex}");
                return StatusCode(500, "Failed to ingest document. Please try again.");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        [HttpPost("query")]
        public async Task<ActionResult<List<MemoryResultDto>>> QueryDocument([FromBody] QueryRequest request, CancellationToken ct = default)
        {
            var query = request.Query;
            var userId = "testUser123";// User.FindFirstValue(ClaimTypes.NameIdentifier)!; // from auth
            CreateConversationDto conversationDto = new CreateConversationDto( query, userId);
            var newConversation = await _chatService.AddConversationAsync(userId, conversationDto, ct);

            if(newConversation.Id == Guid.Empty)
            {
                return BadRequest("Failed to create a new conversation.");
            }
            
            CreateMessageDto userMessageDto = new CreateMessageDto(newConversation.Id, "user", query);
            var userMessageDtoSuccess = await _chatService.AddMessageAsync(userId, userMessageDto, ct);

            if(userMessageDtoSuccess == null)
            {
                return BadRequest("Failed to add new user message to the conversation.");
            }

            var result = await _knowledge.AskAsync(query, ct);

            CreateMessageDto agentMessageDto = new CreateMessageDto(newConversation.Id, "assistant", result.Answer);
            var agentMessageSuccess = await _chatService.AddMessageAsync(userId, agentMessageDto, ct);

            if(agentMessageSuccess == null)
            {
                return BadRequest("Failed to add new agent message to the conversation.");
            }

            return Ok(result);
        }
    }
}
