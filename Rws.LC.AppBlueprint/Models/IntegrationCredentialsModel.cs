namespace Rws.LC.AppBlueprint.Models
{
    public class IntegrationCredentialsModel
    {
        /// <summary>
        /// The Trados Cloud Client ID.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// The Trados Cloud Client Secret.
        /// </summary>
        public string ClientSecret { get; set; }

        /// <summary>
        /// The Trados Cloud Tenant ID.
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// The webhook URL for integration.
        /// </summary>
        public string WebhookUrl { get; set; }

        /// <summary>
        /// The region for this tenant.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Indicates if all required credentials are present.
        /// </summary>
        public bool IsComplete => !string.IsNullOrEmpty(ClientId) &&
                                  !string.IsNullOrEmpty(ClientSecret) &&
                                  !string.IsNullOrEmpty(TenantId);
    }
}