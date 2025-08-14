using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Activities.Models;
using System.Linq;

namespace BigCommerce.Migration.Functions.Orchestrators;

/// <summary>
/// Sub-orchestrator for processing manageable chunks of entities to avoid Azure Functions timeout
/// Uses timeout-safe chunked sub-orchestration for better performance and reliability
/// Enhanced with native cancellation support for Phase 2.3.4
/// 
/// Design Philosophy:
/// - Each chunk processes max 500 entities (well under 10-minute timeout limit)
/// - Enables parallel processing of multiple chunks
/// - Provides fault isolation - failed chunks don't affect others
/// - Supports checkpointing and resume capabilities
/// - Integrates with native cancellation system
/// </summary>
public static class ProcessEntityChunkOrchestrator
{
    /// <summary>
    /// Processes a single chunk of entities using the timeout-safe sub-orchestration pattern
    /// Phase 2.3.4: Enhanced with native cancellation integration
    /// </summary>
    /// <param name="context">Durable orchestration context</param>
    /// <returns>Batch processing result for this chunk</returns>
    [Function("ProcessEntityChunkOrchestrator")]
    public static async Task<BatchProcessingResult> ProcessEntityChunk(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("ProcessEntityChunkOrchestrator");
        var input = context.GetInput<ProcessEntityChunkRequest>();
        var startTime = context.CurrentUtcDateTime;

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "Chunk processing request is required");
        }

        logger.LogInformation("🎯 [CHUNK-{ChunkNumber}] Starting chunk processing: {ChunkSize} {EntityType} entities " +
                             "(indexes {StartIndex}-{EndIndex}) for migration {MigrationId}",
            input.ChunkNumber, input.ChunkSize, input.EntityType, 
            input.StartIndex, input.StartIndex + input.ChunkSize - 1, input.MigrationId);

        try
        {
            // Phase 2.3.4: Enhanced cancellation integration - Check blob store before processing
            var cancellationState = new {
                IsCancelled = input.IsCancelled,
                CancellationReason = input.CancellationReason ?? string.Empty,
                CancelledAt = input.CancelledAt
            };

            // Check for inherited cancellation state
            if (cancellationState.IsCancelled)
            {
                logger.LogWarning("🚫 [CHUNK-{ChunkNumber}] Chunk processing cancelled (inherited): {CancellationReason}",
                    input.ChunkNumber, cancellationState.CancellationReason);
                
                return new BatchProcessingResult
                {
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string> { $"Chunk {input.ChunkNumber} was cancelled (inherited): {cancellationState.CancellationReason}" }
                };
            }

            // Phase 2.3.4: Check for real-time cancellation via blob store
            var (isCancelled, cancellationReason) = await context.CallActivityAsync<(bool, string)>(
                "CheckCancellationFlag", 
                input.MigrationId);

            if (isCancelled)
            {
                logger.LogWarning("🚫 [CHUNK-{ChunkNumber}] Chunk processing cancelled (real-time): {CancellationReason}",
                    input.ChunkNumber, cancellationReason);

                // Update cancellation state
                cancellationState = new {
                    IsCancelled = true,
                    CancellationReason = cancellationReason,
                    CancelledAt = (DateTime?)context.CurrentUtcDateTime
                };

                // Update the input for the activity
                input.IsCancelled = true;
                input.CancellationReason = cancellationReason;
                input.CancelledAt = context.CurrentUtcDateTime;
                
                return new BatchProcessingResult
                {
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string> { $"Chunk {input.ChunkNumber} was cancelled (real-time): {cancellationReason}" }
                };
            }

            // Process the chunk using the dedicated activity function
            // This activity is designed to handle only manageable chunks (max 500 entities)
            logger.LogInformation("🔄 [CHUNK-{ChunkNumber}] Calling ProcessEntityChunk activity for {EntityType}", 
                input.ChunkNumber, input.EntityType);

            var result = await context.CallActivityAsync<BatchProcessingResult>(
                "ProcessEntityChunk", 
                input);

            var processingTime = context.CurrentUtcDateTime - startTime;
            
            if (result != null)
            {
                result.ProcessingTime = processingTime;
                
                logger.LogInformation("✅ [CHUNK-{ChunkNumber}] Chunk processing completed: " +
                                     "{SuccessfulEntities}/{TotalProcessed} entities successful in {ProcessingTimeMs}ms",
                    input.ChunkNumber, result.SuccessfulEntities, result.TotalProcessed, 
                    processingTime.TotalMilliseconds);

                // Log any errors for this chunk
                if (result.Errors?.Any() == true)
                {
                    logger.LogWarning("⚠️ [CHUNK-{ChunkNumber}] Chunk had {ErrorCount} errors: {Errors}",
                        input.ChunkNumber, result.Errors.Count, string.Join("; ", result.Errors));
                }
            }
            else
            {
                logger.LogError("❌ [CHUNK-{ChunkNumber}] Chunk processing returned null result", input.ChunkNumber);
                
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
            
            logger.LogError(ex, "💥 [CHUNK-{ChunkNumber}] Chunk processing failed after {ProcessingTimeMs}ms: {ErrorMessage}",
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