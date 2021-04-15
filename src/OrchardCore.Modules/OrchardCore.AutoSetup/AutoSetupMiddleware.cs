using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Abstractions.Setup;
using OrchardCore.AutoSetup.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;
using OrchardCore.Setup.Services;

namespace OrchardCore.AutoSetup
{
    /// <summary>
    /// The auto setup middleware.
    /// </summary>
    public class AutoSetupMiddleware
    {
        /// <summary>
        /// The next middleware in the execution pipeline.
        /// </summary>
        private readonly RequestDelegate _next;

        /// <summary>
        /// The Shell host.
        /// </summary>
        private readonly IShellHost _shellHost;

        /// <summary>
        /// The Shell settings.
        /// </summary>
        private readonly ShellSettings _shellSettings;

        /// <summary>
        /// The Shell settings manager.
        /// </summary>
        private readonly IShellSettingsManager _shellSettingsManager;

        /// <summary>
        /// The auto-setup options.
        /// </summary>
        private readonly AutoSetupOptions _options;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<AutoSetupMiddleware> _logger;

        private readonly TenantSetupOptions _setupOptions;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoSetupMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the execution pipeline.</param>
        /// <param name="shellHost">The Shell host.</param>
        /// <param name="shellSettings">The Shell settings.</param>
        /// <param name="shellSettingsManager">The Shell settings manager.</param>
        /// <param name="options">The auto-setup Options.</param>
        /// <param name="logger">The logger.</param>
        public AutoSetupMiddleware(
            RequestDelegate next,
            IShellHost shellHost,
            ShellSettings shellSettings,
            IShellSettingsManager shellSettingsManager,
            IOptions<AutoSetupOptions> options,
            ILogger<AutoSetupMiddleware> logger)
        {
            _next = next;
            _shellHost = shellHost;
            _shellSettings = shellSettings;
            _shellSettingsManager = shellSettingsManager;
            _options = options.Value;
            _logger = logger;

            _setupOptions = _options.Tenants.FirstOrDefault(options => _shellSettings.Name == options.ShellName);
        }

        /// <summary>
        /// The middleware auto-setup invoke.
        /// </summary>
        /// <param name="httpContext">
        /// The http context.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (_setupOptions != null && _shellSettings.State == TenantState.Uninitialized)
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (_shellSettings.State == TenantState.Uninitialized)
                    {
                        var setupService = httpContext.RequestServices.GetRequiredService<ISetupService>();
                        if (await SetupTenantAsync(setupService, _setupOptions, _shellSettings))
                        {
                            if (_setupOptions.IsDefault)
                            {
                                // Create the rest of the Shells for further on demand setup.
                                foreach (var setupOptions in _options.Tenants)
                                {
                                    if (_setupOptions != setupOptions)
                                    {
                                        await CreateTenantSettingsAsync(setupOptions);
                                    }
                                }
                            }

                            var pathBase = httpContext.Request.PathBase;
                            if (!pathBase.HasValue)
                            {
                                pathBase = "/";
                            }

                            httpContext.Response.Redirect(pathBase);

                            return;
                        }
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            await _next.Invoke(httpContext);
        }

        /// <summary>
        /// Setup tenant.
        /// </summary>
        /// <param name="setupService">The setup service.</param>
        /// <param name="setupOptions">The tenant setup options.</param>
        /// <param name="shellSettings">The tenant shell settings.</param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task<bool> SetupTenantAsync(ISetupService setupService, TenantSetupOptions setupOptions, ShellSettings shellSettings)
        {
            var setupContext = await GetSetupContextAsync(setupOptions, setupService, shellSettings);

            _logger.LogInformation("AutoSetup is initializing the site");

            await setupService.SetupAsync(setupContext);

            if (setupContext.Errors.Count == 0)
            {
                _logger.LogInformation($"AutoSetup successfully provisioned the site {setupOptions.SiteName}");

                return true;
            }

            var stringBuilder = new StringBuilder();
            foreach (var error in setupContext.Errors)
            {
                stringBuilder.AppendLine($"{error.Key} : '{error.Value}'");
            }

            _logger.LogError("AutoSetup failed installing the site '{SiteName}' with errors: {Errors}", setupOptions.SiteName, stringBuilder);
            return false;
        }

        /// <summary>
        /// Create tenant shell settings.
        /// </summary>
        /// <param name="setupOptions">The setup options.</param>
        /// <returns>The <see cref="ShellSettings"/>.</returns>
        public async Task<ShellSettings> CreateTenantSettingsAsync(TenantSetupOptions setupOptions)
        {
            var shellSettings = _shellSettingsManager.CreateDefaultSettings();

            shellSettings.Name = setupOptions.ShellName;
            shellSettings.RequestUrlHost = setupOptions.RequestUrlHost;
            shellSettings.RequestUrlPrefix = setupOptions.RequestUrlPrefix;
            shellSettings.State = TenantState.Uninitialized;

            shellSettings["ConnectionString"] = setupOptions.DatabaseConnectionString;
            shellSettings["TablePrefix"] = setupOptions.DatabaseTablePrefix;
            shellSettings["DatabaseProvider"] = setupOptions.DatabaseProvider;
            shellSettings["Secret"] = Guid.NewGuid().ToString();
            shellSettings["RecipeName"] = setupOptions.RecipeName;

            await _shellHost.UpdateShellSettingsAsync(shellSettings);

            return shellSettings;
        }

        /// <summary>
        /// Get setup context from the configuration.
        /// </summary>
        /// <param name="options">The tenant setup options.</param>
        /// <param name="setupService">The setup service.</param>
        /// <param name="shellSettings">The tenant shell settings.</param>
        /// <returns> The <see cref="SetupContext"/>. to setup the site </returns>
        private static async Task<SetupContext> GetSetupContextAsync(TenantSetupOptions options, ISetupService setupService, ShellSettings shellSettings)
        {
            var recipes = await setupService.GetSetupRecipesAsync();

            var recipe = recipes.SingleOrDefault(r => r.Name == options.RecipeName);

            var setupContext = new SetupContext
            {
                Recipe = recipe,
                ShellSettings = shellSettings,
                Errors = new Dictionary<string, string>()
            };

            setupContext.Properties[SetupConstants.AdminEmail] = options.AdminEmail;
            setupContext.Properties[SetupConstants.AdminPassword] = options.AdminPassword;
            setupContext.Properties[SetupConstants.AdminUsername] = options.AdminUsername;
            setupContext.Properties[SetupConstants.DatabaseConnectionString] = options.DatabaseConnectionString;
            setupContext.Properties[SetupConstants.DatabaseProvider] = options.DatabaseProvider;
            setupContext.Properties[SetupConstants.DatabaseTablePrefix] = options.DatabaseTablePrefix;
            setupContext.Properties[SetupConstants.SiteName] = options.SiteName;
            setupContext.Properties[SetupConstants.SiteTimeZone] = options.SiteTimeZone;

            return setupContext;
        }
    }
}
