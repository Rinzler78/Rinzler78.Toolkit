namespace Rinzler78.Sample;

/// <summary>The application's entry point.</summary>
internal static class Program
{
    /// <summary>Runs the application.</summary>
    /// <param name="args">The command line arguments.</param>
    internal static void Main(string[] args) => Console.WriteLine(Describe(args));

    /// <summary>
    /// Describes what the application was asked to do. Separated from
    /// <see cref="Main(string[])"/> because an entry point cannot be unit tested and
    /// a description can: the seam is the point, not the message.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>A single line describing the request.</returns>
    internal static string Describe(string[] args) =>
        args.Length == 0 ? "nothing to do" : $"asked for: {string.Join(' ', args)}";
}
