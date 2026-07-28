using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RagPipeline;
using SimpleChatbot.DTOs;
using SimpleChatbot.Models;
using SimpleChatbot.Services;
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

        public DocumentController(IChatService chatService, RagPipeline.Abstractions.IKnowledgeRetriever knowledge)
        {
            _chatService = chatService;
            _knowledge = knowledge;
        }

        [HttpGet("query")]
        public async Task<ActionResult<List<MemoryResultDto>>> QueryDocument([FromQuery] string query, CancellationToken ct = default)
        {
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
