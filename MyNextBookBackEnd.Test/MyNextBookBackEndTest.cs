using System.Reflection;
using System.Text.Json;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyNextBookBackEnd.Models;
using Moq;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace MyNextBookBackEnd.Tests
{
    public class RefreshSeriesTests
    {
        [Fact]
        public async Task RefreshSeries_WithValidData_ReturnsOk()
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
            SentrySdk.CaptureMessage("Start azure function refreshseries");
            Debug.WriteLine("Running RefreshSeries_WithValidData_ReturnsOk test");

            var loggerMock = new Mock<ILogger<RefreshSeries>>();
            var refreshSeries = new RefreshSeries(loggerMock.Object);

            // Get the path to the data file
            var projectDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dataFilePath = Path.Combine(projectDir, "data", "TestDataPitt.json");

            // Read the JSON file
            var jsonData = await File.ReadAllTextAsync(dataFilePath);

            // Deserialize into SeriesToRefreshData
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var data = JsonSerializer.Deserialize<SeriesToRefreshData>(jsonData, options);

            // Create a mock HttpRequestData
            var context = new Mock<FunctionContext>();
            var reqMock = new Mock<HttpRequestData>(context.Object);
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(jsonData);
            writer.Flush();
            stream.Position = 0;
            reqMock.Setup(r => r.Body).Returns(stream);

            reqMock.Setup(r => r.CreateResponse()).Returns(() =>
            {
                var responseMock = new Mock<HttpResponseData>(context.Object);
                responseMock.SetupProperty(r => r.StatusCode);
                responseMock.SetupProperty(r => r.Body, new MemoryStream());
                return responseMock.Object;
            });

            // Act
            var logMessages = new List<string>();

            loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel level, EventId eventId, object state, Exception exception, Delegate formatter) =>
            {
                var message = formatter.DynamicInvoke(state, exception);
                logMessages.Add(message.ToString());
                Debug.WriteLine(message.ToString());
            });

       
            var response = await refreshSeries.ProcessSeriesDataAsync(data, reqMock.Object);

            // Read the response body stream
            response.Body.Position = 0; // Reset the stream position to the beginning
            string responseBody = await new StreamReader(response.Body).ReadToEndAsync();

            // Deserialize the response body into SeriesToRefreshData
            // Deserialize the response body into SeriesToRefreshData
            var result = JsonSerializer.Deserialize<SeriesToRefreshData>(responseBody, options);

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Response not OK: {responseBody}");
            }

            // Assert
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Response not OK: {responseBody}");
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            // Write the number of missing books to console and test window
            var missingBooksCount = result.booksMissingFromSeries.Count;
            Console.WriteLine($"Number of missing books: {missingBooksCount}");
            Debug.WriteLine($"Number of missing books: {missingBooksCount}");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
       
        }
    }
}
