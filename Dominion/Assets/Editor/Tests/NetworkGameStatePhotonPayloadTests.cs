#if UNITY_INCLUDE_TESTS
using System.Text;
using NUnit.Framework;

public sealed class NetworkGameStatePhotonPayloadTests
{
    [Test]
    public void EncodeDecode_RoundTripsJsonBeyondPhotonStringLimit()
    {
        string json = "{\"payload\":\"" + new string('x', 40000) + "\"}";
        int utf8Length = Encoding.UTF8.GetByteCount(json);
        Assert.Greater(utf8Length, short.MaxValue);

        byte[] payload = NetworkGameStatePhotonPayload.Encode(json);

        Assert.IsNotNull(payload);
        Assert.Greater(payload.Length, 0);
        Assert.Less(payload.Length, utf8Length);
        Assert.IsTrue(NetworkGameStatePhotonPayload.TryDecode(payload, out string decoded));
        Assert.AreEqual(json, decoded);
    }

    [Test]
    public void TryDecode_AcceptsLegacyStringPayload()
    {
        const string json = "{\"legacy\":true}";

        Assert.IsTrue(NetworkGameStatePhotonPayload.TryDecode(json, out string decoded));
        Assert.AreEqual(json, decoded);
    }
}
#endif
