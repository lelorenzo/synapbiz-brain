using Synapbiz.Brain.Mcp.Tool;

using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
return await BrainMcpProgram.RunAsync(args, input, output, Console.Error, CancellationToken.None).ConfigureAwait(false);
