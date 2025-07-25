﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rws.LC.AppBlueprint.Enums;
using Rws.LC.AppBlueprint.Helpers;
using Rws.LC.AppBlueprint.Infrastructure;
using Rws.LC.AppBlueprint.Interfaces;
using Rws.LC.AppBlueprint.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Rws.LC.AppBlueprint.Controllers
{
    [Route("v1")]
    [ApiController]
    public class StandardController : ControllerBase
    {
        /// <summary>
        /// The configuration
        /// </summary>
        private readonly IConfiguration _configuration;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The descriptor service.
        /// </summary>
        private readonly IDescriptorService _descriptorService;

        /// <summary>
        /// The account service.
        /// </summary>
        private readonly IAccountService _accountService;

        /// <summary>
        /// The health reporter.
        /// </summary>
        private readonly IHealthReporter _healthReporter;

        /// <summary>
        /// HTTP client factory for making requests
        /// </summary>
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardController"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="descriptorService">The descriptor service.</param>
        /// <param name="accountService">The account service.</param>
        /// <param name="healthReporter">The health reporter.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        public StandardController(IConfiguration configuration,
            ILogger<StandardController> logger,
            IDescriptorService descriptorService,
            IAccountService accountService,
            IHealthReporter healthReporter,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _descriptorService = descriptorService;
            _accountService = accountService;
            _healthReporter = healthReporter;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Gets the app descriptor.
        /// </summary>
        /// <returns>The descriptor</returns>
        [HttpGet("descriptor")]
        public IActionResult Descriptor()
        {
            // This endpoint provides the descriptor for the Language Cloud to inspect and register correctly.
            // It can be implemented in any number of ways. The example implementation is to load the descriptor.json file
            // Alternative implementation can be generating the descriptor based on config settings, environment variables,
            // etc.
            _logger.LogInformation("Entered Descriptor endpoint.");

            // Descriptor service will provide an object describing the descriptor.
            JsonNode descriptor = _descriptorService.GetDescriptor();

            // TODO: You might need to change the baseUrl in appsettings.json
            descriptor["baseUrl"] = _configuration["baseUrl"];

            if (_configuration.GetValue<bool>("multiRegion:enabled"))
            {
                descriptor["regionalBaseUrls"] = new JsonObject();

                string euBaseUrl = _configuration.GetValue<string>("multiRegion:regionalBaseUrls:eu");
                string caBaseUrl = _configuration.GetValue<string>("multiRegion:regionalBaseUrls:ca");

                if (!string.IsNullOrEmpty(euBaseUrl))
                {
                    descriptor["regionalBaseUrls"]["eu"] = euBaseUrl;
                }
                if (!string.IsNullOrEmpty(caBaseUrl))
                {
                    descriptor["regionalBaseUrls"]["ca"] = caBaseUrl;
                }
            }

            return Ok(descriptor);
        }

        /// <summary>
        /// Gets the app health.
        /// </summary>
        /// <returns>200 status code if it's healthy.</returns>
        [HttpGet("health")]
        public IActionResult Health()
        {
            // This is a health check endpoint. In most cases returning Ok is enough, but you might want to make checks
            // to resources this service uses, like: DB, message queues, storage etc.
            // Any response besides 200 Ok, will be considered as failure. As a suggestion use "return StatusCode(500);"
            // when you need to signal that the service is having health issues.

            var isHealthy = _healthReporter.IsServiceHealthy();
            if (isHealthy)
            {
                return Ok();
            }

            return StatusCode(500);
        }

        /// <summary>
        /// This endpoint provides the documentation for the app. It can return the HTML page with the documentation
        /// or redirect to a page. In this sample redirect is used with URL configured in appsettings.json
        /// </summary>
        [HttpGet("documentation")]
        public IActionResult Documentation()
        {
            return Redirect(_configuration.GetValue<string>("documentationUrl"));
        }

        /// <summary>
        /// Receive lifecycle events for the app.
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost("app-lifecycle")]
        public async Task<IActionResult> AppLifecycle()
        {
            string payload;
            using (StreamReader sr = new StreamReader(Request.Body))
            {
                payload = await sr.ReadToEndAsync();
            }

            var tenantId = HttpContext.User?.GetTenantId();
            string devTenantId = HttpContext.Request.Headers.SingleOrDefault(h => h.Key.Equals(Constants.DevTenantIdHeader, StringComparison.OrdinalIgnoreCase)).Value;
            string appId = HttpContext.Request.Headers.SingleOrDefault(h => h.Key.Equals(Constants.AppIdHeader, StringComparison.OrdinalIgnoreCase)).Value;

            var lifecycle = JsonSerializer.Deserialize<AppLifecycleEvent>(payload, JsonSettings.Default());
            switch (lifecycle.Id)
            {
                case AppLifecycleEventEnum.REGISTERED:
                    _logger.LogInformation($"App Registered in Language Cloud.");
                    // This is the event notifying that the App has been registered in Language Cloud
                    // no further details are available for that event
                    AppLifecycleEvent<RegisteredEvent> registeredEvent = JsonSerializer.Deserialize<AppLifecycleEvent<RegisteredEvent>>(payload, JsonSettings.Default());
                    await _accountService.SaveRegistrationInfo(registeredEvent.Data, tenantId, appId, CancellationToken.None).ConfigureAwait(true);
                    break;
                case AppLifecycleEventEnum.INSTALLED:
                    _logger.LogInformation("App Installed Event Received for tenant id {TenantId}.", tenantId);
                    await _accountService.ValidateLifecycleEvent(devTenantId, appId);
                    AppLifecycleEvent<InstalledEvent> installedEvent = JsonSerializer.Deserialize<AppLifecycleEvent<InstalledEvent>>(payload, JsonSettings.Default());
                    await _accountService.SaveAccountInfo(tenantId, installedEvent.Data.Region, CancellationToken.None).ConfigureAwait(true);

                    break;
                case AppLifecycleEventEnum.UNREGISTERED:
                    // This is the event notifying that the App has been unregistered/deleted from Language Cloud.
                    // No further details are available for that event.
                    _logger.LogInformation("App Unregistered Event Received.");
                    await _accountService.ValidateLifecycleEvent(devTenantId, appId);
                    // All the tenant information should be removed.
                    await _accountService.RemoveAccounts(CancellationToken.None).ConfigureAwait(true);
                    // Remove the registration information
                    await _accountService.RemoveRegistrationInfo(CancellationToken.None).ConfigureAwait(true);
                    break;

                case AppLifecycleEventEnum.UNINSTALLED:
                    _logger.LogInformation("App Uninstalled Event Received.");
                    await _accountService.ValidateLifecycleEvent(devTenantId, appId);

                    // NOTIFY PHP SYSTEM ABOUT UNINSTALL
                    try
                    {
                        await NotifyUninstallAsync(tenantId);
                        _logger.LogInformation("Uninstall notification sent successfully for tenant {TenantId}", tenantId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to notify uninstall for tenant {TenantId}: {Error}", tenantId, ex.Message);
                    }

                    await _accountService.RemoveAccountInfo(tenantId, CancellationToken.None).ConfigureAwait(true);
                    break;
            }

            return Ok();
        }

        /// <summary>
        /// Gets the configuration settings.
        /// </summary>
        /// <returns>The updated configuration settings.</returns>
        [Authorize]
        [HttpGet("configuration")]
        public async Task<IActionResult> GetConfigurationSettings()
        {
            // All configuration settings must be returned to the caller.
            // Configurations that are secret will be returned with the value set to "*****", if they have a value.

            _logger.LogInformation("Retrieving the configuration settings.");
            var tenantId = HttpContext.User?.GetTenantId();
            ConfigurationSettingsResult configurationSettingsResult = await _accountService.GetConfigurationSettings(tenantId, CancellationToken.None).ConfigureAwait(true);

            return Ok(configurationSettingsResult);
        }

        /// <summary>
        /// Sets or updates the configuration settings.
        /// </summary>
        /// <returns>The updated configuration settings.</returns>
        [Authorize]
        [HttpPost("configuration")]
        public async Task<IActionResult> SetConfigurationSettings(List<ConfigurationValueModel> configurationValues)
        {
            _logger.LogInformation("Setting the configuration settings.");

            var tenantId = HttpContext.User?.GetTenantId();

            ConfigurationSettingsResult configurationSettingsResult = await _accountService.SaveOrUpdateConfigurationSettings(tenantId, configurationValues, CancellationToken.None).ConfigureAwait(true);

            return Ok(configurationSettingsResult);
        }

        /// <summary>
        /// Validates the configuration.
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost("configuration/validation")]
        public async Task<IActionResult> ValidateConfiguration()
        {
            _logger.LogInformation("Validating the configuration settings.");

            var tenantId = HttpContext.User?.GetTenantId();
            await _accountService.ValidateConfigurationSettings(tenantId, CancellationToken.None).ConfigureAwait(true);

            // ADD AUTOMATIC INTEGRATION SETUP HERE
            _logger.LogInformation("Starting automatic integration setup for tenant {TenantId}", tenantId);
            try
            {
                await SetupIntegrationAsync(tenantId);
                _logger.LogInformation("Integration setup completed successfully for tenant {TenantId}", tenantId);
            }
            catch (Exception integrationEx)
            {
                _logger.LogError(integrationEx, "Integration setup failed for tenant {TenantId}: {Error}", tenantId, integrationEx.Message);
                // Don't fail the validation if integration setup fails
            }

            return Ok();
        }

        /// <summary>
        /// This endpoint provides the privacy policy for the app. It can return the HTML page with the privacy policy
        /// or redirect to a page. In this sample redirect is used the static file privacyPolicy.html.
        /// </summary>
        [HttpGet("privacyPolicy")]
        public IActionResult PrivacyPolicy()
        {
            var html = System.IO.File.ReadAllText(@"./resources/privacyPolicy.html");
            return base.Content(html, "text/html");
        }

        /// <summary>
        /// This endpoint provides the terms and conditions for the app. It can return the HTML page with the terms and conditions
        /// or redirect to a page. In this sample redirect is used the static file termsAndCondition.html.
        /// </summary>
        [HttpGet("termsAndConditions")]
        public IActionResult TermsANdConditions()
        {
            var html = System.IO.File.ReadAllText(@"./resources/termsAndConditions.html");
            return base.Content(html, "text/html");
        }

        /// <summary>
        /// Automatically sets up integration with external provisioning system
        /// </summary>
        /// <param name="tenantId">The tenant ID for the installation</param>
        private async Task SetupIntegrationAsync(string tenantId)
        {
            // DEBUG: Log all incoming headers
            _logger.LogInformation("=== DEBUG: All incoming headers from Trados ===");
            foreach (var header in HttpContext.Request.Headers)
            {
                _logger.LogInformation("Incoming Header: {Key} = {Value}", header.Key, header.Value.ToString());
            }
            _logger.LogInformation("=== END DEBUG ===");

            _logger.LogInformation("Setting up integration for tenant {TenantId}", tenantId);

            // Get stored credentials for this installation
            var credentials = await _accountService.GetIntegrationCredentials().ConfigureAwait(false);

            if (string.IsNullOrEmpty(credentials.ClientId) || string.IsNullOrEmpty(credentials.ClientSecret))
            {
                throw new InvalidOperationException("Missing required Trados credentials for integration setup");
            }

            // Prepare provisioning request
            var provisioningData = new
            {
                accountId = tenantId,  // Changed from tenantId to accountId to match Trados standard
                clientCredentials = new
                {
                    clientId = credentials.ClientId,
                    clientSecret = credentials.ClientSecret
                },
                eventType = "INSTALLED",
                configurationData = new { },
                webhook_endpoint = "/webhook/trados",
                integration_type = "deployment_production",
                source_addon = "trados-deployment-addon",
                auto_provisioned = true,
                provisioned_at = DateTime.UtcNow.ToString("O")
            };

            // Get the integration endpoint URL
            string integrationUrl = _configuration.GetValue<string>("integrationEndpoint") ??
                                  "https://api.filkin.com/trados-integration/provision-instance";

            _logger.LogInformation("Calling integration endpoint: {Url}", integrationUrl);

            // Send JWS-signed request (forward the original JWS from Trados)
            var result = await SendJwsSignedRequestAsync(integrationUrl, provisioningData);

            if (result.Success)
            {
                _logger.LogInformation("Integration provisioned successfully. Instance ID: {InstanceId}", result.InstanceId);

                // Optionally save the webhook URL if returned
                if (!string.IsNullOrEmpty(result.WebhookUrl))
                {
                    await _accountService.SaveWebhookUrl(tenantId, result.WebhookUrl, CancellationToken.None).ConfigureAwait(false);
                    _logger.LogInformation("Webhook URL saved: {WebhookUrl}", result.WebhookUrl);
                }
            }
            else
            {
                throw new InvalidOperationException($"Integration setup failed: {result.Error}");
            }
        }

        /// <summary>
        /// Sends JWS-signed request to external provisioning system by forwarding the original JWS from Trados
        /// </summary>
        private async Task<IntegrationResult> SendJwsSignedRequestAsync(string url, object data)
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var payload = JsonSerializer.Serialize(data, JsonSettings.Default());

            _logger.LogInformation("Sending JWS request to {Url} using original Trados authentication", url);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };

            // Forward the original JWS signature from Trados
            var jwsSignature = HttpContext.Request.Headers["x-lc-signature"].FirstOrDefault();
            if (!string.IsNullOrEmpty(jwsSignature))
            {
                request.Headers.Add("x-lc-signature", jwsSignature);
                _logger.LogInformation("Forwarding JWS signature from Trados");
            }
            else
            {
                _logger.LogWarning("No JWS signature found in request headers");
                throw new InvalidOperationException("Missing JWS signature from Trados request");
            }

            var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Integration API response: {StatusCode} - {Content}",
                response.StatusCode, responseContent);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonDocument>(responseContent, JsonSettings.Default());
                var root = result.RootElement;

                return new IntegrationResult
                {
                    Success = root.GetProperty("success").GetBoolean(),
                    InstanceId = root.TryGetProperty("instanceId", out var instanceId) ? instanceId.GetString() : null,
                    WebhookUrl = root.TryGetProperty("webhookEndpoint", out var webhookUrl) ? webhookUrl.GetString() : null,
                    Error = root.TryGetProperty("error", out var error) ? error.GetString() : null
                };
            }
            else
            {
                return new IntegrationResult
                {
                    Success = false,
                    Error = $"HTTP {response.StatusCode}: {responseContent}"
                };
            }
        }

        /// <summary>
        /// Notifies the PHP system about addon uninstall
        /// </summary>
        private async Task NotifyUninstallAsync(string tenantId)
        {
            // Get stored credentials for the uninstall notification
            var credentials = await _accountService.GetIntegrationCredentials().ConfigureAwait(false);

            var provisioningData = new
            {
                accountId = tenantId,  // Changed from tenantId to accountId
                clientCredentials = new
                {
                    clientId = credentials.ClientId ?? "uninstall_placeholder",
                    clientSecret = credentials.ClientSecret ?? "uninstall_placeholder"
                },
                eventType = "UNINSTALLED",
                timestamp = DateTime.UtcNow.ToString("O")
            };

            string integrationUrl = _configuration.GetValue<string>("integrationEndpoint") ??
                                  "https://api.filkin.com/trados-integration/provision-instance";

            _logger.LogInformation("Sending uninstall notification to: {Url}", integrationUrl);

            await SendUninstallRequestAsync(integrationUrl, provisioningData);
        }

        /// <summary>
        /// Sends uninstall request with JWS authentication
        /// </summary>
        private async Task SendUninstallRequestAsync(string url, object data)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(data, JsonSettings.Default());

            _logger.LogInformation("Sending JWS-authenticated uninstall request");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };

            // Forward the original JWS signature from Trados
            var jwsSignature = HttpContext.Request.Headers["x-lc-signature"].FirstOrDefault();
            if (!string.IsNullOrEmpty(jwsSignature))
            {
                request.Headers.Add("x-lc-signature", jwsSignature);
            }
            else
            {
                throw new InvalidOperationException("Cannot authenticate uninstall request - JWS signature not available");
            }

            var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Uninstall API response: {StatusCode} - {Content}",
                response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Uninstall notification failed: HTTP {response.StatusCode}: {responseContent}");
            }
        }

        /// <summary>
        /// Result of integration setup
        /// </summary>
        private class IntegrationResult
        {
            public bool Success { get; set; }
            public string InstanceId { get; set; }
            public string WebhookUrl { get; set; }
            public string Error { get; set; }
        }
    }
}