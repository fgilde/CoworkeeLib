using System.Buffers.Text;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace Aspire.Hosting;

/// <summary>One half of a VAPID key pair made on first use (base64url: the uncompressed P-256 public key or the private scalar); both halves share the pair.</summary>
internal sealed class VapidKeyDefault(Lazy<(string Public, string Private)> pair, bool privateKey) : ParameterDefault
{
    public override string GetDefaultValue() => privateKey ? pair.Value.Private : pair.Value.Public;

    // a deployment brings its own pair: nothing to generate in the manifest
    public override void WriteToManifest(ManifestPublishingContext context)
    {
    }

    public static (string Public, string Private) Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (Base64Url.EncodeToString([4, .. parameters.Q.X!, .. parameters.Q.Y!]), Base64Url.EncodeToString(parameters.D));
    }
}
