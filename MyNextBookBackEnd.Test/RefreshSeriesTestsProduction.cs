using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using MyNextBookBackEnd.Models;
using System.Diagnostics;
using System.Reflection;

namespace MyNextBookBackEnd.Tests
{
    public class RefreshSeriesTestsProduction
    {
        // ... existing code ...

        [Fact]
        [Trait("Category", "Integration")]
        public async Task RefreshSeries_CallProductionService_WithValidData_ReturnsOk()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<RefreshSeriesTestsProduction>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            string productionUrl = configuration["ProductionFunctionUrl"];
            if (string.IsNullOrEmpty(productionUrl))
            {
                throw new InvalidOperationException("ProductionFunctionUrl is not set in configuration.");
            }

            var projectDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dataFilePath = Path.Combine(projectDir, "data", "TestDataPitt.json");
            var jsonData = await File.ReadAllTextAsync(dataFilePath);

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            // Act
  
            var response = await httpClient.PostAsync(productionUrl, content);

            var responseBody = await response.Content.ReadAsStringAsync();
            // Assert
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);


            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = JsonSerializer.Deserialize<SeriesToRefreshData>(responseBody, options);

            var missingBooksCount = result.booksMissingFromSeries?.Count ?? 0;
            Console.WriteLine($"Number of missing books: {missingBooksCount}");
            Debug.WriteLine($"Number of missing books: {missingBooksCount}");
        }
    }
}
