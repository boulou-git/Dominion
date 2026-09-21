#if UNITY_INCLUDE_TESTS
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NetworkGameStatePhotonPayloadTests
{
    [Test]
    public void Decode_RejectsCompressedExpansionBeyondLimit()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            stream.WriteByte(2);
            using (GZipStream gzip = new GZipStream(stream, CompressionMode.Compress, true))
            {
                byte[] block = new byte[8192];
                for (int i = 0; i <= NetworkGameStatePhotonPayload.MaxDecodedBytes / block.Length; i++)
                    gzip.Write(block, 0, block.Length);
            }
            Assert.IsFalse(NetworkGameStatePhotonPayload.TryDecode(stream.ToArray(), out string decoded));
            Assert.IsNull(decoded);
        }
    }

    [Test]
    public void MalformedJson_IsRejectedWithoutReplacingCurrentState()
    {
        GameStateSnapshot previous = NetworkGameState.State;
        MethodInfo apply = typeof(NetworkGameState).GetMethod("ApplyJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(apply);
        LogAssert.Expect(LogType.Warning, new Regex("Ignoring malformed replicated game state:"));
        Assert.AreEqual(false, apply.Invoke(null, new object[] { "{invalid", false }));
        Assert.AreSame(previous, NetworkGameState.State);
    }

    [Test]
    public void PayloadLimits_RejectOversizedRawAndLegacyData()
    {
        string huge = new string('x', NetworkGameStatePhotonPayload.MaxDecodedBytes + 1);
        Assert.IsNull(NetworkGameStatePhotonPayload.Encode(huge));
        Assert.IsFalse(NetworkGameStatePhotonPayload.TryDecode(huge, out _));
        byte[] raw = new byte[NetworkGameStatePhotonPayload.MaxDecodedBytes + 2];
        raw[0] = 1;
        Assert.IsFalse(NetworkGameStatePhotonPayload.TryDecode(raw, out _));
    }

    [Test]
    public void JournalPlainText_CannotInjectRichTextOrShiftLinkOffsets()
    {
        const string input = "<b>Joueur</b> <size=99>chat</size>";
        string safe = PublicJournalView.SafePlainText(input);
        Assert.AreEqual(input.Length, safe.Length);
        Assert.IsFalse(safe.Contains("<"));
        Assert.IsFalse(safe.Contains(">"));
        Assert.AreEqual(string.Empty, PublicJournalView.SafePlainText(null));
    }

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
