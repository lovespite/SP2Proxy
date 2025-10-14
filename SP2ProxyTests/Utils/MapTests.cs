using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace SP2Proxy.Utils.Tests;

[TestClass()]
public class MapTests
{
    [TestMethod]
    public void NewMapTest()
    {
        var map = new Map()
            .Set("1", "str1")
            .Set("2", (byte)123)
            .Set("3", (short)-2345)
            .Set("4", (ushort)34567)
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("7", (long)-67890123)
            .Set("8", (ulong)78901234)
            .Set("9", 3.14159f)
            .Set("10", 2.718281828459045d)
            .Set("11", 2718281828459045m)
            .Set("12", true)
            .Set("13", false)
            .Set("14", [1, 2, 3, 4, 5]);


        Assert.AreEqual("str1", map.Get<string>("1").Value);
        Assert.AreEqual((byte)123, map.Get<byte>("2").Value);
        Assert.AreEqual((short)-2345, map.Get<short>("3").Value);
        Assert.AreEqual((ushort)34567, map.Get<ushort>("4").Value);
        Assert.AreEqual((int)-456789, map.Get<int>("5").Value);
        Assert.AreEqual((uint)567890, map.Get<uint>("6").Value);
        Assert.AreEqual((long)-67890123, map.Get<long>("7").Value);
        Assert.AreEqual((ulong)78901234, map.Get<ulong>("8").Value);
        Assert.AreEqual(3.14159f, map.Get<float>("9").Value);
        Assert.AreEqual(2.718281828459045d, map.Get<double>("10").Value);
        Assert.AreEqual(2718281828459045m, map.Get<decimal>("11").Value);
        Assert.AreEqual(true, map.Get<bool>("12").Value);
        Assert.AreEqual(false, map.Get<bool>("13").Value);


        var bytes = map.Get<byte[]>("14");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, bytes.Value);

