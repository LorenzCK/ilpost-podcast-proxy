using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging((context, logging) => {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .ConfigureServices((context, services) => {
        services.AddSingleton<IlPostPodcastProxy.IlPostClient>();
        services.AddTransient<IlPostPodcastProxy.RestSharpBodyDumperInterceptor>();
    })
    .Build();

using (host) {
    var ilPostClient = host.Services.GetRequiredService<IlPostPodcastProxy.IlPostClient>();
    await ilPostClient.LoginAsync();
    var podcast = host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Podcast"] ?? "morning";
    Console.WriteLine(await ilPostClient.GetEnrichedFeedAsync(podcast));
}
