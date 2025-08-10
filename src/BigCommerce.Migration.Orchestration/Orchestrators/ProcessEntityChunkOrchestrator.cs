using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Orchestrators;

/// <summary>
/// Sub-orchestrator for processing manageable chunks of entities to avoid Azure Functions timeout
/// Uses timeout-safe chunked sub-orchestration for better performance and reliability
/// 
/// Design Philosophy:
/// - Each chunk processes max 500 entities (well under 10-minute timeout limit)
/// - Enables parallel processing of multiple chunks
/// - Provides fault isolation - failed chunks don't affect others
/// - Supports checkpointing and resume capabilities
/// </summary>
public class ProcessEntityChunkOrchestrator
{
    private readonly ILogger<ProcessEntityChunkOrchestrator> _logger;

    public ProcessEntityChunkOrchestrator(ILogger<ProcessEntityChunkOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a single chunk of entities using the timeout-safe sub-orchestration pattern
    /// </summary>
    /// <param name="context">Durable orchestration context</param>
    /// <returns>Batch processing result for this chunk</returns>
    [Function("ProcessEntityChunkOrchestrator")]
    public async Task<BatchProcessingResult> ProcessEntityChunk(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ProcessEntityChunkRequest>();
        var startTime = context.CurrentUtcDateTime;

        _logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Starting chunk processing: {ChunkSize} {EntityType} entities " +
                             "(indexes {StartIndex}-{EndIndex}) for migration {MigrationId}",
            input.ChunkNumber, input.ChunkSize, input.EntityType, 
            input.StartIndex, input.StartIndex + input.ChunkSize - 1, input.MigrationId);

        try
        {
            // Check for cancellation before processing
            if (input.IsCancelled)
            {
                _logger.LogWarning("🚫 [CHUNK-{ChunkNumber}] Chunk processing cancelled: {CancellationReason}",
                    input.ChunkNumber, input.CancellationReason ?? "Unknown reason");
                
                return new BatchProcessingResult
                {
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string> { $"Chunk {input.ChunkNumber} was cancelled: {input.CancellationReason}" }
                };
            }

            // Process the chunk using the dedicated activity function
            // This activity is designed to handle only manageable chunks (max 500 entities)
            var result = await context.CallActivityAsync<BatchProcessingResult>(
                "ProcessEntityChunk", 
                input);

            var processingTime = context.CurrentUtcDateTime - startTime;
            
            if (result != null)
            {
                result.ProcessingTime = processingTime;
                
                _logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Chunk processing completed: " +
                                     "{SuccessfulEntities}/{TotalProcessed} entities successful in {ProcessingTimeMs}ms",
                    input.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                    processingTime.TotalMilliseconds);

                // Log any errors for this chunk
                if (result.Errors?.Any() == true)
                {
                    _logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] Chunk had {ErrorCount} errors: {Errors}",
                        input.ChunkNumber, result.Errors.Count, string.Join("; ", result.Errors));
                }
            }
            else
            {
                _logger.LogError("❌ [CHUNK-{ChunkNumber}] Chunk processing returned null result", input.ChunkNumber);
                
                result = new BatchProcessingResult
                {
                    TotalProcessed = input.ChunkSize,
                    SuccessfulEntities = 0,
                    FailedEntities = input.ChunkSize,
                    ProcessingTime = processingTime,
                    Errors = new List<string> { $"Chunk {input.ChunkNumber} processing returned null result" }
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            var processingTime = context.CurrentUtcDateTime - startTime;
            
            _logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Chunk processing failed after {ProcessingTimeMs}ms: {ErrorMessage}",
                input.ChunkNumber, processingTime.TotalMilliseconds, ex.Message);

            // Return error result for this chunk
            return new BatchProcessingResult
            {
                TotalProcessed = input.ChunkSize,
                SuccessfulEntities = 0,
                FailedEntities = input.ChunkSize,
                ProcessingTime = processingTime,
                Errors = new List<string> { $"Chunk {input.ChunkNumber} failed: {ex.Message}" }
            };
        }
    }
}