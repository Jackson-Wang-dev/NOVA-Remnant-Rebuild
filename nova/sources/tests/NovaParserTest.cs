using System.Linq;
using Chickensoft.GoDotTest;
using Godot;
using Nova.Parser;
using Shouldly;

namespace Nova.Tests;

/// <summary>
/// Regression coverage for ParseCodeBlockWithAttributes: a non-empty `[key=value, ...]` attribute
/// list never actually terminated (the closing "]" was consumed before being checked for, so the
/// loop always tried to parse one more key and threw "Expect identifier or string, found
/// BlockStart" on whatever followed). Only the empty-list case `[]` happened to work, which is why
/// this went unnoticed - novascript-reference.md's own `[stage="after_dialogue"]` example never
/// actually parsed until this was fixed. NovaParser.ParseBlocks is pure string-in/struct-out, no
/// live singletons needed, but GoDotTest's TestClass still requires the Node constructor parameter.
/// </summary>
public class NovaParserTest : TestClass
{
    public NovaParserTest(Node testScene) : base(testScene) { }

    [Test]
    public void ParsesSingleAttributeAndResumesAfterClosingBracket()
    {
        var blocks = NovaParser.ParseBlocks("[stage=\"after_dialogue\"]<|\nfoo()\n|>\n下一句话\n");

        var lazyBlock = blocks.First(b => b.Type == BlockType.LazyExecution);
        lazyBlock.Attributes["stage"].ShouldBe("after_dialogue");

        blocks.ShouldContain(b => b.Type == BlockType.Text && b.Content == "下一句话");
    }

    [Test]
    public void ParsesMultipleAttributes()
    {
        var blocks = NovaParser.ParseBlocks("[stage=\"after_dialogue\", foo=\"bar\"]<|\nbaz()\n|>\n");

        var lazyBlock = blocks.First(b => b.Type == BlockType.LazyExecution);
        lazyBlock.Attributes["stage"].ShouldBe("after_dialogue");
        lazyBlock.Attributes["foo"].ShouldBe("bar");
    }

    [Test]
    public void ParsesEmptyAttributeList()
    {
        var blocks = NovaParser.ParseBlocks("[]<|\nfoo()\n|>\n");

        var lazyBlock = blocks.First(b => b.Type == BlockType.LazyExecution);
        lazyBlock.Attributes.Count.ShouldBe(0);
    }
}
