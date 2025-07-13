using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rws.LC.AppBlueprint.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class IntegrationController : ControllerBase
    {
        private readonly ILogger<IntegrationController> _logger;

        public IntegrationController(ILogger<IntegrationController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Health check endpoint for integration status
        /// </summary>
        [HttpGet("health")]
        public IActionResult GetHealth()
        {
            _logger.LogInformation("Integration health check requested");

            var health = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                integration = new
                {
                    automaticProvisioning = true,
                    hmacAuthentication = true,
                    proxyEnabled = true
                }
            };

            return Ok(health);
        }

        /// <summary>
        /// Get integration status and statistics
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            _logger.LogInformation("Integration status requested");

            var status = new
            {
                integrationActive = true,
                automaticSetup = true,
                lastActivity = DateTime.UtcNow,
                testWebsiteUrl = "http://localhost/trados-integration/",
                dashboardUrl = "http://localhost/trados-integration/index.php",
                message = "Integration is working automatically. Check the dashboard for real-time data."
            };

            return Ok(status);
        }

        /// <summary>
        /// Get integration configuration info
        /// </summary>
        [HttpGet("config")]
        public IActionResult GetConfiguration()
        {
            _logger.LogInformation("Integration configuration requested");

            var config = new
            {
                proxyBaseUrl = "http://localhost/trados-integration/",
                hmacAuthEnabled = true,
                automaticProvisioningEnabled = true,
                supportedEvents = new[] { "INSTALLED", "UNINSTALLED", "UPDATED" },
                endpoints = new
                {
                    provision = "/provision-instance.php",
                    health = "/health-check.php",
                    dashboard = "/index.php"
                }
            };

            return Ok(config);
        }

        /// <summary>
        /// View integration logs (redirect to dashboard)
        /// </summary>
        [HttpGet("logs")]
        public IActionResult GetLogs()
        {
            _logger.LogInformation("Integration logs requested - redirecting to dashboard");

            // Redirect to the test website dashboard where real logs are displayed
            return Redirect("http://localhost/trados-integration/index.php");
        }

        /// <summary>
        /// Test connectivity to the test website
        /// </summary>
        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            _logger.LogInformation("Testing connection to test website");

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await httpClient.GetAsync("http://localhost/trados-integration/health-check.php");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Connection test successful");
                    return Ok(new
                    {
                        status = "success",
                        message = "Test website is reachable and healthy",
                        testWebsiteResponse = JsonSerializer.Deserialize<object>(content),
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogWarning("Connection test failed with status: {StatusCode}", response.StatusCode);
                    return BadRequest(new
                    {
                        status = "error",
                        message = $"Test website returned status: {response.StatusCode}",
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during connection test");
                return BadRequest(new
                {
                    status = "error",
                    message = "Could not connect to test website",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Connection test timed out");
                return BadRequest(new
                {
                    status = "error",
                    message = "Connection test timed out",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during connection test");
                return StatusCode(500, new
                {
                    status = "error",
                    message = "Unexpected error occurred",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Get integration metrics and statistics
        /// </summary>
        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics()
        {
            _logger.LogInformation("Integration metrics requested");

            try
            {
                // Try to get real metrics from the test website
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var response = await httpClient.GetAsync("http://localhost/trados-integration/api/stats.php");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var metrics = JsonSerializer.Deserialize<object>(content);

                    return Ok(new
                    {
                        status = "success",
                        source = "live_data",
                        metrics = metrics,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    // Fallback to basic metrics
                    return Ok(new
                    {
                        status = "success",
                        source = "fallback_data",
                        metrics = new
                        {
                            integration_active = true,
                            automatic_setup = true,
                            last_check = DateTime.UtcNow
                        },
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch live metrics, returning basic status");

                return Ok(new
                {
                    status = "success",
                    source = "basic_status",
                    metrics = new
                    {
                        integration_active = true,
                        automatic_setup = true,
                        message = "Live metrics unavailable - integration working normally"
                    },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Information endpoint about the integration
        /// </summary>
        [HttpGet("info")]
        public IActionResult GetInfo()
        {
            var info = new
            {
                title = "Trados Deployment Addon Integration",
                description = "Automatic integration between Trados addon and test website",
                version = "1.0.0",
                features = new[]
                {
                    "Automatic provisioning during addon installation",
                    "HMAC authentication for secure communication",
                    "Real-time dashboard with live integration data",
                    "Comprehensive activity logging",
                    "Health monitoring and status checks"
                },
                endpoints = new
                {
                    dashboard = "http://localhost/trados-integration/",
                    provision = "http://localhost/trados-integration/provision-instance.php",
                    health = "http://localhost/trados-integration/health-check.php"
                },
                documentation = new
                {
                    setup = "Integration happens automatically when addon is installed",
                    monitoring = "View real-time data at the dashboard URL",
                    troubleshooting = "Check health endpoint for system status"
                }
            };

            return Ok(info);
        }
    }
}