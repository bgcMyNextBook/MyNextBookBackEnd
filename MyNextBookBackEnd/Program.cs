using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Sentry.Extensions.Logging.Extensions.DependencyInjection;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

DefaultAzureCredential credential = new DefaultAzureCredential();


// Get secrets from Azure Key Vault
string vaultUri = "https://kv-mynextbook.vault.azure.net/";

if (string.IsNullOrEmpty(vaultUri))
{
    throw new InvalidOperationException("Vault URI is not configured.");
}
var keyVaultClient = new SecretClient(new Uri(vaultUri), credential);

KeyVaultSecret sentryDSNSecret = await keyVaultClient.GetSecretAsync("sentryDSN");

SentrySdk.Init(options =>
{
    // A Sentry Data Source Name (DSN) is required.
    // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
    // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
    options.Dsn = sentryDSNSecret.Value;

    // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
    // This might be helpful, or might interfere with the normal operation of your application.
    // We enable it here for demonstration purposes when first trying Sentry.
    // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
    options.Debug = false;

    // This option is recommended. It enables Sentry's "Release Health" feature.
    options.AutoSessionTracking = true;

    // Enabling this option is recommended for client applications only. It ensures all threads use the same global scope.
    options.IsGlobalModeEnabled = false;

    // Example sample rate for your transactions: captures 10% of transactions
    options.TracesSampleRate = 0.1;
});

builder.Build().Run();
