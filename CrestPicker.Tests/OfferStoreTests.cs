using System.Collections.Generic;
using System.IO;
using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class OfferStoreTests
{
    [Fact]
    public void BrokenJson_GivesEmptyDictionary()
    {
        Assert.False(OfferStore.Load("{ not json", out Dictionary<string, bool> slots));
        Assert.Empty(slots);
    }

    [Fact]
    public void JsonRoundTrip_KeepsSlots()
    {
        Dictionary<string, bool> source = new Dictionary<string, bool> { ["7"] = true, ["3"] = false };

        Assert.True(OfferStore.Load(OfferStore.Serialize(source), out Dictionary<string, bool> back));

        Assert.True(back["7"]);
        Assert.False(back["3"]);
        Assert.True(OfferStore.IsOffered(back, 7));
        Assert.False(OfferStore.IsOffered(back, 3));
    }

    [Fact]
    public void NegativeSlot_IsNeverWritten()
    {
        Dictionary<string, bool> slots = new Dictionary<string, bool>();

        OfferStore.Mark(slots, -1);

        Assert.Empty(slots);
        Assert.False(OfferStore.IsOffered(slots, -1));
    }

    [Fact]
    public void Mark_ThenIsOffered_IsTrue()
    {
        Dictionary<string, bool> slots = new Dictionary<string, bool>();

        OfferStore.Mark(slots, 4);

        Assert.True(OfferStore.IsOffered(slots, 4));
        Assert.False(OfferStore.IsOffered(slots, 5));
    }

    [Fact]
    public void FileRoundTrip_KeepsSlots()
    {
        string path = Path.Combine(Path.GetTempPath(), "crestpicker-tests", "state.json");
        Dictionary<string, bool> slots = new Dictionary<string, bool>();
        OfferStore.Mark(slots, 2);

        Assert.True(OfferStore.SaveFile(path, slots));
        Assert.True(OfferStore.LoadFile(path, out Dictionary<string, bool> back));
        Assert.True(OfferStore.IsOffered(back, 2));
    }
}
