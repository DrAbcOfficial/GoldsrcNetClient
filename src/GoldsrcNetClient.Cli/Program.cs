using CliFx;

namespace GoldsrcNetClient.Cli;

public static class Program
{
    public static async Task<int> Main() =>
        await new CommandLineApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .SetTitle("GoldsrcNetClient.Cli")
            .SetDescription("GoldSrc network client CLI")
            .SetVersion("1.0.0")
            .Build()
            .RunAsync();
}
