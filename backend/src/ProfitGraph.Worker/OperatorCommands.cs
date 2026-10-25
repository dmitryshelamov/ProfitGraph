using ProfitGraph.Application.Common.Operations;

namespace ProfitGraph.Worker;

/// <summary>
///     <c>--run {name}</c> runs one operator command with stdin and stdout and exits with its code; the Worker then starts
///     no background service and applies no migration (ADR 0022).
/// </summary>
internal static class OperatorCommands
{
    private const string Switch = "--run";

    public static string? Requested(string[] args)
    {
        int index = Array.IndexOf(args, Switch);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static async Task<int> Run(IServiceProvider services, string name, TextReader input, TextWriter output)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IOperatorCommand? command = scope.ServiceProvider.GetServices<IOperatorCommand>().SingleOrDefault(candidate => candidate.Name == name);

        if (command is null)
        {
            await output.WriteLineAsync($"Unknown command '{name}'.");

            return 64;
        }

        return await command.Run(input, output, CancellationToken.None);
    }
}