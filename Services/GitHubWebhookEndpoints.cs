using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Minimal API endpoints for GitHub webhooks
/// </summary>
public static class GitHubWebhookEndpoints
{
    public static void MapGitHubWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks/github");

        // Receive webhook from GitHub
        group.MapPost("/{webhookId}", HandleGitHubWebhook)
            .AllowAnonymous(); // GitHub doesn't send auth headers, uses signature instead
    }

    private static async Task<IResult> HandleGitHubWebhook(
        string webhookId,
        HttpContext httpContext,
        GitHubWebhookService webhookService,
        ILogger<GitHubWebhookService> logger)
    {
        try
        {
            // Get event type from header
            var eventType = httpContext.Request.Headers["X-GitHub-Event"].FirstOrDefault();
            if (string.IsNullOrEmpty(eventType))
            {
                return Results.BadRequest(new { error = "Missing X-GitHub-Event header" });
            }

            // Get signature from header
            var signature = httpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

            // Get delivery ID
            var deliveryId = httpContext.Request.Headers["X-GitHub-Delivery"].FirstOrDefault();

            // Read payload
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync();

            logger.LogInformation("Received GitHub webhook: {Event} for {WebhookId}, delivery: {DeliveryId}",
                eventType, webhookId, deliveryId);

            // Process the webhook
            var (success, message) = await webhookService.ProcessWebhookAsync(
                webhookId,
                eventType,
                signature,
                payload);

            if (success)
            {
                return Results.Ok(new { success = true, message });
            }
            else
            {
                logger.LogWarning("Webhook processing failed: {Message}", message);
                return Results.BadRequest(new { success = false, error = message });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling GitHub webhook {WebhookId}", webhookId);
            return Results.StatusCode(500);
        }
    }
}
