using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;

namespace Rws.LC.AppBlueprint.Controllers
{
    /// <summary>
    /// Simple proxy controller for routing test website requests to XAMPP
    /// This only exists for integration testing
    /// </summary>
    [Route("trados-integration")]
    [ApiController]
    public class TestWebsiteProxyController : ControllerBase
    {
        private readonly ILogger<TestWebsiteProxyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private const string XAMPP_BASE_URL = "http://localhost:80";

        public TestWebsiteProxyController(ILogger<TestWebsiteProxyController> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Proxy all requests under /trados-integration/ to XAMPP
        /// </summary>
        [HttpGet]
        [HttpPost]
        [Route("{*path}")]
        public async Task<IActionResult> ProxyToXampp(string path = "")
        {
            try
            {
                var targetUrl = $"{XAMPP_BASE_URL}/trados-integration/{path}";
                // Add .php extension if path doesn't already have an extension
                if (!string.IsNullOrEmpty(path) && !Path.HasExtension(path))
                {
                    targetUrl += ".php";
                }
                if (!string.IsNullOrEmpty(Request.QueryString.Value))
                {
                    targetUrl += Request.QueryString.Value;
                }

                _logger.LogInformation("Proxying to XAMPP: {Method} {TargetUrl}", Request.Method, targetUrl);

                using var httpClient = _httpClientFactory.CreateClient();
                using var requestMessage = new HttpRequestMessage(new HttpMethod(Request.Method), targetUrl);

                // Copy body for POST requests
                if (Request.Method == "POST" && Request.ContentLength > 0)
                {
                    var body = await GetRawBodyStringAsync(Request);
                    requestMessage.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                // Copy important headers - ADD HMAC HEADERS
                if (Request.Headers.ContainsKey("X-Signature"))
                    requestMessage.Headers.Add("X-Signature", Request.Headers["X-Signature"].ToString());
                if (Request.Headers.ContainsKey("X-Timestamp"))
                    requestMessage.Headers.Add("X-Timestamp", Request.Headers["X-Timestamp"].ToString());
                if (Request.Headers.ContainsKey("X-Nonce"))
                    requestMessage.Headers.Add("X-Nonce", Request.Headers["X-Nonce"].ToString());

                // ADD THESE LINES FOR HMAC:
                if (Request.Headers.ContainsKey("X-HMAC-Signature"))
                    requestMessage.Headers.Add("X-HMAC-Signature", Request.Headers["X-HMAC-Signature"].ToString());
                if (Request.Headers.ContainsKey("Authorization"))
                    requestMessage.Headers.Add("Authorization", Request.Headers["Authorization"].ToString());

                // Also copy all headers that start with X-
                foreach (var header in Request.Headers)
                {
                    if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!requestMessage.Headers.Contains(header.Key))
                        {
                            try
                            {
                                requestMessage.Headers.Add(header.Key, header.Value.ToString());
                            }
                            catch
                            {
                                // Some headers might not be allowed, skip them
                            }
                        }
                    }
                }

                // DEBUG: Log all headers being sent
                _logger.LogInformation("=== DEBUG: Headers being sent to XAMPP ===");
                foreach (var header in requestMessage.Headers)
                {
                    _logger.LogInformation("Header: {Key} = {Value}", header.Key, string.Join(", ", header.Value));
                }
                _logger.LogInformation("=== END DEBUG ===");

                // Actually send the request and return the response
                using var response = await httpClient.SendAsync(requestMessage);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("XAMPP response: {StatusCode}", response.StatusCode);

                return new ContentResult
                {
                    Content = responseContent,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to proxy to XAMPP. Is Apache running?");
                return StatusCode(502, new { error = "Test website not available", message = "XAMPP may not be running" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Proxy error");
                return StatusCode(500, new { error = "Proxy error", message = ex.Message });
            }
        }

        /// <summary>
        /// Helper method to get raw body string
        /// </summary>
        private async Task<string> GetRawBodyStringAsync(HttpRequest request)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;
            return body;
        }
    }
}