        Assert.IsFalse(map.Get<int>("nonexistent").HasValue);
    }

    [TestMethod]
    public void LoopReferenceDetectTest()
    {
        var root = new Map();

        Assert.ThrowsException<ArgumentException>(() =>
        {
            root.Set("self", root);
        });

        var a = new Map();
        var b = new Map();
        var c = new Map();

        // a -> b -> c -> a (circular reference)
        a.Set("a", b);
        b.Set("c", c);

        Assert.ThrowsException<ArgumentException>(() =>
        {
            c.Set("a", a); // This should throw an exception due to circular reference
        });

        root.Set("a", a); // This should be fine
        b.SetPath("root", root); // This can bypass the loop-reference check

        // root -> a -> b -> c
        //              ↓
        //             root (circular reference through path)
        Assert.ThrowsException<ArgumentException>(() =>
        {
            root.SerializeAsBase64(); // This should throw an exception due to circular reference
        });
    }

    [TestMethod]
    public void SerializeTest()
    {
        var map = new Map()
            .Set("1", "str1")
            .Set("2", (byte)123)
            .Set("3", (short)-2345)
            .Set("4", (ushort)34567)
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("7", (long)-67890123)
            .Set("8", (ulong)78901234)
            .Set("9", 3.14159f)
            .Set("10", 2.718281828459045d)
            .Set("11", 2718281828459045m)
            .Set("12", true)
            .Set("13", false)
            .Set("14", [1, 2, 3, 4, 5]);

        var map2 = new Map()
            .Set("1", "str1")
            .Set("2", (byte)123)
            .Set("3", (short)-2345)
            .Set("4", (ushort)34567)
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("7", (long)-67890123)
            .Set("8", (ulong)78901234)
            .Set("9", 3.14159f)
            .Set("10", 2.718281828459045d)
            .Set("11", 2718281828459045m)
            .Set("12", true)
            .Set("13", false)
            .Set("14", [1, 2, 3, 4, 5]);


        var map3 = new Map()
            .Set("1", "str1")
            .Set("2", (byte)123)
            .Set("3", (short)-2345)
            .Set("4", (ushort)34567)
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("7", (long)-67890123)
            .Set("8", (ulong)78901234)
            .Set("9", 3.14159f)
            .Set("10", 2.718281828459045d)
            .Set("11", 2718281828459045m)
            .Set("12", true)
            .Set("13", false)
            .Set("14", [1, 2, 3, 4, 5]);

        var map4 = Map.NewCaseInsensitive()
            .Set("A", "123123")
            .Set("a", string.Empty)
            .Lock();

        map.Set("child", map2);
        map2.Set("child", map3);
        map3.Set("child", map4);

        var bytes = map.ToBinaryData();
        Assert.IsTrue(bytes.Length > 10);

        using var ms = new MemoryStream(bytes.ToArray());

        map = Map.Deserialize(ms);

        Assert.IsNotNull(map);

        Assert.AreEqual("str1", map.Get<string>("1").Value);
        Assert.AreEqual((byte)123, map.Get<byte>("2").Value);
        Assert.AreEqual((short)-2345, map.Get<short>("3").Value);
        Assert.AreEqual((ushort)34567, map.Get<ushort>("4").Value);
        Assert.AreEqual((int)-456789, map.Get<int>("5").Value);
        Assert.AreEqual((uint)567890, map.Get<uint>("6").Value);
        Assert.AreEqual((long)-67890123, map.Get<long>("7").Value);
        Assert.AreEqual((ulong)78901234, map.Get<ulong>("8").Value);
        Assert.AreEqual(3.14159f, map.Get<float>("9").Value);
        Assert.AreEqual(2.718281828459045d, map.Get<double>("10").Value);
        Assert.AreEqual(2718281828459045m, map.Get<decimal>("11").Value);
        Assert.AreEqual(true, map.Get<bool>("12").Value);
        Assert.AreEqual(false, map.Get<bool>("13").Value);

        map = map.Get<Map>("child").Value;

        Assert.AreEqual("str1", map.Get<string>("1").Value);
        Assert.AreEqual((byte)123, map.Get<byte>("2").Value);
        Assert.AreEqual((short)-2345, map.Get<short>("3").Value);
        Assert.AreEqual((ushort)34567, map.Get<ushort>("4").Value);
        Assert.AreEqual((int)-456789, map.Get<int>("5").Value);
        Assert.AreEqual((uint)567890, map.Get<uint>("6").Value);
        Assert.AreEqual((long)-67890123, map.Get<long>("7").Value);
        Assert.AreEqual((ulong)78901234, map.Get<ulong>("8").Value);
        Assert.AreEqual(3.14159f, map.Get<float>("9").Value);
        Assert.AreEqual(2.718281828459045d, map.Get<double>("10").Value);
        Assert.AreEqual(2718281828459045m, map.Get<decimal>("11").Value);
        Assert.AreEqual(true, map.Get<bool>("12").Value);
        Assert.AreEqual(false, map.Get<bool>("13").Value);


        map = map.Get<Map>("child").Value;

        Assert.AreEqual("str1", map.Get<string>("1").Value);
        Assert.AreEqual((byte)123, map.Get<byte>("2").Value);
        Assert.AreEqual((short)-2345, map.Get<short>("3").Value);
        Assert.AreEqual((ushort)34567, map.Get<ushort>("4").Value);
        Assert.AreEqual((int)-456789, map.Get<int>("5").Value);
        Assert.AreEqual((uint)567890, map.Get<uint>("6").Value);
        Assert.AreEqual((long)-67890123, map.Get<long>("7").Value);
        Assert.AreEqual((ulong)78901234, map.Get<ulong>("8").Value);
        Assert.AreEqual(3.14159f, map.Get<float>("9").Value);
        Assert.AreEqual(2.718281828459045d, map.Get<double>("10").Value);
        Assert.AreEqual(2718281828459045m, map.Get<decimal>("11").Value);
        Assert.AreEqual(true, map.Get<bool>("12").Value);
        Assert.AreEqual(false, map.Get<bool>("13").Value);

        var m4 = map.Get<Map>("child").Value;

        Assert.AreEqual(string.Empty, m4.Get<string>("A").Value);
        Assert.IsTrue(m4.ReadOnly);
        Assert.IsFalse(m4.CaseSensitive);
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            m4.Set("newKey", "value");
        });

        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            m4.Delete("newKey");
        });
    }

    [TestMethod]
    public void PathTest()
    {
        var map = Map.NewCaseInsensitive()
            .SetPath("a.b.c", 1);

        Assert.AreEqual(1, map.GetPath<int>("a.b.c").Value);

        map.DeletePath("a.b");

        Assert.IsFalse(map.GetPath<int>("a.b.c").HasValue);
        Assert.IsFalse(map.GetPath<int>("a.b").HasValue);
    }

    [TestMethod]
    public void CloneTest()
    {

        var map = new Map()
            .Set("1", "str1")
            .Set("2", (byte)123)
            .Set("3", (short)-2345);

        var map2 = new Map()
            .Set("4", (ushort)34567)
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("7", (long)-67890123)
            .Set("8", (ulong)78901234)
            .Set("9", 3.14159f);


        var map3 = new Map()
            .Set("5", (int)-456789)
            .Set("6", (uint)567890)
            .Set("11", 2718281828459045m)
            .Set("12", true)
            .Set("13", false)
            .Set("14", [1, 2, 3, 4, 5]);

        var map4 = Map.NewCaseInsensitive()
            .Set("A", "123123")
            .Set("a", string.Empty)
            .Lock();

        map.Set("child", map2);
        map2.Set("child", map3);
        map3.Set("child", map4);

        var base64 = map.SerializeAsBase64();

        var cloned = (Map)map.Clone();
        var clonedBase64 = cloned.SerializeAsBase64();

        Assert.AreEqual(base64, clonedBase64);

    }

    [TestMethod]
    public void GuidTest()
    {
        var m = Map.New();
        var guid = Guid.NewGuid();


        m.Set("1", guid);

        var bytes = m.ToBinaryData();
        m = Map.Deserialize(bytes);

        Assert.IsNotNull(m);
        Assert.AreEqual(guid, m.Get<Guid>("1").Value);
    }

    [TestMethod]
    public void GuidTest2()
    {
        var m = Map.New();
        var guid = Guid.NewGuid();


        m.Set("1", guid, true);

        var bytes = m.ToBinaryData();
        m = Map.Deserialize(bytes);

        Assert.IsNotNull(m);
        Assert.AreEqual(guid.ToString(), m.Get<string>("1").Value);
    }

    [TestMethod]
    public void AutoConvertTest()
    {
        var m = Map.New();
        m.Set("1", "123");
        m.Set("2", "123.456");
        m.Set("3", "true");
        m.Set("4", "not a number");
        Assert.AreEqual(123, m.Get<int>("1").Value);
        Assert.AreEqual(123.456, m.Get<double>("2").Value);
        Assert.AreEqual(true, m.Get<bool>("3").Value);
        Assert.IsFalse(m.Get<int>("4").HasValue);
    }

    [TestMethod]
    public void DeserializeMemoryStressTest()
    {
        var m = Map.New();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            m.Set($"key{i}", $"value{i}");
        }
        sw.Stop();

        Console.WriteLine($"Map set 10000 items took {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        var bytes = m.ToBinaryData();
        sw.Stop();

        Console.WriteLine($"Map serialize 10000 items took {sw.ElapsedMilliseconds} ms, size: {bytes.Length} bytes");

        sw.Restart();
        for (int i = 0; i < 10000; i++)
        {
            var m2 = Map.Deserialize(bytes);
        }
        sw.Stop();

        Console.WriteLine($"Map deserialize 10000 items(10000 times) took {sw.ElapsedMilliseconds} ms");
    }
}