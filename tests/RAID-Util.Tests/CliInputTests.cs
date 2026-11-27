using System.IO;
using RAID_Util.Helpers;

namespace RAID_Util.Tests;

/// <summary>Serializa los tests que sustituyen la consola estándar.</summary>
[Collection("Environment")]
public sealed class CliInputTests
{
    [Theory]
    [InlineData("1", true, true)]
    [InlineData("2", false, false)]
    [InlineData("0", true, true)]
    [InlineData("0", false, false)]
    public void ReadBooleanAcceptsTheDocumentedOptions(string input, bool current, bool expected)
    {
        var (result, _) = Read(input, current);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadBooleanRejectsInvalidInputAndReprompts()
    {
        var (result, output) = Read("x\n9\n\n2\n", true);

        Assert.False(result);
        Assert.Equal(3, CountOccurrences(output, "Please enter a number from 0 to 2."));
    }

    [Fact]
    public void ReadBooleanOnEofKeepsCurrentValue()
    {
        var (result, output) = Read(string.Empty, false);

        Assert.False(result);
        Assert.DoesNotContain("Please enter a number from 0 to 2.", output);
    }

    private static (bool Result, string Output) Read(string input, bool current)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        try
        {
            using var reader = new StringReader(input);
            using var writer = new StringWriter();
            Console.SetIn(reader);
            Console.SetOut(writer);
            return (CliApp.ReadBoolean("Option", current), writer.ToString());
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
