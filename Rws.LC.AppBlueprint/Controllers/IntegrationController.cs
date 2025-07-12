using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rws.LC.AppBlueprint.Infrastructure;
using Rws.LC.AppBlueprint.Interfaces;
using Rws.LC.AppBlueprint.Models;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Rws.LC.AppBlueprint.Controllers
{
    [Route("v1/integration")]
    [ApiController]
    /// [Authorize]
    public class IntegrationController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly IAccountService _accountService;

        public IntegrationController(ILogger<IntegrationController> logger, IAccountService accountService)
        {
            _logger = logger;
            _accountService = accountService;
        }

        /// <summary>
        /// Gets the integration credentials and webhook information.
        /// </summary>
        /// <returns>All stored integration data</returns>
        [AllowAnonymous]
        [HttpGet("credentials")]
        public async Task<IActionResult> GetCredentials()
        {
            _logger.LogInformation("Getting integration credentials");

            try
            {
                var credentials = await _accountService.GetIntegrationCredentials().ConfigureAwait(false);
                return Ok(credentials);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error retrieving integration credentials");
                return StatusCode(500, new { error = "Failed to retrieve credentials", message = ex.Message });
            }
        }

        /// <summary>
        /// Saves the webhook URL for integration testing.
        /// </summary>
        /// <param name="request">The webhook save request</param>
        /// <returns>Success response</returns>
        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> SaveWebhook([FromBody] SaveWebhookRequest request)
        {
            _logger.LogInformation("Saving webhook URL: {WebhookUrl}", request.WebhookUrl);

            try
            {
                // Get tenant ID from stored registration since we're testing without Trados Cloud authentication
                var credentials = await _accountService.GetIntegrationCredentials().ConfigureAwait(false);
                var tenantId = credentials.TenantId;
                if (string.IsNullOrEmpty(tenantId))
                {
                    return BadRequest(new { error = "Tenant ID not found in registration" });
                }

                await _accountService.SaveWebhookUrl(tenantId, request.WebhookUrl, CancellationToken.None).ConfigureAwait(false);
                return Ok(new { success = true, message = "Webhook URL saved successfully" });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error saving webhook URL");
                return StatusCode(500, new { error = "Failed to save webhook", message = ex.Message });
            }
        }

        /// <summary>
        /// Renders the integration management UI.
        /// </summary>
        /// <returns>HTML page for managing integration settings</returns>
        [AllowAnonymous]
        [HttpGet("ui")]
        public async Task<IActionResult> IntegrationUI()
        {
            try
            {
                var credentials = await _accountService.GetIntegrationCredentials().ConfigureAwait(false);
                var html = GenerateIntegrationUI(credentials);
                return Content(html, "text/html");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading integration UI");
                var errorHtml = GenerateErrorUI(ex.Message);
                return Content(errorHtml, "text/html");
            }
        }

        private string GenerateIntegrationUI(IntegrationCredentialsModel credentials)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Trados Deployment Addon</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }}
        .container {{ max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background: #0066cc; color: white; padding: 15px; margin: -20px -20px 20px -20px; border-radius: 8px 8px 0 0; }}
        .section {{ margin: 20px 0; padding: 15px; border: 1px solid #ddd; border-radius: 4px; }}
        .credentials {{ background-color: #f8f9fa; }}
        .webhook-form {{ background-color: #fff3cd; }}
        .form-group {{ margin: 10px 0; }}
        label {{ display: block; font-weight: bold; margin-bottom: 5px; }}
        input[type=text] {{ width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }}
        button {{ background: #28a745; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; }}
        button:hover {{ background: #218838; }}
        .btn-view {{ background: #17a2b8; }}
        .btn-view:hover {{ background: #138496; }}
        .status {{ padding: 10px; margin: 10px 0; border-radius: 4px; }}
        .status.complete {{ background: #d4edda; border: 1px solid #c3e6cb; color: #155724; }}
        .status.incomplete {{ background: #f8d7da; border: 1px solid #f5c6cb; color: #721c24; }}
        .cred-item {{ margin: 8px 0; }}
        .cred-label {{ font-weight: bold; display: inline-block; width: 120px; }}
        .cred-value {{ font-family: monospace; background: #e9ecef; padding: 2px 6px; border-radius: 3px; }}
        .masked {{ color: #6c757d; }}
    </style>
</head>
<body>
    <div class='container no-readability'>
        <div class='header'>
            <h1>Trados Deployment Addon</h1>
            <p>Manage your integration credentials and webhook settings</p>
        </div>

        <div class='section credentials'>
            <h2>Stored Credentials</h2>
            <div class='status {(credentials.IsComplete ? "complete" : "incomplete")}'>
                Status: {(credentials.IsComplete ? "Complete - Ready for integration" : "Incomplete - Missing credentials")}
            </div>
            
            <div class='cred-item'>
                <span class='cred-label'>Client ID:</span>
                <span class='cred-value'>{credentials.ClientId ?? "Not available"}</span>
            </div>
            <div class='cred-item'>
                <span class='cred-label'>Client Secret:</span>
                <span class='cred-value masked'>{(string.IsNullOrEmpty(credentials.ClientSecret) ? "Not available" : "*****")}</span>
            </div>
            <div class='cred-item'>
                <span class='cred-label'>Tenant ID:</span>
                <span class='cred-value'>{credentials.TenantId ?? "Not available"}</span>
            </div>
            <div class='cred-item'>
                <span class='cred-label'>Region:</span>
                <span class='cred-value'>{credentials.Region ?? "Not available"}</span>
            </div>
            <div class='cred-item'>
                <span class='cred-label'>Webhook URL:</span>
                <span class='cred-value'>{credentials.WebhookUrl ?? "Not configured"}</span>
            </div>
        </div>

        <div class='section webhook-form'>
            <h2>Configure Webhook</h2>
            <form id='webhookForm'>
                <div class='form-group'>
                    <label for='webhookUrl'>Webhook URL:</label>
                    <input type='text' id='webhookUrl' name='webhookUrl' value='{credentials.WebhookUrl}' 
                           placeholder='https://your-integration-endpoint.com/webhook' required>
                </div>
                <button type='submit'>Save Webhook</button>
                <button type='button' class='btn-view' onclick='showAllCredentials()'>View All Credentials</button>
            </form>
            <div id='result' style='margin-top: 10px;'></div>
        </div>
    </div>

    <script>
        document.getElementById('webhookForm').addEventListener('submit', async function(e) {{
            e.preventDefault();
            
            const webhookUrl = document.getElementById('webhookUrl').value;
            const resultDiv = document.getElementById('result');
            
            try {{
                const response = await fetch('/v1/integration/webhook', {{
                    method: 'POST',
                    headers: {{
                        'Content-Type': 'application/json',
                    }},
                    body: JSON.stringify({{ webhookUrl: webhookUrl }})
                }});
                
                const result = await response.json();
                
                if (response.ok) {{
                    resultDiv.innerHTML = '<div class=""status complete"">✅ Webhook saved successfully!</div>';
                    setTimeout(() => location.reload(), 1500);
                }} else {{
                    resultDiv.innerHTML = '<div class=""status incomplete"">❌ Error: ' + result.message + '</div>';
                }}
            }} catch (error) {{
                resultDiv.innerHTML = '<div class=""status incomplete"">❌ Error: ' + error.message + '</div>';
            }}
        }});

        async function showAllCredentials() {{
            try {{
                const response = await fetch('/v1/integration/credentials');
                const credentials = await response.json();
                
                const popup = window.open('', '_blank', 'width=600,height=500,scrollbars=yes');
            popup.document.write(`
                <html>
                <head>
                <title>Integration Credentials</title>
                <meta name=""robots"" content=""noindex, nofollow"">
                <meta name=""readability-score"" content=""0"">
                <meta property=""op:markup_version"" content=""v1.0"">
                <meta name=""parsely-type"" content=""homepage"">
                <meta name=""clearly-extension"" content=""disabled"">
                <meta name=""evernote"" content=""disabled"">
                <style>
                    body {{ font-family: Arial, sans-serif; padding: 20px; background: #f8f9fa; }}
                    .container {{ background: white; padding: 20px; border-radius: 8px; }}
                    .header {{ color: #0066cc; border-bottom: 2px solid #0066cc; padding-bottom: 10px; }}
                    .cred-item {{ margin: 15px 0; padding: 10px; background: #f8f9fa; border-radius: 4px; }}
                    .cred-label {{ font-weight: bold; color: #495057; }}
                    .cred-value {{ font-family: monospace; background: #e9ecef; padding: 5px 8px; border-radius: 3px; margin-top: 5px; word-break: break-all; }}
                    .complete {{ color: #28a745; }}
                    .incomplete {{ color: #dc3545; }}
                    .clearly-disabled {{ display: block; }}
                </style>
                </head>
                    <body>
                        <div class='container no-readability'>
                            <form autocomplete='off' data-lpignore='true' data-form-type='other'>
                            <h2 class='header'>Complete Integration Credentials</h2>
                            <p class='${{credentials.isComplete ? 'complete' : 'incomplete'}}'>
                                Status: ${{credentials.isComplete ? 'Complete' : 'Incomplete'}}
                            </p>
                            
                            <div class='cred-item'>
                                <div class='cred-label'>Client ID:</div>
                                <div class='cred-value' data-lpignore='true' autocomplete='off'>${{credentials.clientId || 'Not available'}}</div>
                            </div>
                            
                            <div class='cred-item'>
                                <div class='cred-label'>Client Secret:</div>
                                <div class='cred-value' data-lpignore='true' autocomplete='off'>${{credentials.clientSecret || 'Not available'}}</div>
                            </div>
                            
                            <div class='cred-item'>
                                <div class='cred-label'>Tenant ID:</div>
                                <div class='cred-value' data-lpignore='true' autocomplete='off'>${{credentials.tenantId || 'Not available'}}</div>
                            </div>
                            
                            <div class='cred-item'>
                                <div class='cred-label'>Region:</div>
                                <div class='cred-value' data-lpignore='true' autocomplete='off'>${{credentials.region || 'Not available'}}</div>
                            </div>
                            
                            <div class='cred-item'>
                                <div class='cred-label'>Webhook URL:</div>
                                <div class='cred-value' data-lpignore='true' autocomplete='off'>${{credentials.webhookUrl || 'Not configured'}}</div>
                            </div>
                            
                            <p><small>Copy these credentials for your integration setup.</small></p>
                            </form>
                        </div>
                    </body>
                    </html>
                `);
                popup.document.close();
            }} catch (error) {{
                alert('Error loading credentials: ' + error.message);
            }}
        }}
    </script>
</body>
</html>";
        }

        private string GenerateErrorUI(string errorMessage)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Integration Error</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }}
        .error {{ background: #f8d7da; border: 1px solid #f5c6cb; color: #721c24; padding: 15px; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class='container no-readability'>
        <h1>Integration Manager</h1>
        <div class='error'>
            <h3>Error Loading Integration Data</h3>
            <p>{errorMessage}</p>
            <p>Please ensure your add-on is properly registered and installed.</p>
        </div>
    </div>
</body>
</html>";
        }
    }

    // Request model for saving webhook
    public class SaveWebhookRequest
    {
        public string WebhookUrl { get; set; }
    }
}