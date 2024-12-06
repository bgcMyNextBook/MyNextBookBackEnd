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
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace MyNextBookBackEnd
{
    public class RefreshSeries
    {
        private readonly ILogger<RefreshSeries> _logger;
        private readonly IConfiguration _configuration;

        public RefreshSeries(ILogger<RefreshSeries> logger)
        {
            _logger = logger;
            //_configuration = configuration;
        }

        [Function("RefreshSeries")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
       
            // Deserialize the request body into SeriesToRefreshData
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<SeriesToRefreshData>(requestBody);

            // Process the data object
            return await ProcessSeriesDataAsync(data, req);
        }

        public async Task<HttpResponseData> ProcessSeriesDataAsync(SeriesToRefreshData data, HttpRequestData req)
        {
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
                await response.WriteStringAsync(ex.Message);
                return response;
            }
        }


        public async Task<SeriesToRefreshData> Refresh(SeriesToRefreshData data)
        {
            try
            {


                DefaultAzureCredential credential = new DefaultAzureCredential();

                // Get secrets from Azure Key Vault
                string vaultUri = "https://kv-mynextbook.vault.azure.net/";

                if (string.IsNullOrEmpty(vaultUri))
                {
                    throw new InvalidOperationException("Vault URI is not configured.");
                }
                var keyVaultClient = new SecretClient(new Uri(vaultUri), credential);
                KeyVaultSecret apiKeySecret = await keyVaultClient.GetSecretAsync("OpenAIApiKey");
                KeyVaultSecret endPointSecret = await keyVaultClient.GetSecretAsync("OpenAIEndPoint");
                KeyVaultSecret modelDeploymentSecret = await keyVaultClient.GetSecretAsync("OpenAIModelDeployment");

                string apiKey = apiKeySecret.Value;
                string endPoint = endPointSecret.Value;
                string modelDeployment = modelDeploymentSecret.Value;

                // *******  CREATE AI CLIENT AND CONFIGURE PROMPT ********
                AzureOpenAIClient openAIClient = new AzureOpenAIClient(new Uri(endPoint), new ApiKeyCredential(apiKey));
                OpenAI.Chat.ChatClient chatClient = openAIClient.GetChatClient(modelDeployment);
                OpenAI.Chat.ChatCompletionOptions options = new();
                options.Temperature = 0.1f;
                options.TopP = 1.0f;
                options.MaxOutputTokenCount = 14000;
                options.ResponseFormat = StructuredOutputsExtensions.CreateJsonSchemaFormat<SeriesToRefreshData>("SeriesToRefreshData",
                    jsonSchemaIsStrict: true, logger: _logger);

                // *******  CALL AI ********
                List<OpenAI.Chat.ChatMessage> messages = new List<OpenAI.Chat.ChatMessage>();
                messages.Add(new SystemChatMessage("You are a librarian that helps to identify books that are missing from book series"));
                messages.Add(new UserChatMessage($"given the following book series information {JsonConvert.SerializeObject(data)} find books that should be in the series but are not. return those books in the schema format in the booksMissingFromSeries leaving sysMynbIDAsString as an empty string."));


                ClientResult<OpenAI.Chat.ChatCompletion>? completionResults = await chatClient.CompleteChatAsync(messages, options);

                // *******  HANDLE AI RESULTS ********

                //** NO RESULTS
                if (completionResults == null)
                {
                    _logger.LogInformation("No completion results returned");
                    throw new Exception("No completion results returned");
                }

                //** USEABLE RESULTS
                if (completionResults.Value.FinishReason == ChatFinishReason.Stop)
                {
                    string jsonResults = completionResults.Value.Content[0].Text;
                    SeriesToRefreshData? returnValue = JsonConvert.DeserializeObject<SeriesToRefreshData>(jsonResults);
                    return returnValue;
                }

                //** CONTENT FILTERED RESULTS
                if (completionResults.Value.FinishReason == ChatFinishReason.ContentFilter)
                {
                    _logger.LogInformation("Chat content filtered");
#pragma warning disable AOAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    ResponseContentFilterResult cfr = AzureChatExtensions.GetResponseContentFilterResult(completionResults);
#pragma warning restore AOAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

                    _logger.LogInformation($"Chat content filtered: {cfr}");
                    throw new Exception($"Chat content filtered: {cfr.ToString()}");
                }

                //** ERROR RESULTS
                _logger.LogInformation($"Chat did not finish: {completionResults.Value.FinishReason.ToString()}");
                throw new Exception($"Chat did not finish: {completionResults.Value.FinishReason.ToString()}");
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.ToString());
                throw;
            }
        }
    }
}