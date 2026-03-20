using System.CommandLine;
using Sts2Analytics.Cli.Commands;

var root = new RootCommand("STS2 Analytics — run data analysis tool");
root.Add(ImportCommand.Create());
root.Add(StatsCommand.Create());
root.Add(CardsCommand.Create());
root.Add(RelicsCommand.Create());
root.Add(RatingCommand.Create());
root.Add(RunCommand.Create());
root.Add(ExportCommand.Create());
return await root.Parse(args).InvokeAsync();
