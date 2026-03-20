using System.CommandLine;
using Sts2Analytics.Cli.Commands;

var root = new RootCommand("STS2 Analytics — run data analysis tool");
root.Add(ImportCommand.Create());
return await root.Parse(args).InvokeAsync();
