switch (args.FirstOrDefault())
{
    case "wait": await Task.Delay(TimeSpan.FromSeconds(30)); break;
    case "flood": Console.Write(new string('x', 1_100_000)); break;
    case "echo": Console.Write(await Console.In.ReadToEndAsync()); Console.Error.Write("diagnostic"); break;
    default: return 7;
}
return 0;
