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
using Microsoft.Extensions.Configuration;

namespace MyNextBookBackEnd.Tests
{
    public class RefreshSeriesTests
    {
        [Fact]
        public async Task RefreshSeries_WithValidData_ReturnsOk()
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = "https://41990b90035138cb0a9dbdb374ca61e2@o4507073550155776.ingest.us.sentry.io/4507073559396352";
                options.Debug = true;
                options.AutoSessionTracking = true;
                options.IsGlobalModeEnabled = false;
                options.TracesSampleRate = 0.1;
            });

            SentrySdk.CaptureMessage("Start azure function refreshseries");
            Debug.WriteLine("Running RefreshSeries_WithValidData_ReturnsOk test");

           
            var loggerMock = new Mock<ILogger<RefreshSeries>>();

            var refreshSeries = new RefreshSeries(loggerMock.Object);

      

            var projectDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dataFilePath = Path.Combine(projectDir, "data", "TestDataPitt.json");

            var jsonData = await File.ReadAllTextAsync(dataFilePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var data = JsonSerializer.Deserialize<SeriesToRefreshData>(jsonData, options);

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

            response.Body.Position = 0;
            string responseBody = await new StreamReader(response.Body).ReadToEndAsync();

            var result = JsonSerializer.Deserialize<SeriesToRefreshData>(responseBody, options);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Response not OK: {responseBody}");
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            var missingBooksCount = result.booksMissingFromSeries.Count;
            Console.WriteLine($"Number of missing books: {missingBooksCount}");
            Debug.WriteLine($"Number of missing books: {missingBooksCount}");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }
}
