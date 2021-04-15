using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;
using OrchardCore.Google.Authentication.Services;
using OrchardCore.Google.Authentication.Settings;

namespace OrchardCore.Google.Authentication.Configuration
{
    public class GoogleOptionsConfiguration :
        IConfigureOptions<AuthenticationOptions>,
        IConfigureNamedOptions<GoogleOptions>
    {
        private readonly GoogleAuthenticationService _googleAuthenticationService;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ShellSettings _shellSettings;
        private readonly ILogger _logger;

        public GoogleOptionsConfiguration(
            GoogleAuthenticationService googleAuthenticationService,
            IDataProtectionProvider dataProtectionProvider,
            ShellSettings shellSettings,
            ILogger<GoogleOptionsConfiguration> logger)
        {
            _googleAuthenticationService = googleAuthenticationService;
            _dataProtectionProvider = dataProtectionProvider;
            _shellSettings = shellSettings;
            _logger = logger;
        }

        public void Configure(AuthenticationOptions options)
        {
            var settings = GetGoogleAuthenticationSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            options.AddScheme(GoogleDefaults.AuthenticationScheme, builder =>
            {
                builder.DisplayName = "Google";
                builder.HandlerType = typeof(GoogleHandler);
            });
        }

        public void Configure(string name, GoogleOptions options)
        {
            if (!String.Equals(name, GoogleDefaults.AuthenticationScheme))
            {
                return;
            }

            var settings = GetGoogleAuthenticationSettingsAsync().GetAwaiter().GetResult();
            if (settings == null)
            {
                return;
            }

            options.ClientId = settings.ClientID;
            try
            {
                options.ClientSecret = _dataProtectionProvider.CreateProtector(GoogleConstants.Features.GoogleAuthentication).Unprotect(settings.ClientSecret);
            }
            catch
            {
                _logger.LogError("The Consumer Secret could not be decrypted. It may have been encrypted using a different key.");
            }

            if (settings.CallbackPath.HasValue)
            {
                options.CallbackPath = settings.CallbackPath;
            }

            options.SaveTokens = settings.SaveTokens;
        }

        public void Configure(GoogleOptions options) => Debug.Fail("This infrastructure method shouldn't be called.");

        private async Task<GoogleAuthenticationSettings> GetGoogleAuthenticationSettingsAsync()
        {
            var settings = await _googleAuthenticationService.GetSettingsAsync();
            if (!_googleAuthenticationService.CheckSettings(settings))
            {
                if (_shellSettings.State == TenantState.Running)
                {
                    _logger.LogWarning("Google Authentication is not correctly configured.");
                }

                return null;
            }

            return settings;
        }
    }
}
