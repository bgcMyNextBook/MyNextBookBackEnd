// File: MyNextBookBackEnd/RefreshSeries.cs
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;

using System.Threading.Tasks;

using Azure.AI.OpenAI;
using OpenAI.Chat;
using Azure;
using Newtonsoft.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using MyNextBookBackEnd.Models;
using Azure.AI.OpenAI.Chat;
using System.Runtime.Versioning;
using Azure.Identity;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Sentry.Protocol;
using static System.Net.WebRequestMethods;
using System.Net;
using System.Security;
using Azure.Security.KeyVault.Secrets;

namespace MyNextBookBackEnd
{
    public class RefreshSeries
    {
        private readonly ILogger<RefreshSeries> _logger;

        public RefreshSeries(ILogger<RefreshSeries> logger)
        {
            _logger = logger;
        }

        [Function("RefreshSeries")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            SentrySdk.Init(options =>
            {
                // A Sentry Data Source Name (DSN) is required.
                // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
                options.Dsn = "https://41990b90035138cb0a9dbdb374ca61e2@o4507073550155776.ingest.us.sentry.io/4507073559396352";

                // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
                // This might be helpful, or might interfere with the normal operation of your application.
                // We enable it here for demonstration purposes when first trying Sentry.
                // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
                options.Debug = true;

                // This option is recommended. It enables Sentry's "Release Health" feature.
                options.AutoSessionTracking = true;

                // Enabling this option is recommended for client applications only. It ensures all threads use the same global scope.
                options.IsGlobalModeEnabled = false;

                // Example sample rate for your transactions: captures 10% of transactions
                options.TracesSampleRate = 0.1;
            });
            // Arrange
            SentrySdk.CaptureMessage("Start azure function refreshseries");
            // Deserialize the request body into SeriesToRefreshData
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<SeriesToRefreshData>(requestBody);

            // Process the data object
            return await ProcessSeriesDataAsync(data, req);
        }

        public async Task<HttpResponseData> ProcessSeriesDataAsync(SeriesToRefreshData data, HttpRequestData req)
        {



            SentrySdk.Init(options =>
            {
                // A Sentry Data Source Name (DSN) is required.
                // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
                options.Dsn = "https://41990b90035138cb0a9dbdb374ca61e2@o4507073550155776.ingest.us.sentry.io/4507073559396352";

                // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
                // This might be helpful, or might interfere with the normal operation of your application.
                // We enable it here for demonstration purposes when first trying Sentry.
                // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
                options.Debug = true;

                // This option is recommended. It enables Sentry's "Release Health" feature.
                options.AutoSessionTracking = true;

                // Enabling this option is recommended for client applications only. It ensures all threads use the same global scope.
                options.IsGlobalModeEnabled = false;

                // Example sample rate for your transactions: captures 10% of transactions
                options.TracesSampleRate = 0.1;
            });
            // Arrange
            SentrySdk.CaptureMessage("Start azure function refreshSeries");
            // Validate the data object
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(data);
            HttpResponseData? response = null;
            if (!Validator.TryValidateObject(data, validationContext, validationResults, true))
            {
                // Return Bad Request with validation errors
                HttpResponseData httpResponseData = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                //HttpResponseData? response = httpResponseData;
                await response.WriteAsJsonAsync(validationResults);
                return response;
            }

            // Perform processing logic here
            _logger.LogInformation($"Processing series: {data.Name}");

            try
            {
                SeriesToRefreshData returnValue = await Refresh(data);
                response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                string json = JsonConvert.SerializeObject(returnValue);
                await response.WriteStringAsync(json);
                return response;
            }
            catch (Exception ex)
            {
                response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await response.WriteStringAsync(ex.ToString());
                return response;
            }
        }
        //string endPoint = "https://ai-prototypes-vision.openai.azure.com";///openai/deployments/gpt-4o/chat/completions?api-version=2024-08-01-preview";
        //"https://ai-prototypes-vision.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-15-preview"  

        //https://ai-prototypes-vision.openai.azure.com/
        //https://ai-prototypes-vision.openai.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-08-01-preview
        string modelDeployment = "gpt-4o";

        public async Task<SeriesToRefreshData> Refresh(SeriesToRefreshData data)
        {
            try
            {
                // *******  CREATE AI CLIENT AND CONFIGURE PROMPT ********

                DefaultAzureCredential credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    Diagnostics = { IsLoggingContentEnabled = true, IsLoggingEnabled = true },
                    ExcludeEnvironmentCredential = true,
                    ExcludeManagedIdentityCredential = true,
                    ExcludeSharedTokenCacheCredential = true,
                    ExcludeInteractiveBrowserCredential = true,
                    ExcludeAzurePowerShellCredential = true,
                    ExcludeVisualStudioCodeCredential = false,
                    ExcludeAzureCliCredential = false
                });

                // Get secrets from Azure Key Vault
                var keyVaultClient = new SecretClient(new Uri("https://kv-hubbrady438297829401.vault.azure.net/"), credential);
                KeyVaultSecret apiKeySecret = await keyVaultClient.GetSecretAsync("OpenAIApiKey");
                KeyVaultSecret endPointSecret = await keyVaultClient.GetSecretAsync("OpenAIEndPoint");
                KeyVaultSecret modelDeploymentSecret = await keyVaultClient.GetSecretAsync("OpenAIModelDeployment");

                string apiKey = apiKeySecret.Value;
                string endPoint = endPointSecret.Value;
                string modelDeployment = modelDeploymentSecret.Value;

                AzureOpenAIClient openAIClient = new AzureOpenAIClient(new Uri(endPoint), new ApiKeyCredential(apiKey));
                OpenAI.Chat.ChatClient chatClient = openAIClient.GetChatClient(modelDeployment);

                OpenAI.Chat.ChatCompletionOptions options = new();
                options.Temperature = 0.1f;
                options.TopP = 1.0f;
                options.MaxOutputTokenCount = 14000;
                options.ResponseFormat = StructuredOutputsExtensions.CreateJsonSchemaFormat<SeriesToRefreshData>("SeriesToRefreshData", jsonSchemaIsStrict: true);
                // learning guid will throw an error in CompleteChatAsync

                List<OpenAI.Chat.ChatMessage> messages = new List<OpenAI.Chat.ChatMessage>();

                messages.Add(new SystemChatMessage("You are a librarian that helps to identify books that are missing from book series"));
                string json = JsonConvert.SerializeObject(data);
                messages.Add(new UserChatMessage($"given the following book series information {json} find books that should be in the series but are not. return those books in the schema format in the booksMissingFromSeries leaving sysMynbIDAsString as an empty string."));

                ClientResult<OpenAI.Chat.ChatCompletion>? completionResults = await chatClient.CompleteChatAsync(messages, options);
                if (completionResults == null)
                {
                    _logger.LogInformation("No completion results returned");
                    throw new Exception("No completion results returned");
                }
                string jsonResults = completionResults.Value.Content[0].Text;// completionResults.Value.Content[0].Text.Replace("booksMissingFromSeries", "booksMissingFromSeries");
                SeriesToRefreshData returnValue = JsonConvert.DeserializeObject<SeriesToRefreshData>(jsonResults);
                return returnValue;
            }

            catch (Exception ex)
            {
                _logger.LogInformation(ex.ToString());
                throw;
            }
        }
    }
}