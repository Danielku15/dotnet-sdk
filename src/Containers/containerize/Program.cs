// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.NET.Build.Containers.Tasks;

namespace containerize;

internal class Program
{
    private static readonly Dictionary<string, Func<CliRootCommand>> CommandFactory = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(CreateNewImage)] = () => new ContainerizeCommand(),
        [nameof(PushImageToRegistry)] = () => throw new NotImplementedException("TODO - create command for pushing")
    };

    private static Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length <= 0 || !CommandFactory.TryGetValue(args[0], out var factory))
            {
                string commands = string.Join(", ", CommandFactory.Keys);
                throw new ArgumentException($"Expected the first argument to be a valid command: {commands}");
            }

            CliRootCommand command = factory();
            return command.Parse(args.Skip(1).ToArray()).InvokeAsync();
        }
        catch (Exception e)
        {
            string message = !e.Message.StartsWith("CONTAINER", StringComparison.OrdinalIgnoreCase) ? $"CONTAINER9000: " + e.ToString() : e.ToString();
            Console.WriteLine($"Containerize: error {message}");

            return Task.FromResult(1);
        }
    }
}
