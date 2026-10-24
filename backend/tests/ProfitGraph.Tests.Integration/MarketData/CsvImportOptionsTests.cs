using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.ImportCandles;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.MarketData;

public sealed class CsvImportOptionsTests
{
    private const string Csv = "openTime,open,high,low,close,volume\n" +
        "2026-01-01T00:00:00Z,100,110,90,105,1\n2026-01-01T01:00:00Z,100,110,90,105,1\n2026-01-01T02:00:00Z,100,110,90,105,1\n";

    [Test]
    public async Task ReloadAppliesToNewScope_AndKeepsActiveImportSnapshot()
    {
        await using var application = new ProfitGraphApiFactory();
        var configuration = (IConfigurationRoot)application.Services.GetRequiredService<IConfiguration>();
        using IServiceScope active = application.Services.CreateScope();
        var original = active.ServiceProvider.GetRequiredService<CsvImportOptions>();
        var originalReader = active.ServiceProvider.GetRequiredService<ICsvCandleReader>();

        configuration["CsvImport:BatchSize"] = "2";
        configuration["CsvImport:MaxRows"] = "2";
        configuration["CsvImport:MaxDetailedErrors"] = "1";
        configuration.Reload();

        active.ServiceProvider.GetRequiredService<CsvImportOptions>().ShouldBeSameAs(original);
        using IServiceScope next = application.Services.CreateScope();
        var updated = next.ServiceProvider.GetRequiredService<CsvImportOptions>();
        updated.ShouldBeSameAs(application.Services.GetRequiredService<IOptionsMonitor<CsvImportOptions>>().CurrentValue);
        updated.MaxRows.ShouldBe(2);

        using var oldStream = new MemoryStream(Encoding.UTF8.GetBytes(Csv));
        (await originalReader.Read(oldStream, CancellationToken.None).ToListAsync()).Count.ShouldBe(3);
        using var newStream = new MemoryStream(Encoding.UTF8.GetBytes(Csv));
        await Should.ThrowAsync<ApplicationValidationException>(async () =>
            await next.ServiceProvider.GetRequiredService<ICsvCandleReader>().Read(newStream, CancellationToken.None).ToListAsync());
    }

    [Test]
    public void InconsistentLimitsStopTheApplication()
    {
        var application = new ProfitGraphApiFactory(new Dictionary<string, string> { ["CsvImport:BatchSize"] = "10", ["CsvImport:MaxRows"] = "5" });

        Should.Throw<OptionsValidationException>(application.CreateClient);
        application.Dispose();
    }
}