using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rws.LC.AppBlueprint.Helpers;
using Rws.LC.AppBlueprint.Infrastructure;
using Rws.LC.AppBlueprint.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rws.LC.AppBlueprint.Controllers
{
    [Route("v1")]
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAccountService _accountService;
        private readonly IHttpClientFactory _httpClientFactory;

        public WebhookController(
            ILogger<WebhookController> logger,
            IConfiguration configuration,
            IAccountService accountService,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _accountService = accountService;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Receives webhook events from Trados Cloud Platform
        /// </summary>
        [Authorize]
        [HttpPost("webhooks")]
        public async Task<IActionResult> ReceiveWebhook()
        {
            string payload;
            using (StreamReader sr = new StreamReader(Request.Body))
            {
                payload = await sr.ReadToEndAsync();
            }

            var tenantId = HttpContext.User?.GetTenantId();

            _logger.LogInformation("Webhook received from Trados Cloud for tenant {TenantId}", tenantId);
            _logger.LogDebug("Webhook payload: {Payload}", payload);

            try
            {
                // Parse the incoming webhook
                var webhookEvent = JsonSerializer.Deserialize<JsonDocument>(payload, JsonSettings.Default());
                var root = webhookEvent.RootElement;

                // Extract event information
                var eventType = root.TryGetProperty("eventType", out var eventTypeProp) ?
                    eventTypeProp.GetString() : "UNKNOWN";
                var timestamp = root.TryGetProperty("timestamp", out var timestampProp) ?
                    timestampProp.GetString() : DateTime.UtcNow.ToString("O");

                _logger.LogInformation("Processing webhook event: {EventType} for tenant {TenantId}",
                    eventType, tenantId);

                // Forward the webhook to your website with JWS authentication
                var result = await ForwardWebhookToWebsite(tenantId, eventType, payload, root);

                if (result.Success)
                {
                    _logger.LogInformation("Webhook successfully forwarded to website for tenant {TenantId}", tenantId);

                    return Ok(new
                    {
                        success = true,
                        message = "Webhook processed successfully",
                        eventType = eventType,
                        forwardedTo = "integration-website",
                        timestamp = DateTime.UtcNow.ToString("O")
                    });
                }
                else
                {
                    _logger.LogError("Failed to forward webhook to website: {Error}", result.Error);

                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Failed to forward webhook to integration website",
                        details = result.Error
                    });
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid JSON in webhook payload");
                return BadRequest(new
                {
                    success = false,
                    error = "Invalid JSON payload"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing webhook");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Internal server error"
                });
            }
        }

        /// <summary>
        /// Forward the webhook to your website with JWS authentication (passthrough)
        /// </summary>
        private async Task<WebhookForwardResult> ForwardWebhookToWebsite(string tenantId, string eventType, string originalPayload, JsonElement originalData)
        {
            try
            {
                // Create the payload to send to your website
                var webhookPayload = new
                {
                    accountId = tenantId,  // Changed to accountId to match Trados standard
                    eventType = eventType,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    data = originalData.ValueKind != JsonValueKind.Undefined ? (object)originalData : null,
                    originalPayload = JsonSerializer.Deserialize<object>(originalPayload),
                    source = "trados-cloud"
                };

                var payloadJson = JsonSerializer.Serialize(webhookPayload, JsonSettings.Default());

                // Send to your website's webhook endpoint
                string webhookUrl = "https://api.filkin.com/trados-integration/v1/webhooks";

                var result = await SendJwsSignedWebhook(webhookUrl, payloadJson);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding webhook to website");
                return new WebhookForwardResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Send JWS-signed webhook to your website (forward original JWS from Trados)
        /// </summary>
        private async Task<WebhookForwardResult> SendJwsSignedWebhook(string url, string payload)
        {
            using var httpClient = _httpClientFactory.CreateClient();

            _logger.LogInformation("Sending JWS-signed webhook to {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };

            // Forward the original JWS signature from Trados
            var jwsSignature = HttpContext.Request.Headers["x-lc-signature"].FirstOrDefault();
            if (!string.IsNullOrEmpty(jwsSignature))
            {
                request.Headers.Add("x-lc-signature", jwsSignature);
                _logger.LogInformation("Forwarding JWS signature from Trados to website");
            }
            else
            {
                _logger.LogWarning("No JWS signature found in original request");
                return new WebhookForwardResult
                {
                    Success = false,
                    Error = "Missing JWS signature from Trados request"
                };
            }

            // Add source identifier
            request.Headers.Add("X-Source", "trados-addon");

            try
            {
                var response = await httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Webhook response: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);

                if (response.IsSuccessStatusCode)
                {
                    return new WebhookForwardResult
                    {
                        Success = true,
                        Response = responseContent
                    };
                }
                else
                {
                    return new WebhookForwardResult
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {responseContent}"
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                return new WebhookForwardResult
                {
                    Success = false,
                    Error = $"HTTP request failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Result of webhook forwarding operation
        /// </summary>
        private class WebhookForwardResult
        {
            public bool Success { get; set; }
            public string Response { get; set; }
            public string Error { get; set; }
        }
    }
}